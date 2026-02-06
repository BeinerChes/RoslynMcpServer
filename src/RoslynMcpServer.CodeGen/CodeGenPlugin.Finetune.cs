using System.Text.Json.Nodes;
using SharpOps.Torch.Training;

namespace RoslynMcpServer.CodeGen;

/// <summary>
/// Finetune tool registration and handling, separated from the main CodeGenPlugin class.
/// </summary>
internal static class CodeGenPluginFinetune
{
    private static Task<TrainingResult>? _trainingTask;
    private static string? _trainingLogPath;
    private static readonly object _trainingLock = new();
    private static CodeGenPlugin? _plugin;

    internal static void Register(CodeGenPlugin plugin, McpServer server)
    {
        _plugin = plugin;

        server.RegisterTool(
            "Finetune",
            new ToolDefinition
            {
                Description = """
                    Fine-tune the SharpTinyCoder model using LoRA. Runs in background — returns immediately
                    with a log file path. Call again to check status. After training completes, the model is
                    automatically reloaded for inference. Uses sensible defaults (rank=8, qv modules,
                    lr=1e-4, auto-stop on train loss plateau).
                    """,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        dataPath = new
                        {
                            type = "string",
                            description = "Path to JSONL file or folder with training data. Default: .roslyn-mcp/Models/finetune/dataset/"
                        }
                    },
                    required = Array.Empty<string>()
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    DestructiveHint = false,
                    IdempotentHint = false
                }
            },
            HandleFinetuneAsync);
    }

    private static Task<object> HandleFinetuneAsync(JsonObject? args)
    {
        lock (_trainingLock)
        {
            if (_trainingTask is not null && !_trainingTask.IsCompleted)
            {
                return Task.FromResult<object>(RoslynTools.CreateSuccessResponse(new
                {
                    status = "running",
                    message = "Training is already in progress.",
                    logFile = _trainingLogPath
                }));
            }

            if (_trainingTask is not null && _trainingTask.IsCompleted)
            {
                var completedResult = GetTrainingResult();
                _trainingTask = null;
                return Task.FromResult<object>(completedResult);
            }
        }

        var dataPath = args?["dataPath"]?.GetValue<string>();
        if (string.IsNullOrEmpty(dataPath))
        {
            dataPath = Path.Combine(AppContext.BaseDirectory, "Models", "finetune", "dataset");
        }

        if (!File.Exists(dataPath) && !Directory.Exists(dataPath))
        {
            return Task.FromResult<object>(RoslynTools.CreateErrorResponse($"Data path not found: {dataPath}"));
        }

        var baseDir = AppContext.BaseDirectory;
        var checkpointPath = Path.Combine(baseDir, "Models", "checkpoint.pt");
        var tokenizerPath = Path.Combine(baseDir, "Models", "tokenizer", "tokenizer.json");

        if (!File.Exists(checkpointPath))
        {
            return Task.FromResult<object>(RoslynTools.CreateErrorResponse($"Checkpoint not found: {checkpointPath}"));
        }

        var logDir = Path.Combine(baseDir, "Models", "finetune");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, $"training_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        var config = new TrainingConfig
        {
            DataPath = dataPath,
            CheckpointPath = checkpointPath,
            TokenizerPath = tokenizerPath,
            OutputPath = checkpointPath,
        };

        lock (_trainingLock)
        {
            _trainingLogPath = logPath;

            _trainingTask = Task.Run(() =>
            {
                using var logWriter = new StreamWriter(logPath, append: false) { AutoFlush = true };
                var teeWriter = new TeeTextWriter(logWriter, Console.Error);
                var trainer = new LoRATrainer(config, teeWriter);
                var result = trainer.Run();

                if (!float.IsNaN(result.BestLoss))
                {
                    _plugin?.ReloadSharpOpsModel();
                    teeWriter.WriteLine("[Finetune] Model reloaded for inference.");
                    ArchiveDataset(dataPath, logPath, teeWriter);
                }

                return result;
            });
        }

        return Task.FromResult<object>(RoslynTools.CreateSuccessResponse(new
        {
            status = "started",
            message = "Fine-tuning started in background. Call Finetune again to check status.",
            logFile = logPath,
            config = new
            {
                dataPath,
                config.Rank,
                config.Alpha,
                config.TargetModules,
                config.Epochs,
                config.LearningRate,
                config.Patience,
                earlyStopMinDelta = config.MinDelta
            }
        }));
    }

    private static object GetTrainingResult()
    {
        if (_trainingTask == null)
            return RoslynTools.CreateErrorResponse("No training task found");

        if (_trainingTask.IsFaulted)
        {
            var ex = _trainingTask.Exception?.InnerException ?? _trainingTask.Exception;
            var details = ex?.ToString() ?? "Unknown error";
            return RoslynTools.CreateErrorResponse($"Training failed: {details}");
        }

        var result = _trainingTask.Result;

        if (float.IsNaN(result.BestLoss))
            return RoslynTools.CreateErrorResponse("Training failed: no training examples found");

        var bestLoss = float.IsPositiveInfinity(result.BestLoss) ? -1f : result.BestLoss;

        return RoslynTools.CreateSuccessResponse(new
        {
            status = "completed",
            message = "Fine-tuning completed. Model has been reloaded.",
            bestLoss,
            epochsRun = result.EpochsRun,
            earlyStopped = result.EarlyStopped,
            outputPath = result.OutputPath,
            logFile = _trainingLogPath
        });
    }

    private static void ArchiveDataset(string dataPath, string logPath, TextWriter log)
    {
        try
        {
            var dataDir = File.Exists(dataPath) ? Path.GetDirectoryName(dataPath)! : dataPath;
            var files = Directory.GetFiles(dataDir, "*.jsonl");
            if (files.Length == 0) return;

            var logName = Path.GetFileNameWithoutExtension(logPath);
            var timestamp = logName.Replace("training_", "");
            var archiveDir = Path.Combine(Path.GetDirectoryName(dataDir)!, "archive", timestamp);
            Directory.CreateDirectory(archiveDir);

            foreach (var file in files)
            {
                var dest = Path.Combine(archiveDir, Path.GetFileName(file));
                File.Move(file, dest);
            }

            log.WriteLine($"[Finetune] Dataset archived to {archiveDir} ({files.Length} file(s))");
        }
        catch (Exception ex)
        {
            log.WriteLine($"[Finetune] Warning: failed to archive dataset: {ex.Message}");
        }
    }
}

/// <summary>
/// TextWriter that writes to two underlying writers (tee).
/// </summary>
internal sealed class TeeTextWriter(TextWriter primary, TextWriter secondary) : TextWriter
{
    public override System.Text.Encoding Encoding => primary.Encoding;

    public override void Write(char value)
    {
        primary.Write(value);
        secondary.Write(value);
    }

    public override void Write(string? value)
    {
        primary.Write(value);
        secondary.Write(value);
    }

    public override void WriteLine(string? value)
    {
        primary.WriteLine(value);
        secondary.WriteLine(value);
    }

    public override void Flush()
    {
        primary.Flush();
        secondary.Flush();
    }
}
