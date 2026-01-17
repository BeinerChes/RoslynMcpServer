using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
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
                    required = new[] { "solutionPath", "typeName", "methodName" }
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

                var result = await _analyzerService!.GetMethodBodyAsync(
                    solutionPath,
                    typeName,
                    methodName,
                    parameterTypes);

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
