using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definitionArray29 = new[] { "solutionPath", "filePath", "line", "column", "newName" };

    private static void RegisterRenameSymbolTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_rename_symbol",
            new ToolDefinition
            {
                Description = "Renames a symbol (type, method, property, field, parameter, variable) at a specific file position across the entire solution. " +
                              "Updates all references automatically. Essential for safe refactoring without breaking code.",
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
                        newName = new
                        {
                            type = "string",
                            description = "The new name for the symbol"
                        }
                    },
                    required = definitionArray29
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    IdempotentHint = false
                }
            },
            async args =>
            {
                var solutionPath = args?["solutionPath"]?.GetValue<string>();
                var filePath = args?["filePath"]?.GetValue<string>();
                var line = args?["line"]?.GetValue<int>() ?? 0;
                var column = args?["column"]?.GetValue<int>() ?? 0;
                var newName = args?["newName"]?.GetValue<string>();

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

                if (string.IsNullOrWhiteSpace(newName))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: newName is required" }
                        },
                        isError = true
                    };
                }

                var result = await SolutionAnalyzerService.RenameSymbolAsync(
                    solutionPath, filePath, line, column, newName);

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
