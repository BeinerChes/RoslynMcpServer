using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definitionArray26 = new[] { "typeName" };

    /// <summary>
    /// Finds all implementations of an interface or derived classes.
    /// </summary>
    private static void RegisterGetImplementationsTool(McpServer server)
    {
        server.RegisterTool(
            "GetImplementations",
            new ToolDefinition
            {
                Description = "Finds all implementations of an interface or all classes derived from a base class. Essential for understanding inheritance hierarchies and finding all variants of a pattern.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
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
                    required = new[] { "typeName" }
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

                var typeName = args?["typeName"]?.GetValue<string>();

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

                var result = await SolutionAnalyzerService.FindImplementationsAsync(
                    solutionPath!,
                    typeName,
                    includeBaseType,
                    maxResults);

                if (!result.Success)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = result.Error ?? "Failed to find implementations" }
                        },
                        isError = false
                    };
                }

                // Build compact response: "Namespace.Type (Kind) path:line"
                var compactImpls = result.Implementations?.Select(i =>
                {
                    var relativePath = GetRelativePath(i.FilePath ?? "", solutionPath!);
                    var abstractFlag = i.IsAbstract ? " [abstract]" : "";
                    return $"{i.FullyQualifiedName} ({i.Kind}){abstractFlag} {relativePath}:{i.Line}";
                }).ToList() ?? new List<string>();

                var compactResult = new
                {
                    baseType = result.BaseType?.FullyQualifiedName ?? typeName,
                    count = result.TotalFound,
                    implementations = compactImpls
                };

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
