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
    public int Rank { get; init; } = 8;
    public float Alpha { get; init; } = 32.0f;
    public float LoRADropout { get; init; } = 0.0f;
    public string TargetModules { get; init; } = "qv";
    public int Epochs { get; init; } = 200;
    public int BatchSize { get; init; } = 4;
    public float LearningRate { get; init; } = 1e-4f;
    public float WeightDecay { get; init; } = 0.01f;
    public int MaxLength { get; init; } = 512;
    public float ValSplit { get; init; } = 0.0f;
    public int Patience { get; init; } = 10;
    public bool NoEarlyStopping { get; init; } = false;
    /// <summary>
    /// Minimum improvement in train loss to reset patience counter. Default: 0.001
    /// </summary>
    public float MinDelta { get; init; } = 0.001f;
}

/// <summary>
/// Result of a LoRA fine-tuning run.
/// </summary>
public sealed record TrainingResult(float BestLoss, string OutputPath, int EpochsRun, bool EarlyStopped);

/// <summary>
/// LoRA fine-tuning trainer. Ports finetune.py training loop.
/// </summary>
public sealed class LoRATrainer
{
    private readonly TrainingConfig _config;
    private readonly TextWriter _log;

    public LoRATrainer(TrainingConfig config, TextWriter? log = null)
    {
        _config = config;
        _log = log ?? Console.Out;
    }

    public TrainingResult Run()
    {
        int actualEpochs = 0;
        bool earlyStopped = false;
        _log.WriteLine(new string('=', 60));
        _log.WriteLine("SharpTinyCoder LoRA Fine-Tuning (C#/TorchSharp)");
        _log.WriteLine(new string('=', 60));

        var device = torch.cuda.is_available() ? torch.CUDA : torch.CPU;
        _log.WriteLine($"Device: {device}");

        // 1. Load tokenizer
        _log.WriteLine($"\n[1/6] Loading tokenizer from {_config.TokenizerPath}...");
        var tokenizer = new SharpOpsTokenizer(_config.TokenizerPath);
        _log.WriteLine($"  Vocab size: {tokenizer.VocabSize}");

        // 2. Load base model
        _log.WriteLine($"\n[2/6] Loading base model from {_config.CheckpointPath}...");
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
        _log.WriteLine($"  Base model parameters: {model.NumParameters(trainableOnly: false):N0}");

        // 3. Attach LoRA
        var targetModules = LoRAModel.ResolveTargetModules(_config.TargetModules);
        _log.WriteLine($"\n[3/6] Attaching LoRA (rank={_config.Rank}, alpha={_config.Alpha}, modules={_config.TargetModules})...");
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
        _log.WriteLine($"  Trainable: {trainableParams:N0} ({100.0 * trainableParams / totalParams:F2}%)");

        // 4. Load data
        _log.WriteLine($"\n[4/6] Loading fine-tune data from {_config.DataPath}...");
        var allExamples = DataLoader.LoadData(_config.DataPath);
        _log.WriteLine($"  Total examples: {allExamples.Count}");

        if (allExamples.Count == 0)
        {
            _log.WriteLine("ERROR: No training examples found!");
            return new TrainingResult(float.NaN, "", 0, false);
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
        _log.WriteLine($"  Train: {trainExamples.Count}, Val: {valExamples.Count}");

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
        _log.WriteLine($"\n[5/6] Training for {_config.Epochs} epochs ({esInfo})...");
        _log.WriteLine($"  Batch size: {_config.BatchSize}");
        _log.WriteLine($"  Learning rate: {_config.LearningRate}");
        _log.WriteLine($"  Steps per epoch: {stepsPerEpoch}");
        _log.WriteLine();

        float bestValLoss = float.PositiveInfinity;
        int patienceCounter = 0;
        Dictionary<string, Tensor>? bestState = null;


        for (int epoch = 0; epoch < _config.Epochs; epoch++)
        {
            actualEpochs = epoch + 1;

            // Training
            loraModel.train();
            var trainLosses = new List<float>();
            var batches = trainDataset.GetBatchIndices(_config.BatchSize, shuffle: true);

            for (int step = 0; step < batches.Count; step++)
            {
                var (inputIds, labels, attentionMask) = trainDataset.GetBatch(batches[step]);

                // Move to device, disposing CPU tensors
                var gpuInputIds = inputIds.to(device);
                var gpuLabels = labels.to(device);
                var gpuMask = attentionMask.to(device);
                inputIds.Dispose();
                labels.Dispose();
                attentionMask.Dispose();

                optimizer.zero_grad();

                var (logits, loss) = loraModel.forward(gpuInputIds, gpuMask, gpuLabels);
                loss!.backward();

                // Gradient clipping
                torch.nn.utils.clip_grad_norm_(loraParams, 1.0);

                optimizer.step();

                var lossVal = loss.item<float>();
                trainLosses.Add(lossVal);

                // Dispose all tensors immediately
                logits.Dispose();
                loss.Dispose();
                gpuInputIds.Dispose();
                gpuLabels.Dispose();
                gpuMask.Dispose();

                // Force cleanup of TorchSharp C# wrappers holding native GPU tensor references
                GC.Collect();
                GC.WaitForPendingFinalizers();


                // Progress every 10 steps
                if ((step + 1) % 10 == 0 || step == batches.Count - 1)
                {
                    _log.Write($"\r  Epoch {epoch + 1,3}/{_config.Epochs} | Step {step + 1}/{batches.Count} | Loss: {lossVal:F4}");
                }
            }

            var avgTrainLoss = trainLosses.Average();

            // Validation + early stopping (only when val split is configured)
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

                        var gpuInputIds = inputIds.to(device);
                        var gpuLabels = labels.to(device);
                        var gpuMask = attentionMask.to(device);
                        inputIds.Dispose();
                        labels.Dispose();
                        attentionMask.Dispose();

                        var (logits, loss) = loraModel.forward(gpuInputIds, gpuMask, gpuLabels);
                        valLosses.Add(loss!.item<float>());

                        logits.Dispose();
                        loss.Dispose();
                        gpuInputIds.Dispose();
                        gpuLabels.Dispose();
                        gpuMask.Dispose();
                    }
                }

                var avgValLoss = valLosses.Average();

                // Early stopping on val loss
                if (avgValLoss < bestValLoss - _config.MinDelta)
                {
                    bestValLoss = avgValLoss;
                    patienceCounter = 0;
                    bestState = SaveLoRAState(loraModel);
                }
                else
                {
                    patienceCounter++;
                }

                _log.WriteLine($"\rEpoch {epoch + 1,3}/{_config.Epochs} | Train: {avgTrainLoss:F4} | Val: {avgValLoss:F4} | Best: {bestValLoss:F4} | Patience: {patienceCounter}/{_config.Patience}");

                if (!_config.NoEarlyStopping && patienceCounter >= _config.Patience)
                {
                    _log.WriteLine($"\nEarly stopping: val loss hasn't improved for {_config.Patience} epochs");
                    earlyStopped = true;
                    break;
                }
            }
            else
            {
                // No val split: just log train loss, no early stopping (matches Python)
                _log.WriteLine($"\rEpoch {epoch + 1,3}/{_config.Epochs} | Train: {avgTrainLoss:F4}");
            }
        }

        // Restore best LoRA weights if we have them (only set when val split is used)
        if (bestState is not null)
        {
            RestoreLoRAState(loraModel, bestState);
        }

        // 6. Merge LoRA weights
        _log.WriteLine($"\n[6/6] Merging LoRA weights...");
        var mergedModel = loraModel.MergeLoRA();
        mergedModel.to(torch.CPU);

        // Save merged checkpoint
        var outputPath = _config.OutputPath ?? GetDefaultOutputPath(_config.CheckpointPath);
        _log.WriteLine($"\nSaving merged model to {outputPath}...");
        SaveCheckpoint(mergedModel, outputPath, bestValLoss);

        _log.WriteLine($"\n{new string('=', 60)}");
        _log.WriteLine("Fine-tuning complete!");
        _log.WriteLine($"  Best loss: {bestValLoss:F6}");
        _log.WriteLine($"  Merged checkpoint: {outputPath}");
        _log.WriteLine($"  Epochs: {actualEpochs}{(earlyStopped ? " (early stopped)" : "")}");
        _log.WriteLine(new string('=', 60));

        return new TrainingResult(bestValLoss, outputPath, actualEpochs, earlyStopped);
    }
    /// <summary>
    /// Loads a checkpoint, trying Python format first, then container extraction, then TorchSharp native format for finetuned checkpoints.
    /// </summary>
    /// <param name="model"></param>
    /// <param name="checkpointPath"></param>
    private void LoadCheckpoint(SharpTinyCoder model, string checkpointPath)
    {
        try
        {
            // Try Python-format checkpoint first
            model.load_py(checkpointPath, strict: false);
            _log.WriteLine($"  Loaded checkpoint from {checkpointPath} (Python format)");
        }
        catch (InvalidCastException)
        {
            // Container format - extract state_dict via Python
            _log.WriteLine("  Checkpoint has container format, extracting state_dict via Python...");
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
                _log.WriteLine($"  Loaded checkpoint from {checkpointPath} (via state_dict extraction)");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        catch (Exception) when (!checkpointPath.EndsWith("_py.pt"))
        {
            // Fall back to TorchSharp native format (LoRA finetuned output)
            model.load(checkpointPath, strict: false);
            _log.WriteLine($"  Loaded checkpoint from {checkpointPath} (TorchSharp native format)");
        }

        // Diagnostic: verify weights loaded
        var namedParams = model.named_parameters().ToList();
        _log.WriteLine($"  Model parameters: {namedParams.Count}");
        if (namedParams.Count > 0)
        {
            var first = namedParams[0];
            var data = first.parameter.data<float>();
            _log.WriteLine($"  First param '{first.name}': shape={string.Join("x", first.parameter.shape)}, first5=[{data[0]:F6}, {data[1]:F6}, {data[2]:F6}, {data[3]:F6}, {data[4]:F6}]");
        }
    }
    /// <summary>
    /// Saves merged model checkpoint by serializing parameters as raw binary and using Python torch.save. Uses UseShellExecute=true to avoid process I/O deadlock.
    /// </summary>
    /// <param name="model"></param>
    /// <param name="outputPath"></param>
    /// <param name="bestLoss"></param>
    private void SaveCheckpoint(SharpTinyCoder model, string outputPath, float bestLoss)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (dir != null) Directory.CreateDirectory(dir);

        var namedParams = model.named_parameters().ToList();
        _log.WriteLine($"  Parameters to save: {namedParams.Count}");

        // TorchSharp save_py is broken — manually serialize via Python
        var tmpDir = Path.Combine(Path.GetTempPath(), $"ckpt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Write each parameter as raw float32 binary + manifest
            var manifest = new List<string>();
            foreach (var (name, param) in namedParams)
            {
                var cpuParam = param.cpu().contiguous();
                var data = cpuParam.data<float>().ToArray();
                var binPath = Path.Combine(tmpDir, $"{manifest.Count}.bin");
                var bytes = new byte[data.Length * 4];
                Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
                File.WriteAllBytes(binPath, bytes);
                manifest.Add($"{name}|{string.Join(",", cpuParam.shape)}");
            }
            File.WriteAllLines(Path.Combine(tmpDir, "manifest.txt"), manifest);

            // Python script to assemble into torch checkpoint
            var script = "import torch,struct,os,sys\n" +
                "d=sys.argv[1]; o=sys.argv[2]\n" +
                "sd={}\n" +
                "for line in open(os.path.join(d,'manifest.txt')):\n" +
                "  line=line.strip()\n" +
                "  if not line: continue\n" +
                "  idx=len(sd); name,sh=line.split('|')\n" +
                "  shape=[int(x) for x in sh.split(',') if x]\n" +
                "  data=open(os.path.join(d,f'{idx}.bin'),'rb').read()\n" +
                "  n=len(data)//4\n" +
                "  sd[name]=torch.tensor(struct.unpack(f'{n}f',data),dtype=torch.float32).reshape(shape) if shape else torch.tensor(struct.unpack(f'{n}f',data),dtype=torch.float32)\n" +
                "torch.save(sd,o)\n";
            var scriptPath = Path.Combine(tmpDir, "assemble.py");
            File.WriteAllText(scriptPath, script);

            // Don't redirect stdout/stderr to avoid process deadlock
            var errPath = Path.Combine(tmpDir, "stderr.txt");
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\" \"{tmpDir}\" \"{outputPath}\" 2>\"{errPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi)!;
            if (!proc.WaitForExit(60000))
            {
                proc.Kill();
                throw new InvalidOperationException("Python checkpoint save timed out after 60s");
            }

            if (proc.ExitCode != 0)
            {
                var err = File.Exists(errPath) ? File.ReadAllText(errPath) : "unknown error";
                _log.WriteLine($"  Python save failed (exit {proc.ExitCode}): {err}");
                throw new InvalidOperationException($"Checkpoint save via Python failed: {err}");
            }

            var size = new FileInfo(outputPath).Length;
            _log.WriteLine($"  Saved {namedParams.Count} params — {size:N0} bytes (Python format)");
            if (size < 1000)
            {
                throw new InvalidOperationException($"Checkpoint suspiciously small ({size} bytes)");
            }
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
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
