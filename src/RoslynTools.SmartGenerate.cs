using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpOps.Inference;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static void RegisterSmartGenerateMethodTool(McpServer server)
    {
        server.RegisterTool(
            "SmartGenerateMethod",
            new ToolDefinition
            {
                Description = "Generate and insert a C# method body using AI. Automatically extracts class fields for context, generates code with SharpTinyCoder, and inserts/updates the method. Returns success or asks Claude to do it manually if generation fails.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new
                        {
                            type = "string",
                            description = "Name of the class/struct containing the method"
                        },
                        methodSignature = new
                        {
                            type = "string",
                            description = "Full method signature, e.g. \"public void Initialize(AnalysisContext context)\""
                        },
                        description = new
                        {
                            type = "string",
                            description = "Optional natural language description of what the method should do"
                        },
                        temperature = new
                        {
                            type = "number",
                            description = "Sampling temperature (0.0-2.0). Default: 0.7",
                            minimum = 0.0,
                            maximum = 2.0
                        }
                    },
                    required = new[] { "typeName", "methodSignature" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    DestructiveHint = false,
                    IdempotentHint = false
                }
            },
            HandleSmartGenerateMethodAsync);
    }

    private static async Task<object> HandleSmartGenerateMethodAsync(JsonObject? args)
    {
        var (solutionPath, pathError) = GetSolutionPathOrError();
        if (pathError != null) return pathError;

        // Check if model is available
        var service = GetSharpOpsService();
        if (service == null)
        {
            return CreateErrorResponse(
                $"Model not available: {_sharpOpsError}. Please write the method manually.");
        }

        // Get required parameters
        if (!TryGetRequiredString(args, "typeName", out var typeName, out var error))
            return error!;
        if (!TryGetRequiredString(args, "methodSignature", out var methodSignature, out error))
            return error!;

        var description = args?["description"]?.GetValue<string>();
        var temperature = (float)GetOptionalDouble(args, "temperature", 0.7);

        try
        {
            // Load solution and find the type
            var solution = await SolutionAnalyzerService.LoadSolutionAsync(solutionPath!);
            if (solution == null)
                return CreateErrorResponse("Failed to load solution");

            // Find the type
            INamedTypeSymbol? typeSymbol = null;
            Document? typeDocument = null;
            TypeDeclarationSyntax? typeDeclaration = null;

            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                // Search for the type
                foreach (var syntaxTree in compilation.SyntaxTrees)
                {
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    var root = await syntaxTree.GetRootAsync();

                    foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(typeDecl);
                        if (symbol != null &&
                            (symbol.Name == typeName || symbol.ToDisplayString().EndsWith("." + typeName)))
                        {
                            typeSymbol = (INamedTypeSymbol)symbol;
                            typeDeclaration = typeDecl;
                            typeDocument = solution.GetDocument(syntaxTree);
                            break;
                        }
                    }
                    if (typeSymbol != null) break;
                }
                if (typeSymbol != null) break;
            }

            if (typeSymbol == null || typeDeclaration == null)
            {
                return CreateErrorResponse($"Type '{typeName}' not found. Please write the method manually.");
            }

            // Extract fields from the type for context
            var fields = new Dictionary<string, string>();
            foreach (var member in typeSymbol.GetMembers())
            {
                if (member is IFieldSymbol field && !field.IsImplicitlyDeclared)
                {
                    fields[field.Name] = field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                }
                else if (member is IPropertySymbol prop && !prop.IsImplicitlyDeclared)
                {
                    fields[prop.Name] = prop.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                }
            }

            // Check if method already exists (by parsing signature to get method name)
            var methodName = ExtractMethodNameFromSignature(methodSignature);
            bool methodExists = typeSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Any(m => m.Name == methodName);

            // Generate with model
            var sharpOps = service.GenerateSharpOps(
                methodSignature,
                fields.Count > 0 ? fields : null,
                description,
                temperature,
                topP: 0.9f,
                maxTokens: 512);

            // Try to compile to C#
            string? compiledBody;
            try
            {
                var sequence = SharpOps.SharpOpsSequence.ParseOps(sharpOps, null);
                compiledBody = SharpOps.SharpOpsCompiler.CompileToString(sequence);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(
                    $"Model generated invalid code. SharpOps: {sharpOps.Trim()}\nError: {ex.Message}\nPlease write the method manually.");
            }

            // Build full method code
            var fullMethodCode = $"{methodSignature}\n{compiledBody}";

            // Insert or update the method
            if (methodExists)
            {
                var updateResult = await SolutionAnalyzerService.UpdateMethodAsync(
                    solutionPath!, typeName, methodName, fullMethodCode, null);

                if (!updateResult.Success)
                {
                    return CreateErrorResponse(
                        $"Failed to update method: {updateResult.Error}\nGenerated code:\n{fullMethodCode}\nPlease apply manually.");
                }

                return CreateSuccessResponse(new
                {
                    action = "updated",
                    typeName,
                    methodName,
                    generatedCode = fullMethodCode,
                    fieldsUsed = fields.Keys.ToList(),
                    message = $"Successfully updated {typeName}.{methodName}"
                });
            }
            else
            {
                var addResult = await SolutionAnalyzerService.AddMemberAsync(
                    solutionPath!, typeName, fullMethodCode, null);

                if (!addResult.Success)
                {
                    return CreateErrorResponse(
                        $"Failed to add method: {addResult.Error}\nGenerated code:\n{fullMethodCode}\nPlease apply manually.");
                }

                return CreateSuccessResponse(new
                {
                    action = "added",
                    typeName,
                    methodName,
                    generatedCode = fullMethodCode,
                    fieldsUsed = fields.Keys.ToList(),
                    filePath = addResult.FilePath,
                    message = $"Successfully added {typeName}.{methodName}"
                });
            }
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Error: {ex.Message}\nPlease write the method manually.");
        }
    }

    private static string ExtractMethodNameFromSignature(string signature)
    {
        // Parse "public void MethodName(params)" -> "MethodName"
        var openParen = signature.IndexOf('(');
        if (openParen < 0) return signature.Split(' ').Last();

        var beforeParen = signature[..openParen].Trim();
        var parts = beforeParen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Last();
    }
}
