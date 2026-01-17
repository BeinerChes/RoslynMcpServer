using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Finds all callers of a method at a given position.
    /// </summary>
    private static void RegisterGetCallersTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_get_callers",
            new ToolDefinition
            {
                Description = "Finds all callers of a method/property at a given file position. Unlike get_references, this returns only actual call sites - not declarations, docs, or type references. Essential for understanding execution flow and impact analysis before refactoring.",
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
                        filePath = new
                        {
                            type = "string",
                            description = "Absolute path to the source file containing the method"
                        },
                        line = new
                        {
                            type = "integer",
                            description = "Line number (1-based) where the method is located",
                            minimum = 1
                        },
                        column = new
                        {
                            type = "integer",
                            description = "Column number (1-based) where the method is located",
                            minimum = 1
                        },
                        maxResults = new
                        {
                            type = "integer",
                            description = "Maximum number of callers to return. Default: 100",
                            minimum = 1,
                            maximum = 1000
                        },
                        offset = new
                        {
                            type = "integer",
                            description = "Skip first N results for pagination. Default: 0",
                            minimum = 0
                        },
                        projectFilter = new
                        {
                            type = "string",
                            description = "Filter by project name. Supports wildcards (*). Example: 'MyApp.*'"
                        },
                        fileFilter = new
                        {
                            type = "string",
                            description = "Filter by file path. Supports wildcards (*). Example: '*Service.cs'"
                        }
                    },
                    required = new[] { "solutionPath", "filePath", "line", "column" }
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
                var filePath = args?["filePath"]?.GetValue<string>();
                var line = args?["line"]?.GetValue<int>() ?? 0;
                var column = args?["column"]?.GetValue<int>() ?? 0;

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

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: filePath is required" }
                        },
                        isError = true
                    };
                }

                if (line < 1)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: line must be >= 1" }
                        },
                        isError = true
                    };
                }

                if (column < 1)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: column must be >= 1" }
                        },
                        isError = true
                    };
                }

                // Parse optional parameters
                var maxResults = args?["maxResults"]?.GetValue<int>() ?? 100;
                var offset = args?["offset"]?.GetValue<int>() ?? 0;
                var projectFilter = args?["projectFilter"]?.GetValue<string>();
                var fileFilter = args?["fileFilter"]?.GetValue<string>();

                var result = await _analyzerService!.GetCallersAsync(
                    solutionPath,
                    filePath,
                    line,
                    column,
                    maxResults,
                    offset,
                    projectFilter,
                    fileFilter);

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
