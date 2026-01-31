using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Updates a method's source code in place.
    /// </summary>
    private static void RegisterUpdateMethodTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_update_method",
            new ToolDefinition
            {
                Description = "Replaces a method's implementation with new source code. Uses Roslyn to precisely locate and replace the method while preserving surrounding code. Essential for making targeted changes to large classes.",
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
                            description = "Name of the method to update. Use '.ctor' or the type name for constructors."
                        },
                        newSourceCode = new
                        {
                            type = "string",
                            description = "The complete new method source code including signature, attributes, and body"
                        },
                        parameterTypes = new
                        {
                            type = "string",
                            description = "Parameter types to identify a specific overload, e.g. 'string, int'. Required if multiple overloads exist."
                        }
                    },
                    required = definitionArray34
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    IdempotentHint = false
                }
            },
            async args =>
            {
                var (solutionPath, solutionError) = GetSolutionPathOrError();
                if (solutionError != null) return solutionError;

                var typeName = args?["typeName"]?.GetValue<string>();
                var methodName = args?["methodName"]?.GetValue<string>();
                var newSourceCode = args?["newSourceCode"]?.GetValue<string>();
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

                if (string.IsNullOrWhiteSpace(newSourceCode))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: newSourceCode is required" }
                        },
                        isError = true
                    };
                }

                var result = await SolutionAnalyzerService.UpdateMethodAsync(
                    solutionPath!,
                    typeName,
                    methodName,
                    newSourceCode,
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

    private static readonly string[] definitionArray34 = new[] { "typeName", "methodName", "newSourceCode" };
}
