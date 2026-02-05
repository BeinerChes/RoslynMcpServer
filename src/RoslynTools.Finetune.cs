using System.Text.Json.Nodes;
using SharpOps.Torch.Training;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static Task<TrainingResult>? _trainingTask;
    private static string? _trainingLogPath;
    private static readonly object _trainingLock = new();

    private static void RegisterFinetuneTool(McpServer server)
    {
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
            // Check if training is already running
            if (_trainingTask is not null && !_trainingTask.IsCompleted)
            {
                return Task.FromResult<object>(CreateSuccessResponse(new
                {
                    status = "running",
                    message = "Training is already in progress.",
                    logFile = _trainingLogPath
                }));
            }

            // If completed, return results
            if (_trainingTask is not null && _trainingTask.IsCompleted)
            {
                var completedResult = GetTrainingResult();
                _trainingTask = null;
                return Task.FromResult<object>(completedResult);
            }
        }

        // Resolve data path
        var dataPath = args?["dataPath"]?.GetValue<string>();
        if (string.IsNullOrEmpty(dataPath))
        {
            dataPath = Path.Combine(AppContext.BaseDirectory, "Models", "finetune", "dataset");
        }

        if (!File.Exists(dataPath) && !Directory.Exists(dataPath))
        {
            return Task.FromResult<object>(CreateErrorResponse($"Data path not found: {dataPath}"));
        }

        // Resolve model paths
        var baseDir = AppContext.BaseDirectory;
        var checkpointPath = Path.Combine(baseDir, "Models", "checkpoint.pt");
        var tokenizerPath = Path.Combine(baseDir, "Models", "tokenizer", "tokenizer.json");

        if (!File.Exists(checkpointPath))
        {
            return Task.FromResult<object>(CreateErrorResponse($"Checkpoint not found: {checkpointPath}"));
        }

        // Set up log file
        var logDir = Path.Combine(baseDir, "Models", "finetune");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, $"training_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        var config = new TrainingConfig
        {
            DataPath = dataPath,
            CheckpointPath = checkpointPath,
            TokenizerPath = tokenizerPath,
            // Output overwrites the active checkpoint so model reload picks it up
            OutputPath = checkpointPath,
        };

        lock (_trainingLock)
        {
            _trainingLogPath = logPath;

            _trainingTask = Task.Run(() =>
            {
                using var logWriter = new StreamWriter(logPath, append: false) { AutoFlush = true };
                // Tee to both log file and stderr (for MCP server console)
                var teeWriter = new TeeTextWriter(logWriter, Console.Error);
                var trainer = new LoRATrainer(config, teeWriter);
                var result = trainer.Run();

                // Auto-reload the model after successful training
                if (!float.IsNaN(result.BestLoss))
                {
                    ReloadSharpOpsModel();
                    teeWriter.WriteLine("[Finetune] Model reloaded for inference.");
                    ArchiveDataset(dataPath, logPath, teeWriter);
                }

                return result;
            });
        }

        return Task.FromResult<object>(CreateSuccessResponse(new
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
            return CreateErrorResponse("No training task found");

        if (_trainingTask.IsFaulted)
        {
            var ex = _trainingTask.Exception?.InnerException ?? _trainingTask.Exception;
            var details = ex?.ToString() ?? "Unknown error";
            return CreateErrorResponse($"Training failed: {details}");
        }

        var result = _trainingTask.Result;

        if (float.IsNaN(result.BestLoss))
            return CreateErrorResponse("Training failed: no training examples found");

        // Handle infinity (no validation split) - report as -1 since JSON doesn't support infinity
        var bestLoss = float.IsPositiveInfinity(result.BestLoss) ? -1f : result.BestLoss;

        return CreateSuccessResponse(new
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

    private static void ReloadSharpOpsModel()
    {
        // Dispose current model and null out so next GetSharpOpsService() reloads
        var old = _sharpOpsService;
        _sharpOpsService = null;
        _sharpOpsError = null;
        old?.Dispose();
    }

    private static void ArchiveDataset(string dataPath, string logPath, TextWriter log)
    {
        try
        {
            // dataPath is either a file or directory
            var dataDir = File.Exists(dataPath) ? Path.GetDirectoryName(dataPath)! : dataPath;
            var files = Directory.GetFiles(dataDir, "*.jsonl");
            if (files.Length == 0) return;

            // Use the log file timestamp for the archive folder name
            var logName = Path.GetFileNameWithoutExtension(logPath); // training_20260204_141952
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
