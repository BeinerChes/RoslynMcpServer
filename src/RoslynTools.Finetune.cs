using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static void RegisterFinetuneTool(McpServer server)
    {
        server.RegisterTool(
            "Finetune",
            new ToolDefinition
            {
                Description = "Fine-tune the SharpTinyCoder model using LoRA. Loads a checkpoint, attaches LoRA adapters, trains on JSONL data from the finetune collector, merges LoRA weights back, and saves the result. Runs as a subprocess.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        dataPath = new
                        {
                            type = "string",
                            description = "Path to JSONL file or folder with training data. Default: .roslyn-mcp/Models/finetune/dataset/"
                        },
                        epochs = new
                        {
                            type = "integer",
                            description = "Number of training epochs. Default: 50",
                            minimum = 1
                        },
                        rank = new
                        {
                            type = "integer",
                            description = "LoRA rank. Default: 16",
                            minimum = 1
                        },
                        alpha = new
                        {
                            type = "number",
                            description = "LoRA alpha scaling factor. Default: 32"
                        },
                        lr = new
                        {
                            type = "number",
                            description = "Learning rate. Default: 1e-4"
                        },
                        targetModules = new
                        {
                            type = "string",
                            description = "LoRA target modules: 'qv', 'attention', 'ffn', or 'all'. Default: all"
                        },
                        batchSize = new
                        {
                            type = "integer",
                            description = "Batch size. Default: 4",
                            minimum = 1
                        },
                        valSplit = new
                        {
                            type = "number",
                            description = "Validation split fraction (0.0-1.0). Default: 0.0"
                        },
                        checkpoint = new
                        {
                            type = "string",
                            description = @"Path to input checkpoint. Default: D:\RMS\CSharpRobot\checkpoints\checkpoint.pt"
                        },
                        output = new
                        {
                            type = "string",
                            description = "Path to save merged checkpoint. Default: <checkpoint>_lora.pt"
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

    private static async Task<object> HandleFinetuneAsync(JsonObject? args)
    {
        // Resolve data path
        var dataPath = args?["dataPath"]?.GetValue<string>();
        if (string.IsNullOrEmpty(dataPath))
        {
            // Default: finetune collector output
            dataPath = Path.Combine(AppContext.BaseDirectory, "Models", "finetune", "dataset");
        }

        if (!File.Exists(dataPath) && !Directory.Exists(dataPath))
        {
            return CreateErrorResponse($"Data path not found: {dataPath}");
        }

        // Build CLI arguments
        var cliArgs = new List<string> { "finetune", "--data", dataPath };

        if (args?["epochs"] is not null)
            cliArgs.AddRange(["--epochs", args["epochs"]!.GetValue<int>().ToString()]);

        if (args?["rank"] is not null)
            cliArgs.AddRange(["--rank", args["rank"]!.GetValue<int>().ToString()]);

        if (args?["alpha"] is not null)
            cliArgs.AddRange(["--alpha", args["alpha"]!.GetValue<double>().ToString()]);

        if (args?["lr"] is not null)
            cliArgs.AddRange(["--lr", args["lr"]!.GetValue<double>().ToString()]);

        if (args?["targetModules"] is not null)
            cliArgs.AddRange(["--target-modules", args["targetModules"]!.GetValue<string>()]);

        if (args?["batchSize"] is not null)
            cliArgs.AddRange(["--batch-size", args["batchSize"]!.GetValue<int>().ToString()]);

        if (args?["valSplit"] is not null)
            cliArgs.AddRange(["--val-split", args["valSplit"]!.GetValue<double>().ToString()]);

        if (args?["checkpoint"] is not null)
            cliArgs.AddRange(["--checkpoint", args["checkpoint"]!.GetValue<string>()]);

        if (args?["output"] is not null)
            cliArgs.AddRange(["--output", args["output"]!.GetValue<string>()]);

        // Find SharpOps.Torch project
        var trainingProjectDir = FindTrainingProject();
        if (trainingProjectDir == null)
        {
            return CreateErrorResponse("Could not find SharpOps.Torch project directory");
        }

        try
        {
            var argString = string.Join(" ", cliArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            Console.Error.WriteLine($"[Finetune] Running: dotnet run --project {trainingProjectDir} -- {argString}");

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{trainingProjectDir}\" -- {argString}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return CreateErrorResponse("Failed to start training process");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var stdout = new StringBuilder(await stdoutTask);
            var stderr = new StringBuilder(await stderrTask);

            // Log output
            foreach (var line in stdout.ToString().Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    Console.Error.WriteLine($"[Finetune] {line.TrimEnd()}");
            }

            if (process.ExitCode != 0)
            {
                return CreateErrorResponse(
                    $"Training failed (exit code {process.ExitCode}):\n{stderr}\n{stdout}");
            }

            // Parse output for final loss
            var output = stdout.ToString();
            var bestLoss = ParseBestLoss(output);

            return CreateSuccessResponse(new
            {
                message = "Fine-tuning completed successfully",
                bestLoss,
                output = output.Length > 2000 ? output[^2000..] : output,
            });
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Finetune error: {ex.Message}");
        }
    }

    private static string? FindTrainingProject()
    {
        // Look relative to solution directory
        if (!string.IsNullOrEmpty(_solutionDir))
        {
            var path = Path.Combine(_solutionDir, "SharpOps.Torch");
            if (Directory.Exists(path)) return path;
        }

        // Look relative to the MCP server base directory
        var baseDir = AppContext.BaseDirectory;
        // Walk up to find SharpOps.Torch
        var current = baseDir;
        for (int i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(current, "SharpOps.Torch");
            if (Directory.Exists(candidate)) return candidate;
            current = Path.GetDirectoryName(current) ?? current;
        }

        return null;
    }

    private static string? ParseBestLoss(string output)
    {
        // Look for "Best loss: X.XXXXXX" in the output
        var lines = output.Split('\n');
        foreach (var line in lines.Reverse())
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Best loss:"))
            {
                return trimmed["Best loss:".Length..].Trim();
            }
        }
        return null;
    }
}
