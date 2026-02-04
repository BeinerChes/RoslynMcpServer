using SharpOps.Inference;
using SharpOps.Torch.LoRA;
using SharpOps.Model;
using System.Diagnostics;
using System.Text.Json;
using TorchSharp;
using TorchSharp.Modules;
using TorchSharp.PyBridge;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace SharpOps.Torch.Training;

/// <summary>
/// Training configuration.
/// </summary>
public sealed class TrainingConfig
{
    public required string DataPath { get; init; }
    public string CheckpointPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "Models", "checkpoint.pt");
    public string? OutputPath { get; init; }
    public string TokenizerPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "Models", "tokenizer.json");
    public int Rank { get; init; } = 16;
    public float Alpha { get; init; } = 32.0f;
    public float LoRADropout { get; init; } = 0.0f;
    public string TargetModules { get; init; } = "all";
    public int Epochs { get; init; } = 50;
    public int BatchSize { get; init; } = 4;
    public float LearningRate { get; init; } = 1e-4f;
    public float WeightDecay { get; init; } = 0.01f;
    public int MaxLength { get; init; } = 512;
    public float ValSplit { get; init; } = 0.0f;
    public int Patience { get; init; } = 10;
    public bool NoEarlyStopping { get; init; } = false;
}

/// <summary>
/// LoRA fine-tuning trainer. Ports finetune.py training loop.
/// </summary>
public sealed class LoRATrainer
{
    private readonly TrainingConfig _config;

    public LoRATrainer(TrainingConfig config)
    {
        _config = config;
    }

    public void Run()
    {
        Console.WriteLine(new string('=', 60));
        Console.WriteLine("SharpTinyCoder LoRA Fine-Tuning (C#/TorchSharp)");
        Console.WriteLine(new string('=', 60));

        var device = torch.cuda.is_available() ? torch.CUDA : torch.CPU;
        Console.WriteLine($"Device: {device}");

        // 1. Load tokenizer
        Console.WriteLine($"\n[1/6] Loading tokenizer from {_config.TokenizerPath}...");
        var tokenizer = new SharpOpsTokenizer(_config.TokenizerPath);
        Console.WriteLine($"  Vocab size: {tokenizer.VocabSize}");

        // 2. Load base model
        Console.WriteLine($"\n[2/6] Loading base model from {_config.CheckpointPath}...");
        var modelConfig = new ModelConfig
        {
            VocabSize = tokenizer.VocabSize,
            ContextLength = _config.MaxLength,
            EmbeddingDim = ModelConfig.ConfigTiny.EmbeddingDim,
            NumLayers = ModelConfig.ConfigTiny.NumLayers,
            NumHeads = ModelConfig.ConfigTiny.NumHeads,
            NumKvHeads = ModelConfig.ConfigTiny.NumKvHeads,
            FfHiddenDim = ModelConfig.ConfigTiny.FfHiddenDim,
            Dropout = 0.0f, // No dropout for fine-tuning
            TieEmbeddings = ModelConfig.ConfigTiny.TieEmbeddings,
        };
        var model = new SharpTinyCoder(modelConfig);

        // Load checkpoint via PyBridge
        LoadCheckpoint(model, _config.CheckpointPath);
        Console.WriteLine($"  Base model parameters: {model.NumParameters(trainableOnly: false):N0}");

        // 3. Attach LoRA
        var targetModules = LoRAModel.ResolveTargetModules(_config.TargetModules);
        Console.WriteLine($"\n[3/6] Attaching LoRA (rank={_config.Rank}, alpha={_config.Alpha}, modules={_config.TargetModules})...");
        var loraModel = new LoRAModel(
            model,
            rank: _config.Rank,
            alpha: _config.Alpha,
            dropout: _config.LoRADropout,
            targetModules: targetModules);

        // Move to device
        loraModel.to(device);

        var trainableParams = loraModel.NumLoRAParameters();
        var totalParams = model.NumParameters(trainableOnly: false);
        Console.WriteLine($"  Trainable: {trainableParams:N0} ({100.0 * trainableParams / totalParams:F2}%)");

        // 4. Load data
        Console.WriteLine($"\n[4/6] Loading fine-tune data from {_config.DataPath}...");
        var allExamples = DataLoader.LoadData(_config.DataPath);
        Console.WriteLine($"  Total examples: {allExamples.Count}");

        if (allExamples.Count == 0)
        {
            Console.Error.WriteLine("ERROR: No training examples found!");
            return;
        }

        // Shuffle and split
        var rng = new Random(42);
        var shuffled = allExamples.OrderBy(_ => rng.Next()).ToList();

        List<Dictionary<string, JsonElement>> trainExamples;
        List<Dictionary<string, JsonElement>> valExamples;

        if (_config.ValSplit > 0)
        {
            var valSize = Math.Max(1, (int)(shuffled.Count * _config.ValSplit));
            valExamples = shuffled.Take(valSize).ToList();
            trainExamples = shuffled.Skip(valSize).ToList();
        }
        else
        {
            trainExamples = shuffled;
            valExamples = [];
        }
        Console.WriteLine($"  Train: {trainExamples.Count}, Val: {valExamples.Count}");

        // Create datasets
        var trainDataset = new FinetuneDataset(trainExamples, tokenizer, _config.MaxLength);
        FinetuneDataset? valDataset = valExamples.Count > 0
            ? new FinetuneDataset(valExamples, tokenizer, _config.MaxLength)
            : null;

        // 5. Optimizer - only LoRA parameters
        var loraParams = loraModel.LoRAParameters().ToList();
        var optimizer = torch.optim.AdamW(
            loraParams,
            lr: _config.LearningRate,
            weight_decay: _config.WeightDecay);

        // Training loop
        var esInfo = _config.NoEarlyStopping ? "disabled" : $"patience={_config.Patience}";
        var stepsPerEpoch = (trainDataset.Count + _config.BatchSize - 1) / _config.BatchSize;
        Console.WriteLine($"\n[5/6] Training for {_config.Epochs} epochs ({esInfo})...");
        Console.WriteLine($"  Batch size: {_config.BatchSize}");
        Console.WriteLine($"  Learning rate: {_config.LearningRate}");
        Console.WriteLine($"  Steps per epoch: {stepsPerEpoch}");
        Console.WriteLine();

        float bestValLoss = float.PositiveInfinity;
        int patienceCounter = 0;
        Dictionary<string, Tensor>? bestState = null;

        for (int epoch = 0; epoch < _config.Epochs; epoch++)
        {
            // Training
            loraModel.train();
            var trainLosses = new List<float>();
            var batches = trainDataset.GetBatchIndices(_config.BatchSize, shuffle: true);

            for (int step = 0; step < batches.Count; step++)
            {
                var (inputIds, labels, attentionMask) = trainDataset.GetBatch(batches[step]);
                inputIds = inputIds.to(device);
                labels = labels.to(device);
                attentionMask = attentionMask.to(device);

                optimizer.zero_grad();

                var (_, loss) = loraModel.forward(inputIds, attentionMask, labels);
                loss!.backward();

                // Gradient clipping
                torch.nn.utils.clip_grad_norm_(loraParams, 1.0);

                optimizer.step();

                var lossVal = loss.item<float>();
                trainLosses.Add(lossVal);

                // Dispose tensors to free memory
                inputIds.Dispose();
                labels.Dispose();
                attentionMask.Dispose();
                loss.Dispose();

                // Progress every 10 steps
                if ((step + 1) % 10 == 0 || step == batches.Count - 1)
                {
                    Console.Write($"\r  Epoch {epoch + 1,3}/{_config.Epochs} | Step {step + 1}/{batches.Count} | Loss: {lossVal:F4}");
                }
            }

            var avgTrainLoss = trainLosses.Average();

            // Validation
            if (valDataset is not null)
            {
                loraModel.eval();
                var valLosses = new List<float>();

                using (torch.no_grad())
                {
                    var valBatches = valDataset.GetBatchIndices(_config.BatchSize, shuffle: false);
                    foreach (var batch in valBatches)
                    {
                        var (inputIds, labels, attentionMask) = valDataset.GetBatch(batch);
                        inputIds = inputIds.to(device);
                        labels = labels.to(device);
                        attentionMask = attentionMask.to(device);

                        var (_, loss) = loraModel.forward(inputIds, attentionMask, labels);
                        valLosses.Add(loss!.item<float>());

                        inputIds.Dispose();
                        labels.Dispose();
                        attentionMask.Dispose();
                        loss.Dispose();
                    }
                }

                var avgValLoss = valLosses.Average();

                // Early stopping check
                if (avgValLoss < bestValLoss - 0.01f)
                {
                    bestValLoss = avgValLoss;
                    patienceCounter = 0;
                    // Save best LoRA state
                    bestState = SaveLoRAState(loraModel);
                }
                else
                {
                    patienceCounter++;
                }

                Console.WriteLine($"\rEpoch {epoch + 1,3}/{_config.Epochs} | Train: {avgTrainLoss:F4} | Val: {avgValLoss:F4} | Best: {bestValLoss:F4} | Patience: {patienceCounter}/{_config.Patience}");

                if (!_config.NoEarlyStopping && patienceCounter >= _config.Patience)
                {
                    Console.WriteLine($"\nEarly stopping: val loss hasn't improved for {_config.Patience} epochs");
                    break;
                }
            }
            else
            {
                Console.WriteLine($"\rEpoch {epoch + 1,3}/{_config.Epochs} | Train: {avgTrainLoss:F4}                              ");

                // Track train loss as "best" when no val split
                if (avgTrainLoss < bestValLoss)
                    bestValLoss = avgTrainLoss;
            }
        }

        // Restore best LoRA weights if we have them
        if (bestState is not null)
        {
            RestoreLoRAState(loraModel, bestState);
        }

        // 6. Merge LoRA weights
        Console.WriteLine($"\n[6/6] Merging LoRA weights...");
        var mergedModel = loraModel.MergeLoRA();
        mergedModel.to(torch.CPU);

        // Save merged checkpoint
        var outputPath = _config.OutputPath ?? GetDefaultOutputPath(_config.CheckpointPath);
        Console.WriteLine($"\nSaving merged model to {outputPath}...");
        SaveCheckpoint(mergedModel, outputPath, bestValLoss);
        Console.WriteLine("  Saved!");

        Console.WriteLine($"\n{new string('=', 60)}");
        Console.WriteLine("Fine-tuning complete!");
        Console.WriteLine($"  Best loss: {bestValLoss:F6}");
        Console.WriteLine($"  Merged checkpoint: {outputPath}");
        Console.WriteLine(new string('=', 60));
    }
    /// <summary>
    /// Loads a Python checkpoint, handling both raw state_dict and container dict formats. Falls back to Python subprocess extraction when PyBridge can't parse the container format.
    /// </summary>
    /// <param name="model"></param>
    /// <param name="checkpointPath"></param>
    private static void LoadCheckpoint(SharpTinyCoder model, string checkpointPath)
    {
        try
        {
            model.load_py(checkpointPath, strict: false);
            Console.Error.WriteLine($"  Loaded checkpoint from {checkpointPath}");
        }
        catch (InvalidCastException)
        {
            Console.Error.WriteLine("  Checkpoint has container format, extracting state_dict via Python...");
            var tempPath = Path.Combine(Path.GetTempPath(), $"statedict_{Guid.NewGuid():N}.pt");
            try
            {
                var checkpointDir = Path.GetDirectoryName(checkpointPath) ?? ".";
                var parentDir = Path.GetDirectoryName(checkpointDir) ?? checkpointDir;

                var script = $"import sys; sys.path.insert(0, r'{parentDir}'); sys.path.insert(0, r'{checkpointDir}'); " +
                             $"import torch; ckpt = torch.load(r'{checkpointPath}', map_location='cpu', weights_only=False); " +
                             $"sd = ckpt['model_state_dict'] if 'model_state_dict' in ckpt else ckpt; " +
                             $"torch.save(sd, r'{tempPath}')";

                var psi = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"-c \"{script}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi)!;
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    var err = proc.StandardError.ReadToEnd();
                    throw new Exception($"Python state_dict extraction failed: {err}");
                }

                model.load_py(tempPath, strict: false);
                Console.Error.WriteLine($"  Loaded checkpoint from {checkpointPath} (via state_dict extraction)");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }

    private static void SaveCheckpoint(SharpTinyCoder model, string outputPath, float bestLoss)
    {
        // Save using PyBridge for Python compatibility
        var dir = Path.GetDirectoryName(outputPath);
        if (dir != null) Directory.CreateDirectory(dir);

        model.save_py(outputPath);
    }

    private static Dictionary<string, Tensor> SaveLoRAState(LoRAModel model)
    {
        var state = new Dictionary<string, Tensor>();
        foreach (var (name, param) in model.named_parameters())
        {
            if (name.Contains("lora_"))
            {
                state[name] = param.detach().clone();
            }
        }
        return state;
    }

    private static void RestoreLoRAState(LoRAModel model, Dictionary<string, Tensor> state)
    {
        using (torch.no_grad())
        {
            foreach (var (name, param) in model.named_parameters())
            {
                if (state.TryGetValue(name, out var savedTensor))
                {
                    param.copy_(savedTensor);
                }
            }
        }
    }

    private static string GetDefaultOutputPath(string inputPath)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(dir, $"{name}_lora.pt");
    }
}
