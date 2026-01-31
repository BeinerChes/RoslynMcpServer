using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definitionArray15 = new[] { "all", "error", "warning", "info" };
    private static readonly string[] definitionArray16 = Array.Empty<string>();

    /// <summary>
    /// Gets compilation diagnostics (warnings/errors) from a solution.
    /// </summary>
    private static void RegisterGetDiagnosticsTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_get_diagnostics",
            new ToolDefinition
            {
                Description = "Compiles a .NET solution and returns diagnostics (errors, warnings). Without diagnosticId, returns summary (counts by diagnostic code). With diagnosticId, returns detailed entries with file/line/method info for targeted fixing.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        solutionPath = new
                        {
                            type = "string",
                            description = "Absolute path to the .sln or .slnx solution file"
                        },
                        diagnosticId = new
                        {
                            type = "string",
                            description = "Optional: specific diagnostic ID to get details for (e.g., 'CA2000', 'CS0618'). If not specified, returns summary counts."
                        },
                        severityFilter = new
                        {
                            type = "string",
                            description = "Filter by severity: 'error', 'warning', 'info', or 'all'. Default: 'all'",
                            @enum = definitionArray15
                        },
                        projectFilter = new
                        {
                            type = "string",
                            description = "Optional: filter by project name (partial match)"
                        },
                        maxResults = new
                        {
                            type = "integer",
                            description = "Maximum entries to return in detail mode. Default: 100",
                            minimum = 1,
                            maximum = 1000
                        },
                        offset = new
                        {
                            type = "integer",
                            description = "Skip first N entries for pagination. Default: 0",
                            minimum = 0
                        }
                    },
                    required = definitionArray16
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var (solutionPath, solutionError) = GetSolutionPathOrError();
                if (solutionError != null) return solutionError;

                var diagnosticId = args?["diagnosticId"]?.GetValue<string>();
                var severityFilter = args?["severityFilter"]?.GetValue<string>();
                var projectFilter = args?["projectFilter"]?.GetValue<string>();
                var maxResults = args?["maxResults"]?.GetValue<int>() ?? 100;
                var offset = args?["offset"]?.GetValue<int>() ?? 0;

                var result = await SolutionAnalyzerService.GetDiagnosticsAsync(
                    solutionPath!,
                    diagnosticId,
                    severityFilter,
                    projectFilter,
                    maxResults,
                    offset);

                if (!result.Success)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = result.Error ?? "Failed to get diagnostics" }
                        },
                        isError = true
                    };
                }

                object compactResult;

                if (result.Summary != null)
                {
                    // Summary mode: "CS0618 (warning) [fix]: Description (count)"
                    var compactSummary = result.Summary.Select(s =>
                    {
                        var fixFlag = s.FixAvailable ? " [fix]" : "";
                        var suppressed = s.SuppressedCount > 0 ? $" ({s.SuppressedCount} suppressed)" : "";
                        return $"{s.Id} ({s.Severity}){fixFlag}: {s.Title} ({s.Count}){suppressed}";
                    }).ToList();

                    compactResult = new
                    {
                        errors = result.TotalErrors,
                        warnings = result.TotalWarnings,
                        summary = compactSummary
                    };
                }
                else
                {
                    // Detail mode: "file:line Type.Method - message"
                    var compactEntries = result.Entries?.Select(e =>
                    {
                        var relativePath = GetRelativePath(e.FilePath, solutionPath!);
                        var location = !string.IsNullOrEmpty(e.ContainingType)
                            ? $"{e.ContainingType}.{e.ContainingMethod}"
                            : "";
                        return $"{relativePath}:{e.Line} {location} - {e.Message}";
                    }).ToList() ?? new List<string>();

                    compactResult = new
                    {
                        total = result.TotalMatchingEntries,
                        entries = compactEntries
                    };
                }

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(compactResult, JsonOptions) }
                    },
                    isError = false
                };
            });
    }
}
