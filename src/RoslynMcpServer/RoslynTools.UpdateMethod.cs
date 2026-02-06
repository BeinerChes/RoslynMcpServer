using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
                Description = "Replaces a method's implementation with new source code. Uses Roslyn to precisely locate and replace the method while preserving surrounding code. Essential for making targeted changes to large classes.\n\nSupports three modes:\n1. Full replacement: provide `newSourceCode` with the complete method\n2. Edit mode: provide `oldText` + `newText` to make targeted edits within the method (token-efficient)\n3. Auto mode: set `auto=true` to regenerate the method body using the built-in SharpTinyCoder model",
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
                        },
                        auto = new
                        {
                            type = "boolean",
                            description = "When true, regenerates the method body using the built-in SharpTinyCoder model. No code input needed — just typeName + methodName. If generation fails, a NotImplementedException stub is inserted; use a non-auto UpdateMethod to provide your implementation."
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
                var auto = GetOptionalBool(args, "auto", false);

                if (string.IsNullOrWhiteSpace(typeName))
                    return CreateToolError("Error: typeName is required");

                if (string.IsNullOrWhiteSpace(methodName))
                    return CreateToolError("Error: methodName is required");

                // Validate: auto is mutually exclusive with newSourceCode/oldText/newText
                if (auto && (!string.IsNullOrEmpty(newSourceCode) || oldText != null || newText != null))
                    return CreateToolError("Error: auto=true cannot be combined with newSourceCode or oldText/newText. Auto mode regenerates the method body automatically.");

                // Validate: either newSourceCode, oldText+newText, or auto
                if (!auto)
                {
                    if (!string.IsNullOrEmpty(newSourceCode) && oldText != null)
                        return CreateToolError("Error: cannot provide both newSourceCode and oldText/newText. Use one mode or the other.");

                    if (oldText != null && newText == null)
                        return CreateToolError("Error: newText is required when oldText is provided (can be empty string for deletion).");

                    if (oldText == null && newText != null)
                        return CreateToolError("Error: oldText is required when newText is provided.");

                    if (string.IsNullOrEmpty(newSourceCode) && oldText == null)
                        return CreateToolError("Error: provide either newSourceCode (full replacement), oldText+newText (edit mode), or auto=true.");
                }

                // Auto mode: get the existing method, extract signature, regenerate body
                bool autoGenerationFailed = false;
                string? generatedCode = null;
                if (auto)
                {
                    if (_codeGenPlugin == null)
                    {
                        return CreateToolError("auto=true requires SharpTinyCoder plugin. Install RoslynMcpServer.CodeGen to enable AI code generation.");
                    }

                    var bodyResult = await SolutionAnalyzerService.GetMethodBodyAsync(
                        solutionPath!, typeName, methodName, parameterTypes);

                    if (!bodyResult.Success)
                        return CreateToolError($"Error: {bodyResult.Error}");

                    // Parse the method source to extract the full signature
                    var tree = CSharpSyntaxTree.ParseText($"class _T {{ {bodyResult.SourceCode} }}");
                    var parseRoot = await tree.GetRootAsync();
                    var methodDecl = parseRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault();

                    if (methodDecl == null)
                        return CreateToolError("Error: could not parse existing method to extract signature. Auto mode only supports regular methods (not constructors).");

                    var signature = methodDecl.WithBody(null).WithExpressionBody(null)
                        .WithSemicolonToken(default).WithLeadingTrivia().WithTrailingTrivia()
                        .NormalizeWhitespace().ToFullString().TrimEnd();

                    var autoResult = await _codeGenPlugin.HandleAutoGenerateAsync(solutionPath!, typeName, signature, comment);
                    autoGenerationFailed = autoResult.Failed;
                    newSourceCode = autoResult.FullMemberCode;

                    if (!autoGenerationFailed)
                    {
                        generatedCode = autoResult.FullMemberCode;
                    }
                }

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

                // Collect finetune data when not auto-generated and plugin is available
                if (!auto && _codeGenPlugin != null && result.Success)
                {
                    _ = Task.Run(() => _codeGenPlugin.CollectFinetuneDataAsync(
                        solutionPath!, result.FilePath!, typeName, result.MethodName!, comment, parameterTypes));
                }

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

                if (auto)
                {
                    var autoResponse = new Dictionary<string, object?>
                    {
                        ["file"] = $"{relativePath}:{result.StartLine}-{result.EndLine}",
                        ["oldSignature"] = result.OldSignature,
                        ["newSignature"] = result.NewSignature,
                        ["autoGenerated"] = true,
                        ["autoGenerationFailed"] = autoGenerationFailed,
                    };
                    if (generatedCode != null)
                    {
                        autoResponse["generatedCode"] = generatedCode;
                    }
                    return CreateToolResponse(autoResponse, false);
                }

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
