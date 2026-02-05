using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definitionArray27 = new[] { "typeName", "methodName" };

    /// <summary>
    /// Gets the full source code of a method including its body.
    /// </summary>
    private static void RegisterGetMethodBodyTool(McpServer server)
    {
        server.RegisterTool(
            "GetMethodBody",
            new ToolDefinition
            {
                Description = "Gets the full source code of a method including its implementation. Returns the complete method text that can be edited and applied back. Essential for working with large classes without reading the entire file.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
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
                    required = new[] { "typeName", "methodName" }
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
                var methodName = args?["methodName"]?.GetValue<string>();
                var parameterTypes = args?["parameterTypes"]?.GetValue<string>();

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
                    solutionPath!,
                    typeName,
                    methodName,
                    parameterTypes);

                if (!result.Success)
                {
                    var errorResponse = new Dictionary<string, object?>
                    {
                        ["error"] = result.Error
                    };
                    if (result.AvailableOverloads != null && result.AvailableOverloads.Count > 0)
                    {
                        errorResponse["availableOverloads"] = result.AvailableOverloads;
                    }
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = JsonSerializer.Serialize(errorResponse, JsonOptions) }
                        },
                        isError = true
                    };
                }

                // Track for visualization sync
                LastSymbolTracker.Track(solutionPath!, $"{typeName}.{methodName}", "method");

                // Fetch related knowledge entries
                List<object>? knowledge = null;
                if (result.TypeName != null)
                {
                    try
                    {
                        var db = await GetKnowledgeDatabaseAsync(solutionPath!);
                        var fullyQualifiedSymbol = $"{result.TypeName}.{methodName}";
                        var entries = await db.GetEntriesForSymbolAsync(fullyQualifiedSymbol);

                        var typeEntries = await db.GetEntriesForSymbolAsync(result.TypeName);
                        entries.AddRange(typeEntries.Where(e => !entries.Any(x => x.Id == e.Id)));

                        if (entries.Count > 0)
                        {
                            knowledge = entries.Select(e => (object)new
                            {
                                e.Id,
                                e.Category,
                                e.Title
                            }).ToList();
                        }
                    }
                    catch
                    {
                        // Knowledge lookup failure shouldn't break the main functionality
                    }
                }

                // Build compact response
                var relativePath = GetRelativePath(result.FilePath ?? "", solutionPath!);
                var compactResult = new Dictionary<string, object?>
                {
                    ["file"] = $"{relativePath}:{result.StartLine}-{result.EndLine}",
                    ["signature"] = result.Signature,
                    ["code"] = result.SourceCode
                };

                if (knowledge != null)
                    compactResult["knowledge"] = knowledge;

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
