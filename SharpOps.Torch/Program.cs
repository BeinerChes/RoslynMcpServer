using static TorchSharp.torch;
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
        else if (args[0] == "cuda-check")
        {
            var nativeDir = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native");
            Console.WriteLine($"Native dir: {nativeDir}");

            // Add native dir to DLL search path
            var added = SetDllDirectory(nativeDir);
            Console.WriteLine($"SetDllDirectory: {added}");

            // Try loading key DLLs in dependency order
            foreach (var dll in new[] { "cudart64_12.dll", "c10.dll", "c10_cuda.dll", "torch_cpu.dll", "torch.dll", "torch_cuda.dll" })
            {
                try
                {
                    var path = Path.Combine(nativeDir, dll);
                    var handle = System.Runtime.InteropServices.NativeLibrary.Load(path);
                    Console.WriteLine($"  {dll}: OK");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  {dll}: FAILED - {ex.Message}");
                }
            }

            Console.WriteLine($"torch.cuda.is_available(): {TorchSharp.torch.cuda.is_available()}");
            Console.WriteLine($"torch.cuda.device_count(): {TorchSharp.torch.cuda.device_count()}");
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
        string? dataPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--checkpoint": checkpointPath = args[++i]; break;
                case "--tokenizer": tokenizerPath = args[++i]; break;
                case "--data": dataPath = args[++i]; break;
            }
        }

        Console.WriteLine("=== Smoke Test ===");
        var tokenizer = new SharpOps.Inference.SharpOpsTokenizer(tokenizerPath);

        if (dataPath != null)
        {
            var examples = Training.DataLoader.LoadData(dataPath);
            Console.WriteLine($"Examples: {examples.Count}");

            var inputText = examples[0]["input"].GetString() ?? "";
            var outputText = examples[0]["output"].GetString() ?? "";

            var inputIds = tokenizer.Encode(inputText);
            var outputIds = tokenizer.Encode(outputText);

            Console.WriteLine($"Input tokens: {inputIds.Length}");
            Console.WriteLine($"Output tokens: {outputIds.Length}");
            Console.WriteLine($"Python expected: input=74, output=53");

            // Print all output token IDs
            Console.Write("C#     output ids: [");
            Console.Write(string.Join(", ", outputIds));
            Console.WriteLine("]");
            Console.WriteLine("Python output ids: [369, 539, 409, 395, 403, 181, 172, 394, 1250, 1113, 401, 550, 1292, 1539, 181, 171, 1765, 487, 276, 251, 143, 1174, 276, 251, 5, 487, 276, 251, 144, 276, 251, 129, 487, 276, 251, 150, 276, 251, 130, 310, 306, 299, 723, 276, 251, 6, 276, 251, 38, 523, 276, 251, 38]");

            // Find first difference
            var pyIds = new[] { 369, 539, 409, 395, 403, 181, 172, 394, 1250, 1113, 401, 550, 1292, 1539, 181, 171, 1765, 487, 276, 251, 143, 1174, 276, 251, 5, 487, 276, 251, 144, 276, 251, 129, 487, 276, 251, 150, 276, 251, 130, 310, 306, 299, 723, 276, 251, 6, 276, 251, 38, 523, 276, 251, 38 };
            int maxLen = Math.Max(outputIds.Length, pyIds.Length);
            for (int j = 0; j < maxLen; j++)
            {
                int csId = j < outputIds.Length ? outputIds[j] : -1;
                int pyId = j < pyIds.Length ? pyIds[j] : -1;
                if (csId != pyId)
                {
                    Console.WriteLine($"FIRST DIFF at position {j}: C#={csId}, Python={pyId}");
                    // Print surrounding context
                    for (int k = Math.Max(0, j - 2); k < Math.Min(maxLen, j + 5); k++)
                    {
                        int cs = k < outputIds.Length ? outputIds[k] : -1;
                        int py = k < pyIds.Length ? pyIds[k] : -1;
                        var marker = cs != py ? " <---" : "";
                        Console.WriteLine($"  [{k}] C#={cs}, Py={py}{marker}");
                    }
                    break;
                }
            }
        }

        Console.WriteLine("\n=== Smoke Test DONE ===");
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

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);
}
