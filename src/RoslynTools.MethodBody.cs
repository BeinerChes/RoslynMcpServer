using System.Text.Json;
using RoslynMcpServer.Graph;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definitionArray27 = new[] { "solutionPath", "typeName", "methodName" };

    /// <summary>
    /// Gets the full source code of a method including its body.
    /// </summary>
    private static void RegisterGetMethodBodyTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_get_method_body",
            new ToolDefinition
            {
                Description = "Gets the full source code of a method including its implementation. Returns the complete method text that can be edited and applied back. Essential for working with large classes without reading the entire file.",
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
                            description = "Name of the type (class, struct) containing the method"
                        },
                        methodName = new
                        {
                            type = "string",
                            description = "Name of the method to retrieve. Use '.ctor' or the type name for constructors."
                        },
                        parameterTypes = new
                        {
                            type = "string",
                            description = "Parameter types to identify a specific overload, e.g. 'string, int' or 'CancellationToken, bool'. Required if multiple overloads exist."
                        }
                    },
                    required = definitionArray27
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
                var methodName = args?["methodName"]?.GetValue<string>();
                var parameterTypes = args?["parameterTypes"]?.GetValue<string>();

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

                if (string.IsNullOrWhiteSpace(methodName))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: methodName is required" }
                        },
                        isError = true
                    };
                }

                var result = await SolutionAnalyzerService.GetMethodBodyAsync(
                    solutionPath,
                    typeName,
                    methodName,
                    parameterTypes);

                // Track for visualization sync
                if (result.Success)
                    LastSymbolTracker.Track(solutionPath, $"{typeName}.{methodName}", "method");

                // Fetch related knowledge entries
                List<object>? knowledge = null;
                if (result.Success && result.TypeName != null)
                {
                    try
                    {
                        var db = await GetKnowledgeDatabaseAsync(solutionPath);
                        // Use fully qualified type name from result
                        var fullyQualifiedSymbol = $"{result.TypeName}.{methodName}";
                        var entries = await db.GetEntriesForSymbolAsync(fullyQualifiedSymbol);

                        // Also search for type-level knowledge
                        var typeEntries = await db.GetEntriesForSymbolAsync(result.TypeName);
                        entries.AddRange(typeEntries.Where(e => !entries.Any(x => x.Id == e.Id)));

                        if (entries.Count > 0)
                        {
                            knowledge = entries.Select(e => (object)new
                            {
                                e.Id,
                                e.Category,
                                e.Title,
                                e.Content,
                                e.Confidence
                            }).ToList();
                        }
                    }
                    catch
                    {
                        // Knowledge lookup failure shouldn't break the main functionality
                    }
                }

                // Build response with optional knowledge
                var response = new
                {
                    result.Success,
                    result.Error,
                    result.SolutionPath,
                    result.TypeName,
                    result.MethodName,
                    result.FilePath,
                    result.StartLine,
                    result.EndLine,
                    result.Signature,
                    result.SourceCode,
                    result.AvailableOverloads,
                    Knowledge = knowledge
                };

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(response, JsonOptions) }
                    },
                    isError = !result.Success
                };
            });
    }
}
