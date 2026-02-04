using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Removes unnecessary using directives from files in the solution.
    /// </summary>
    private static void RegisterRemoveUnnecessaryUsingsTool(McpServer server)
    {
        server.RegisterTool(
            "RemoveUnnecessaryUsings",
            new ToolDefinition
            {
                Description = "Removes unnecessary using directives (CS8019) from C# files. This is a dedicated tool because the standard code fix provider for CS8019 requires IDE services that aren't available in batch mode. Skips generated files in obj/ folders.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
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
                        preview = new
                        {
                            type = "boolean",
                            description = "If true, shows what would be removed without making changes. Default: false"
                        }
                    },
                    required = Array.Empty<string>()
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var (solutionPath, solutionError) = GetSolutionPathOrError();
                if (solutionError != null) return solutionError;

                var projectFilter = args?["projectFilter"]?.GetValue<string>();
                var fileFilter = args?["fileFilter"]?.GetValue<string>();
                var preview = args?["preview"]?.GetValue<bool>() ?? false;

                var result = await SolutionAnalyzerService.RemoveUnnecessaryUsingsAsync(
                    solutionPath!,
                    projectFilter,
                    fileFilter,
                    preview);

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
