using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Gets the full source code of a method including its body.
    /// </summary>
    public static async Task<GetMethodBodyResult> GetMethodBodyAsync(
        string solutionPath,
        string typeName,
        string methodName,
        string? parameterTypes = null)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new GetMethodBodyResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        using var workspace = CreateWorkspace();

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Find the type by name
            var typeSymbols = await SymbolFinder.FindSourceDeclarationsAsync(
                solution,
                name => name.Equals(typeName, StringComparison.Ordinal) ||
                        name.Equals(typeName, StringComparison.OrdinalIgnoreCase),
                SymbolFilter.Type);

            var targetType = typeSymbols
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault();

            if (targetType == null)
            {
                return new GetMethodBodyResult
                {
                    Success = false,
                    Error = $"Type not found: {typeName}"
                };
            }

            // Find all methods with the given name
            var methods = targetType.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary ||
                           m.MethodKind == MethodKind.Constructor)
                .ToList();

            // Also check for constructors if methodName matches type name or is ".ctor"
            if (methodName == ".ctor" || methodName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                var constructors = targetType.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(m => m.MethodKind == MethodKind.Constructor)
                    .ToList();

                if (constructors.Count > 0 && methods.Count == 0)
                {
                    methods = constructors;
                }
            }

            if (methods.Count == 0)
            {
                return new GetMethodBodyResult
                {
                    Success = false,
                    Error = $"Method '{methodName}' not found in type '{typeName}'",
                    TypeName = targetType.ToDisplayString()
                };
            }

            // If multiple overloads exist, try to match by parameter types
            IMethodSymbol? targetMethod = null;

            if (methods.Count == 1)
            {
                targetMethod = methods[0];
            }
            else if (!string.IsNullOrEmpty(parameterTypes))
            {
                // Try to match by parameter signature
                targetMethod = methods.FirstOrDefault(m =>
                    GetParameterSignature(m).Equals(parameterTypes, StringComparison.OrdinalIgnoreCase) ||
                    GetShortParameterSignature(m).Equals(parameterTypes, StringComparison.OrdinalIgnoreCase));

                if (targetMethod == null)
                {
                    return new GetMethodBodyResult
                    {
                        Success = false,
                        Error = $"No overload matches parameter types '{parameterTypes}'",
                        TypeName = targetType.ToDisplayString(),
                        MethodName = methodName,
                        AvailableOverloads = methods.Select(m => GetMethodSignature(m)).ToList()
                    };
                }
            }
            else
            {
                // Multiple overloads, return list for user to choose
                return new GetMethodBodyResult
                {
                    Success = false,
                    Error = $"Multiple overloads found for '{methodName}'. Specify parameterTypes to select one.",
                    TypeName = targetType.ToDisplayString(),
                    MethodName = methodName,
                    AvailableOverloads = methods.Select(m => GetMethodSignature(m)).ToList()
                };
            }

            // Get the syntax node for the method
            var location = targetMethod.Locations.FirstOrDefault(l => l.IsInSource);
            if (location == null)
            {
                return new GetMethodBodyResult
                {
                    Success = false,
                    Error = "Method source location not found (may be in metadata)"
                };
            }

            var syntaxTree = location.SourceTree;
            if (syntaxTree == null)
            {
                return new GetMethodBodyResult
                {
                    Success = false,
                    Error = "Could not get syntax tree for method"
                };
            }

            var root = await syntaxTree.GetRootAsync();
            var methodNode = root.FindNode(location.SourceSpan);

            // Navigate up to the full method declaration if needed
            while (methodNode != null &&
                   methodNode is not MethodDeclarationSyntax &&
                   methodNode is not ConstructorDeclarationSyntax &&
                   methodNode is not PropertyDeclarationSyntax)
            {
                methodNode = methodNode.Parent;
            }

            if (methodNode == null)
            {
                return new GetMethodBodyResult
                {
                    Success = false,
                    Error = "Could not locate method syntax node"
                };
            }

            // Get full text including leading trivia (comments, attributes)
            var fullText = methodNode.ToFullString();
            var lineSpan = location.GetLineSpan();
            var methodSpan = methodNode.GetLocation().GetLineSpan();

            Console.Error.WriteLine($"Found method at {lineSpan.Path}:{methodSpan.StartLinePosition.Line + 1}");

            return new GetMethodBodyResult
            {
                Success = true,
                SolutionPath = solutionPath,
                TypeName = targetType.ToDisplayString(),
                MethodName = targetMethod.Name,
                FilePath = lineSpan.Path,
                StartLine = methodSpan.StartLinePosition.Line + 1,
                EndLine = methodSpan.EndLinePosition.Line + 1,
                Signature = GetMethodSignature(targetMethod),
                SourceCode = fullText.TrimEnd()
            };
        }
        catch (Exception ex)
        {
            return new GetMethodBodyResult
            {
                Success = false,
                Error = $"Failed to get method body: {ex.Message}"
            };
        }
    }

    private static string GetMethodSignature(IMethodSymbol method)
    {
        var returnType = method.MethodKind == MethodKind.Constructor
            ? ""
            : $"{method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} ";

        var parameters = string.Join(", ", method.Parameters.Select(p =>
            $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}"));

        return $"{returnType}{method.Name}({parameters})";
    }

    private static string GetParameterSignature(IMethodSymbol method)
    {
        return string.Join(", ", method.Parameters.Select(p =>
            p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    private static string GetShortParameterSignature(IMethodSymbol method)
    {
        return string.Join(", ", method.Parameters.Select(p => p.Type.Name));
    }
}
