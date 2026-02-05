using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Registers the UpdateMethod tool with MCP server. Supports two modes: full replacement via newSourceCode, or targeted edit via oldText/newText parameters.
    /// </summary>
    /// <param name="server"></param>
    private static void RegisterUpdateMethodTool(McpServer server)
    {
        server.RegisterTool(
            "UpdateMethod",
            new ToolDefinition
            {
                Description = "Replaces a method's implementation with new source code. Uses Roslyn to precisely locate and replace the method while preserving surrounding code. Essential for making targeted changes to large classes.\n\nSupports two modes:\n1. Full replacement: provide `newSourceCode` with the complete method\n2. Edit mode: provide `oldText` + `newText` to make targeted edits within the method (token-efficient)",
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
                        },
                        oldText = new
                        {
                            type = "string",
                            description = "Text to find within the current method source. Use with newText for targeted edits instead of full replacement."
                        },
                        newText = new
                        {
                            type = "string",
                            description = "Replacement text (can be empty string for deletion). Required when oldText is provided."
                        },
                        replaceAll = new
                        {
                            type = "boolean",
                            description = "Replace all occurrences of oldText. Default: false (errors if multiple matches found)."
                        }
                    },
                    required = new[] { "typeName", "methodName" }
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
                var oldText = args?["oldText"]?.GetValue<string>();
                var newText = args?["newText"]?.GetValue<string>();
                var replaceAll = GetOptionalBool(args, "replaceAll", false);

                if (string.IsNullOrWhiteSpace(typeName))
                    return CreateToolError("Error: typeName is required");

                if (string.IsNullOrWhiteSpace(methodName))
                    return CreateToolError("Error: methodName is required");

                // Validate: either newSourceCode or oldText+newText, not both
                if (!string.IsNullOrEmpty(newSourceCode) && oldText != null)
                    return CreateToolError("Error: cannot provide both newSourceCode and oldText/newText. Use one mode or the other.");

                if (oldText != null && newText == null)
                    return CreateToolError("Error: newText is required when oldText is provided (can be empty string for deletion).");

                if (oldText == null && newText != null)
                    return CreateToolError("Error: oldText is required when newText is provided.");

                if (string.IsNullOrEmpty(newSourceCode) && oldText == null)
                    return CreateToolError("Error: provide either newSourceCode (full replacement) or oldText+newText (edit mode).");

                var result = await SolutionAnalyzerService.UpdateMethodAsync(
                    solutionPath!,
                    typeName,
                    methodName,
                    newSourceCode,
                    parameterTypes,
                    comment,
                    oldText,
                    newText,
                    replaceAll);

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
