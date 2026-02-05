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
                        isError = false
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
                        isError = false
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
                        isError = false
                    };
                }

                // Track for visualization sync
                LastSymbolTracker.Track(solutionPath!, $"{typeName}.{methodName}", "method");

                // Build compact response
                var relativePath = GetRelativePath(result.FilePath ?? "", solutionPath!);
                var compactResult = new Dictionary<string, object?>
                {
                    ["file"] = $"{relativePath}:{result.StartLine}-{result.EndLine}",
                    ["signature"] = result.Signature,
                    ["code"] = result.SourceCode
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
