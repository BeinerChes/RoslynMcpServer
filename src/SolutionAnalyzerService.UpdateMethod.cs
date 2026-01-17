using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Updates a method's source code in place.
    /// </summary>
    public async Task<UpdateMethodResult> UpdateMethodAsync(
        string solutionPath,
        string typeName,
        string methodName,
        string newSourceCode,
        string? parameterTypes = null)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new UpdateMethodResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        using var workspace = MSBuildWorkspace.Create();
        RegisterFailureHandler(workspace);

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
                return new UpdateMethodResult
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

            // Also check for constructors
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
                return new UpdateMethodResult
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
                targetMethod = methods.FirstOrDefault(m =>
                    GetParameterSignature(m).Equals(parameterTypes, StringComparison.OrdinalIgnoreCase) ||
                    GetShortParameterSignature(m).Equals(parameterTypes, StringComparison.OrdinalIgnoreCase));

                if (targetMethod == null)
                {
                    return new UpdateMethodResult
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
                return new UpdateMethodResult
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
                return new UpdateMethodResult
                {
                    Success = false,
                    Error = "Method source location not found (may be in metadata)"
                };
            }

            var syntaxTree = location.SourceTree;
            if (syntaxTree == null)
            {
                return new UpdateMethodResult
                {
                    Success = false,
                    Error = "Could not get syntax tree for method"
                };
            }

            var root = await syntaxTree.GetRootAsync();
            var methodNode = root.FindNode(location.SourceSpan);

            // Navigate up to the full method declaration
            while (methodNode != null &&
                   methodNode is not MethodDeclarationSyntax &&
                   methodNode is not ConstructorDeclarationSyntax)
            {
                methodNode = methodNode.Parent;
            }

            if (methodNode == null)
            {
                return new UpdateMethodResult
                {
                    Success = false,
                    Error = "Could not locate method syntax node"
                };
            }

            // Parse the new source code
            var newSyntaxTree = CSharpSyntaxTree.ParseText(newSourceCode.Trim());
            var newRoot = await newSyntaxTree.GetRootAsync();

            // Find the method declaration in the new code
            SyntaxNode? newMethodNode = newRoot.DescendantNodes()
                .FirstOrDefault(n => n is MethodDeclarationSyntax || n is ConstructorDeclarationSyntax);

            // If the new code is just a method, it might be the root's first member
            if (newMethodNode == null)
            {
                // Try parsing as a class member
                var wrappedCode = $"class Temp {{ {newSourceCode} }}";
                var wrappedTree = CSharpSyntaxTree.ParseText(wrappedCode);
                var wrappedRoot = await wrappedTree.GetRootAsync();
                newMethodNode = wrappedRoot.DescendantNodes()
                    .FirstOrDefault(n => n is MethodDeclarationSyntax || n is ConstructorDeclarationSyntax);
            }

            if (newMethodNode == null)
            {
                return new UpdateMethodResult
                {
                    Success = false,
                    Error = "Could not parse new source code as a valid method"
                };
            }

            // Get the old signature for reporting
            var oldSignature = GetMethodSignature(targetMethod);
            var oldSpan = methodNode.GetLocation().GetLineSpan();

            // Replace the old node with the new one, preserving leading trivia
            var leadingTrivia = methodNode.GetLeadingTrivia();
            var trailingTrivia = methodNode.GetTrailingTrivia();

            var newMethodWithTrivia = newMethodNode
                .WithLeadingTrivia(leadingTrivia)
                .WithTrailingTrivia(trailingTrivia);

            var newRootNode = root.ReplaceNode(methodNode, newMethodWithTrivia);

            // Write the updated file - use syntaxTree.FilePath to avoid null dereference
            var filePath = syntaxTree.FilePath;
            var newText = newRootNode.ToFullString();

            await File.WriteAllTextAsync(filePath, newText);

            // Calculate new line numbers
            var newMethodInUpdated = newRootNode.DescendantNodes()
                .FirstOrDefault(n => n.Span.Start == newMethodWithTrivia.Span.Start &&
                                    (n is MethodDeclarationSyntax || n is ConstructorDeclarationSyntax));

            var newSpan = newMethodInUpdated?.GetLocation().GetLineSpan() ?? oldSpan;

            Console.Error.WriteLine($"Updated method at {filePath}:{newSpan.StartLinePosition.Line + 1}");

            // Extract new signature from the updated method
            string? newSignature = null;
            if (newMethodNode is MethodDeclarationSyntax newMethod)
            {
                newSignature = $"{newMethod.ReturnType} {newMethod.Identifier}({string.Join(", ", newMethod.ParameterList.Parameters)})";
            }
            else if (newMethodNode is ConstructorDeclarationSyntax newCtor)
            {
                newSignature = $"{newCtor.Identifier}({string.Join(", ", newCtor.ParameterList.Parameters)})";
            }

            return new UpdateMethodResult
            {
                Success = true,
                FilePath = filePath,
                TypeName = targetType.ToDisplayString(),
                MethodName = targetMethod.Name,
                StartLine = newSpan.StartLinePosition.Line + 1,
                EndLine = newSpan.EndLinePosition.Line + 1,
                OldSignature = oldSignature,
                NewSignature = newSignature
            };
        }
        catch (Exception ex)
        {
            return new UpdateMethodResult
            {
                Success = false,
                Error = $"Failed to update method: {ex.Message}"
            };
        }
    }
}
