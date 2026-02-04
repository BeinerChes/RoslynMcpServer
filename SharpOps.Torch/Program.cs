using SharpOps.Torch.Training;
using TorchSharp.PyBridge;

namespace SharpOps.Torch;

public static class Program
{
    private static readonly string DefaultCheckpoint = Path.Combine(AppContext.BaseDirectory, "Models", "checkpoint.pt");
    private static readonly string DefaultTokenizer = Path.Combine(AppContext.BaseDirectory, "Models", "tokenizer.json");

    public static void Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return;
        }

        if (args[0] == "finetune")
        {
            RunFinetune(args[1..]);
        }
        else if (args[0] == "smoke-test")
        {
            RunSmokeTest(args[1..]);
        }
        else
        {
            Console.Error.WriteLine($"Unknown command: {args[0]}");
            PrintUsage();
            Environment.ExitCode = 1;
        }
    }

    private static void RunFinetune(string[] args)
    {
        string? dataPath = null;
        string checkpointPath = DefaultCheckpoint;
        string? outputPath = null;
        string tokenizerPath = DefaultTokenizer;
        int rank = 16;
        float alpha = 32.0f;
        float loraDropout = 0.0f;
        string targetModules = "all";
        int epochs = 50;
        int batchSize = 4;
        float lr = 1e-4f;
        float weightDecay = 0.01f;
        int maxLength = 512;
        float valSplit = 0.0f;
        int patience = 10;
        bool noEarlyStopping = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--data":
                    dataPath = args[++i];
                    break;
                case "--checkpoint":
                    checkpointPath = args[++i];
                    break;
                case "--output":
                    outputPath = args[++i];
                    break;
                case "--tokenizer":
                    tokenizerPath = args[++i];
                    break;
                case "--rank":
                    rank = int.Parse(args[++i]);
                    break;
                case "--alpha":
                    alpha = float.Parse(args[++i]);
                    break;
                case "--dropout":
                    loraDropout = float.Parse(args[++i]);
                    break;
                case "--target-modules":
                    targetModules = args[++i];
                    break;
                case "--epochs":
                    epochs = int.Parse(args[++i]);
                    break;
                case "--batch-size":
                    batchSize = int.Parse(args[++i]);
                    break;
                case "--lr":
                    lr = float.Parse(args[++i]);
                    break;
                case "--weight-decay":
                    weightDecay = float.Parse(args[++i]);
                    break;
                case "--max-length":
                    maxLength = int.Parse(args[++i]);
                    break;
                case "--val-split":
                    valSplit = float.Parse(args[++i]);
                    break;
                case "--patience":
                    patience = int.Parse(args[++i]);
                    break;
                case "--no-early-stopping":
                    noEarlyStopping = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    Environment.ExitCode = 1;
                    return;
            }
        }

        if (dataPath == null)
        {
            Console.Error.WriteLine("ERROR: --data is required");
            Environment.ExitCode = 1;
            return;
        }

        var config = new TrainingConfig
        {
            DataPath = dataPath,
            CheckpointPath = checkpointPath,
            OutputPath = outputPath,
            TokenizerPath = tokenizerPath,
            Rank = rank,
            Alpha = alpha,
            LoRADropout = loraDropout,
            TargetModules = targetModules,
            Epochs = epochs,
            BatchSize = batchSize,
            LearningRate = lr,
            WeightDecay = weightDecay,
            MaxLength = maxLength,
            ValSplit = valSplit,
            Patience = patience,
            NoEarlyStopping = noEarlyStopping,
        };

        var trainer = new LoRATrainer(config);
        trainer.Run();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            SharpOps.Torch - LoRA Fine-Tuning for SharpTinyCoder

            Usage:
              SharpOps.Torch finetune --data <path> [options]

            Commands:
              finetune    Fine-tune a checkpoint using LoRA

            Finetune Options:
              --data <path>           Path to JSONL file or folder (required)
              --checkpoint <path>     Input checkpoint path (default: D:\RMS\CSharpRobot\checkpoints\checkpoint.pt)
              --output <path>         Output checkpoint path (default: <input>_lora.pt)
              --tokenizer <path>      Tokenizer path (default: D:\RMS\CSharpRobot\tokenizer\tokenizer.json)
              --rank <int>            LoRA rank (default: 16)
              --alpha <float>         LoRA alpha (default: 32)
              --dropout <float>       LoRA dropout (default: 0)
              --target-modules <str>  Target modules: qv, attention, ffn, all (default: all)
              --epochs <int>          Number of epochs (default: 50)
              --batch-size <int>      Batch size (default: 4)
              --lr <float>            Learning rate (default: 1e-4)
              --weight-decay <float>  Weight decay (default: 0.01)
              --max-length <int>      Max sequence length (default: 512)
              --val-split <float>     Validation split fraction (default: 0.0)
              --patience <int>        Early stopping patience (default: 10)
              --no-early-stopping     Disable early stopping
            """);
    }

    private static void RunSmokeTest(string[] args)
    {
        string checkpointPath = DefaultCheckpoint;
        string tokenizerPath = DefaultTokenizer;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--checkpoint": checkpointPath = args[++i]; break;
                case "--tokenizer": tokenizerPath = args[++i]; break;
            }
        }

        Console.WriteLine("=== Smoke Test ===");

        // 1. Load tokenizer
        Console.WriteLine("\n[1/4] Loading tokenizer...");
        var tokenizer = new SharpOps.Inference.SharpOpsTokenizer(tokenizerPath);
        Console.WriteLine($"  Vocab size: {tokenizer.VocabSize}");

        // 2. Create model
        Console.WriteLine("\n[2/4] Creating model...");
        var config = new SharpOps.Model.ModelConfig
        {
            VocabSize = tokenizer.VocabSize,
            ContextLength = SharpOps.Model.ModelConfig.ConfigTiny.ContextLength,
            EmbeddingDim = SharpOps.Model.ModelConfig.ConfigTiny.EmbeddingDim,
            NumLayers = SharpOps.Model.ModelConfig.ConfigTiny.NumLayers,
            NumHeads = SharpOps.Model.ModelConfig.ConfigTiny.NumHeads,
            NumKvHeads = SharpOps.Model.ModelConfig.ConfigTiny.NumKvHeads,
            FfHiddenDim = SharpOps.Model.ModelConfig.ConfigTiny.FfHiddenDim,
            Dropout = 0.0f,
            TieEmbeddings = SharpOps.Model.ModelConfig.ConfigTiny.TieEmbeddings,
        };
        var model = new SharpOps.Model.SharpTinyCoder(config);
        var totalParams = model.NumParameters(trainableOnly: false);
        Console.WriteLine($"  Parameters: {totalParams:N0}");

        // 3. Load checkpoint (handles both raw state_dict and container format)
        Console.WriteLine($"\n[3/4] Loading checkpoint from {checkpointPath}...");
        try
        {
            LoadCheckpointWithFallback(model, checkpointPath);
            Console.WriteLine("  Checkpoint loaded successfully");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  ERROR loading checkpoint: {ex.Message}");
            Environment.ExitCode = 1;
            return;
        }

        // 4. Forward pass
        Console.WriteLine("\n[4/4] Running forward pass...");
        model.eval();
        using (TorchSharp.torch.no_grad())
        {
            var inputIds = TorchSharp.torch.randint(0, config.VocabSize, new long[] { 1, 32 }, dtype: TorchSharp.torch.ScalarType.Int64);
            var labels = inputIds.clone();

            var (logits, loss) = model.forward(inputIds, null, labels);

            Console.WriteLine($"  Input shape:  [{inputIds.shape[0]}, {inputIds.shape[1]}]");
            Console.WriteLine($"  Logits shape: [{logits.shape[0]}, {logits.shape[1]}, {logits.shape[2]}]");
            Console.WriteLine($"  Loss: {loss!.item<float>():F4}");
            Console.WriteLine($"  Loss finite: {float.IsFinite(loss.item<float>())}");
        }

        Console.WriteLine("\n=== Smoke Test PASSED ===");
    }

    /// <summary>
    /// Loads a Python checkpoint with fallback for container dict format. Tries load_py directly first, then falls back to Python subprocess extraction of model_state_dict.
    /// </summary>
    /// <param name="model"></param>
    /// <param name="checkpointPath"></param>
    private static void LoadCheckpointWithFallback(SharpOps.Model.SharpTinyCoder model, string checkpointPath)
    {
        try
        {
            model.load_py(checkpointPath, strict: false);
        }
        catch (InvalidCastException)
        {
            Console.WriteLine("  Container format detected, extracting state_dict via Python...");
            var tempPath = Path.Combine(Path.GetTempPath(), $"statedict_{Guid.NewGuid():N}.pt");
            try
            {
                ExtractStateDictViaPython(checkpointPath, tempPath);
                model.load_py(tempPath, strict: false);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// Extracts model_state_dict from a Python checkpoint file via subprocess. Adds parent directories to sys.path for custom module resolution.
    /// </summary>
    /// <param name="checkpointPath"></param>
    /// <param name="outputPath"></param>
    private static void ExtractStateDictViaPython(string checkpointPath, string outputPath)
    {
        // Add checkpoint dir and parent dirs to sys.path so Python can find custom modules
        var checkpointDir = Path.GetDirectoryName(checkpointPath) ?? ".";
        var parentDir = Path.GetDirectoryName(checkpointDir) ?? checkpointDir;

        var script = $"import sys; sys.path.insert(0, r'{parentDir}'); sys.path.insert(0, r'{checkpointDir}'); " +
                     $"import torch; ckpt = torch.load(r'{checkpointPath}', map_location='cpu', weights_only=False); " +
                     $"sd = ckpt['model_state_dict'] if 'model_state_dict' in ckpt else ckpt; " +
                     $"torch.save(sd, r'{outputPath}')";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"-c \"{script}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            var err = proc.StandardError.ReadToEnd();
            throw new Exception($"Python state_dict extraction failed: {err}");
        }
    }
}
