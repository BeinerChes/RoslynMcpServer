using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
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
                            @enum = new[] { "all", "error", "warning", "info" }
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
                    required = new[] { "solutionPath" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var solutionPath = args?["solutionPath"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(solutionPath))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: solutionPath is required" }
                        },
                        isError = true
                    };
                }

                var diagnosticId = args?["diagnosticId"]?.GetValue<string>();
                var severityFilter = args?["severityFilter"]?.GetValue<string>();
                var projectFilter = args?["projectFilter"]?.GetValue<string>();
                var maxResults = args?["maxResults"]?.GetValue<int>() ?? 100;
                var offset = args?["offset"]?.GetValue<int>() ?? 0;

                var result = await _analyzerService!.GetDiagnosticsAsync(
                    solutionPath,
                    diagnosticId,
                    severityFilter,
                    projectFilter,
                    maxResults,
                    offset);

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) }
                    },
                    isError = !result.Success
                };
            });
    }
}
