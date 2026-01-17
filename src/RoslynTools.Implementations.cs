using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Finds all implementations of an interface or derived classes.
    /// </summary>
    private static void RegisterGetImplementationsTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_get_implementations",
            new ToolDefinition
            {
                Description = "Finds all implementations of an interface or all classes derived from a base class. Essential for understanding inheritance hierarchies and finding all variants of a pattern.",
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
                        typeName = new
                        {
                            type = "string",
                            description = "Name of the interface or base class to find implementations for"
                        },
                        includeBaseType = new
                        {
                            type = "boolean",
                            description = "Include the base type/interface itself in results. Default: false"
                        },
                        maxResults = new
                        {
                            type = "integer",
                            description = "Maximum number of implementations to return. Default: 100",
                            minimum = 1,
                            maximum = 1000
                        }
                    },
                    required = new[] { "solutionPath", "typeName" }
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
                var typeName = args?["typeName"]?.GetValue<string>();

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

                if (string.IsNullOrWhiteSpace(typeName))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: typeName is required" }
                        },
                        isError = true
                    };
                }

                var includeBaseType = args?["includeBaseType"]?.GetValue<bool>() ?? false;
                var maxResults = args?["maxResults"]?.GetValue<int>() ?? 100;

                var result = await _analyzerService!.FindImplementationsAsync(
                    solutionPath,
                    typeName,
                    includeBaseType,
                    maxResults);

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
