using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Finds all references to a symbol at a given position.
    /// </summary>
    private static void RegisterGetReferencesTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_get_references",
            new ToolDefinition
            {
                Description = "Finds all references to a symbol at a given file position. Returns every location where the symbol is used across the entire solution. Essential for impact analysis and refactoring.",
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
                            description = "Absolute path to the source file containing the symbol"
                        },
                        line = new
                        {
                            type = "integer",
                            description = "Line number (1-based) where the symbol is located",
                            minimum = 1
                        },
                        column = new
                        {
                            type = "integer",
                            description = "Column number (1-based) where the symbol is located",
                            minimum = 1
                        },
                        maxResults = new
                        {
                            type = "integer",
                            description = "Maximum number of references to return. Default: 100",
                            minimum = 1,
                            maximum = 10000
                        },
                        projectFilter = new
                        {
                            type = "string",
                            description = "Filter by project name. Supports wildcards (*). Example: 'Atlas.*' or 'MyProject'"
                        },
                        fileFilter = new
                        {
                            type = "string",
                            description = "Filter by file path. Supports wildcards (*). Example: '*Controller.cs' or 'Services'"
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
                var projectFilter = args?["projectFilter"]?.GetValue<string>();
                var fileFilter = args?["fileFilter"]?.GetValue<string>();

                var result = await _analyzerService!.FindReferencesAsync(
                    solutionPath,
                    filePath,
                    line,
                    column,
                    maxResults,
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
