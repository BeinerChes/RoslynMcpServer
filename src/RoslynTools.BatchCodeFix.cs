using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definitionArray9 = new[] { "diagnosticId" };

    private static void RegisterBatchApplyCodeFix(McpServer server)
    {
        server.RegisterTool(
            "BatchApplyCodeFixes",
            new ToolDefinition
            {
                Description = "Batch applies Roslyn code fixes for all diagnostics of a specific type. " +
                              "Much faster than applying fixes one by one - loads solution once, applies all fixes in memory, " +
                              "then writes changes to disk. Use GetDiagnostics first to find diagnostic IDs with fixAvailable: true.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        diagnosticId = new
                        {
                            type = "string",
                            description = "The diagnostic ID to fix (e.g., 'CS0168', 'CS8618'). Required."
                        },
                        projectFilter = new
                        {
                            type = "string",
                            description = "Optional: filter by project name (partial match)"
                        },
                        fileFilter = new
                        {
                            type = "string",
                            description = "Optional: filter by file name or path (partial match)"
                        },
                        maxFixes = new
                        {
                            type = "integer",
                            description = "Maximum number of fixes to apply. Default: 100",
                            minimum = 1,
                            maximum = 1000
                        },
                        preview = new
                        {
                            type = "boolean",
                            description = "If true, returns what would change without actually applying fixes. Default: false"
                        }
                    },
                    required = new[] { "diagnosticId" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    IdempotentHint = false
                }
            },
            async args =>
            {
                var (solutionPath, solutionError) = GetSolutionPathOrError();
                if (solutionError != null) return solutionError;

                var diagnosticId = args?["diagnosticId"]?.GetValue<string>();
                var projectFilter = args?["projectFilter"]?.GetValue<string>();
                var fileFilter = args?["fileFilter"]?.GetValue<string>();
                var maxFixes = args?["maxFixes"]?.GetValue<int>() ?? 100;
                var preview = args?["preview"]?.GetValue<bool>() ?? false;

                if (string.IsNullOrWhiteSpace(diagnosticId))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: diagnosticId is required" }
                        },
                        isError = true
                    };
                }

                var result = await SolutionAnalyzerService.BatchApplyCodeFixAsync(
                    solutionPath!, diagnosticId, projectFilter, fileFilter, maxFixes, preview);

                if (!result.Success)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = result.Error ?? "Failed to batch apply code fixes" }
                        },
                        isError = false
                    };
                }

                // Compact output: summary + relative file list
                var relativeFiles = result.ModifiedFiles
                    .Select(f => GetRelativePath(f, solutionPath!))
                    .ToList();

                var compact = new
                {
                    diagnosticId,
                    found = result.TotalDiagnosticsFound,
                    applied = result.FixesApplied,
                    failed = result.FixesFailed,
                    filesModified = relativeFiles
                };

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(compact, JsonOptions) }
                    },
                    isError = false
                };
            });
    }
}
