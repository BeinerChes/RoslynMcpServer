using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definitionArray10 = new[] { "filePath", "line", "column" };

    /// <summary>
    /// Registers the ApplyCodeFix tool.
    /// </summary>
    private static void RegisterApplyCodeFixTool(McpServer server)
    {
        server.RegisterTool(
            "ApplyCodeFix",
            new ToolDefinition
            {
                Description = "Applies a Roslyn code fix for a diagnostic at a specific file location. " +
                    "First use GetDiagnostics to find issues, then use this tool to automatically fix them. " +
                    "Supports preview mode to see what would change before applying. " +
                    "If multiple fixes are available, returns the list so you can select one.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new
                        {
                            type = "string",
                            description = "Absolute path to the source file containing the diagnostic"
                        },
                        line = new
                        {
                            type = "integer",
                            description = "Line number (1-based) where the diagnostic is located",
                            minimum = 1
                        },
                        column = new
                        {
                            type = "integer",
                            description = "Column number (1-based) where the diagnostic is located",
                            minimum = 1
                        },
                        diagnosticId = new
                        {
                            type = "string",
                            description = "Optional: specific diagnostic ID to fix (e.g., 'CS0168', 'CA2000'). " +
                                "If not specified, fixes the first diagnostic at the location."
                        },
                        fixIndex = new
                        {
                            type = "integer",
                            description = "Optional: index of the fix to apply when multiple fixes are available. " +
                                "If not specified and multiple fixes exist, returns the list of available fixes.",
                            minimum = 0
                        },
                        preview = new
                        {
                            type = "boolean",
                            description = "If true, returns what would change without actually applying the fix. Default: false"
                        }
                    },
                    required = new[] { "filePath", "line", "column" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,  // This tool modifies files
                    IdempotentHint = false // Applying a fix changes state
                }
            },
            async args =>
            {
                var (solutionPath, solutionError) = GetSolutionPathOrError();
                if (solutionError != null) return solutionError;

                var filePath = args?["filePath"]?.GetValue<string>();
                var line = args?["line"]?.GetValue<int>() ?? 0;
                var column = args?["column"]?.GetValue<int>() ?? 0;
                var diagnosticId = args?["diagnosticId"]?.GetValue<string>();
                var fixIndex = args?["fixIndex"]?.GetValue<int?>();
                var preview = args?["preview"]?.GetValue<bool>() ?? false;

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: filePath is required" }
                        },
                        isError = false
                    };
                }

                if (line <= 0)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: line must be a positive integer (1-based)" }
                        },
                        isError = false
                    };
                }

                if (column <= 0)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: column must be a positive integer (1-based)" }
                        },
                        isError = false
                    };
                }

                var result = await SolutionAnalyzerService.ApplyCodeFixAsync(
                    solutionPath!,
                    filePath,
                    line,
                    column,
                    diagnosticId,
                    fixIndex,
                    preview);

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) }
                    },
                    isError = false
                };
            });
    }
}
