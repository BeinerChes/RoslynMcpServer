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
            "UpdateMethod",
            new ToolDefinition
            {
                Description = "Replaces a method's implementation with new source code. Uses Roslyn to precisely locate and replace the method while preserving surrounding code. Essential for making targeted changes to large classes.",
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
                        },
                        comment = new
                        {
                            type = "string",
                            description = "Plain text description of the method. Generates XML doc comment with <summary>, <param>, and <returns> tags, replacing any existing XML doc."
                        }
                    },
                    required = new[] { "typeName", "methodName", "newSourceCode" }
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
                var comment = args?["comment"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(typeName))
                    return CreateToolError("Error: typeName is required");

                if (string.IsNullOrWhiteSpace(methodName))
                    return CreateToolError("Error: methodName is required");

                if (string.IsNullOrWhiteSpace(newSourceCode))
                    return CreateToolError("Error: newSourceCode is required");

                var result = await SolutionAnalyzerService.UpdateMethodAsync(
                    solutionPath!,
                    typeName,
                    methodName,
                    newSourceCode,
                    parameterTypes,
                    comment);

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
                    return CreateToolResponse(errorResponse, true);
                }

                // Compact success response
                var relativePath = GetRelativePath(result.FilePath ?? "", solutionPath!);
                var compactResult = new
                {
                    file = $"{relativePath}:{result.StartLine}-{result.EndLine}",
                    oldSignature = result.OldSignature,
                    newSignature = result.NewSignature
                };

                return CreateToolResponse(compactResult, false);
            });
    }

    private static readonly string[] definitionArray34 = new[] { "typeName", "methodName", "newSourceCode" };
}
