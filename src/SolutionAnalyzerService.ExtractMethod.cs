using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Extracts a code block into a new method using data flow analysis.
    /// </summary>
    public async Task<ExtractMethodResult> ExtractMethodAsync(
        string solutionPath,
        string filePath,
        int startLine,
        int endLine,
        string methodName,
        string? accessibility = "private")
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new ExtractMethodResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        if (!File.Exists(filePath))
        {
            return new ExtractMethodResult
            {
                Success = false,
                Error = $"Source file not found: {filePath}"
            };
        }

        if (startLine < 1 || endLine < startLine)
        {
            return new ExtractMethodResult
            {
                Success = false,
                Error = $"Invalid line range: startLine={startLine}, endLine={endLine}"
            };
        }

        if (string.IsNullOrWhiteSpace(methodName))
        {
            return new ExtractMethodResult
            {
                Success = false,
                Error = "Method name is required"
            };
        }

        using var workspace = CreateWorkspace();

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Find the document
            var documentId = solution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
            if (documentId == null)
            {
                return new ExtractMethodResult
                {
                    Success = false,
                    Error = $"File not found in solution: {filePath}"
                };
            }

            var document = solution.GetDocument(documentId);
            if (document == null)
            {
                return new ExtractMethodResult
                {
                    Success = false,
                    Error = $"Could not load document: {filePath}"
                };
            }

            var syntaxRoot = await document.GetSyntaxRootAsync();
            var semanticModel = await document.GetSemanticModelAsync();

            if (syntaxRoot == null || semanticModel == null)
            {
                return new ExtractMethodResult
                {
                    Success = false,
                    Error = "Could not get syntax tree or semantic model"
                };
            }

            var sourceText = await document.GetTextAsync();

            // Convert line numbers to positions
            var startPosition = sourceText.Lines[startLine - 1].Start;
            var endPosition = sourceText.Lines[Math.Min(endLine, sourceText.Lines.Count) - 1].End;
            var selectionSpan = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(startPosition, endPosition);

            // Find the containing method
            var containingMethod = syntaxRoot.FindNode(selectionSpan)
                .AncestorsAndSelf()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault();

            if (containingMethod == null)
            {
                return new ExtractMethodResult
                {
                    Success = false,
                    Error = "Selected code must be inside a method"
                };
            }

            // Find the containing type
            var containingType = containingMethod.Ancestors()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault();

            if (containingType == null)
            {
                return new ExtractMethodResult
                {
                    Success = false,
                    Error = "Could not find containing type"
                };
            }

            // Get statements within the selection
            var selectedStatements = containingMethod.Body?.Statements
                .Where(s => selectionSpan.Contains(s.Span) || selectionSpan.IntersectsWith(s.Span))
                .ToList();

            if (selectedStatements == null || selectedStatements.Count == 0)
            {
                return new ExtractMethodResult
                {
                    Success = false,
                    Error = "No statements found in selected range"
                };
            }

            // Analyze data flow
            var dataFlowAnalysis = semanticModel.AnalyzeDataFlow(
                selectedStatements.First(),
                selectedStatements.Last());

            if (dataFlowAnalysis == null || !dataFlowAnalysis.Succeeded)
            {
                return new ExtractMethodResult
                {
                    Success = false,
                    Error = "Data flow analysis failed for selected code"
                };
            }

            // Determine parameters (variables that flow in)
            var inputVariables = dataFlowAnalysis.DataFlowsIn
                .Where(s => s.Kind == SymbolKind.Local || s.Kind == SymbolKind.Parameter)
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<ISymbol>()
                .ToList();

            // Determine return values (variables assigned inside and used outside)
            var outputVariables = dataFlowAnalysis.DataFlowsOut
                .Where(s => s.Kind == SymbolKind.Local || s.Kind == SymbolKind.Parameter)
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<ISymbol>()
                .ToList();

            // Check for early returns in selection
            var hasReturn = selectedStatements.Any(s =>
                s.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Any());

            if (hasReturn)
            {
                return new ExtractMethodResult
                {
                    Success = false,
                    Error = "Cannot extract code containing return statements (early return handling not yet supported)"
                };
            }

            // Determine if method should be static
            var usesInstanceMembers = dataFlowAnalysis.DataFlowsIn
                .Any(s => s.Kind == SymbolKind.Field || s.Kind == SymbolKind.Property);
            var isStatic = containingMethod.Modifiers.Any(SyntaxKind.StaticKeyword) && !usesInstanceMembers;

            // Determine if async
            var hasAwait = selectedStatements.Any(s =>
                s.DescendantNodesAndSelf().OfType<AwaitExpressionSyntax>().Any());

            // Build parameter list
            var parameters = new List<string>();
            foreach (var input in inputVariables)
            {
                var typeName = GetTypeDisplayName(input);
                parameters.Add($"{typeName} {input.Name}");
            }

            // Determine return type and build return statement
            string returnType;
            string? returnStatement = null;
            string callArguments = string.Join(", ", inputVariables.Select(v => v.Name));

            if (outputVariables.Count == 0)
            {
                returnType = hasAwait ? "async Task" : "void";
            }
            else if (outputVariables.Count == 1)
            {
                var outVar = outputVariables[0];
                var outTypeName = GetTypeDisplayName(outVar);
                returnType = hasAwait ? $"async Task<{outTypeName}>" : outTypeName;
                returnStatement = $"return {outVar.Name};";
            }
            else
            {
                // Multiple outputs - use tuple
                var tupleTypes = outputVariables.Select(v => $"{GetTypeDisplayName(v)} {v.Name}");
                var tupleType = $"({string.Join(", ", tupleTypes)})";
                returnType = hasAwait ? $"async Task<{tupleType}>" : tupleType;
                returnStatement = $"return ({string.Join(", ", outputVariables.Select(v => v.Name))});";
            }

            // Build the extracted method
            var accessModifier = accessibility?.ToLower() switch
            {
                "public" => "public",
                "internal" => "internal",
                "protected" => "protected",
                _ => "private"
            };

            var staticModifier = isStatic ? "static " : "";
            var selectedCode = string.Join(Environment.NewLine,
                selectedStatements.Select(s => "        " + s.ToFullString().Trim()));

            if (returnStatement != null)
            {
                selectedCode += Environment.NewLine + "        " + returnStatement;
            }

            var newMethodCode = $@"
    {accessModifier} {staticModifier}{returnType} {methodName}({string.Join(", ", parameters)})
    {{
{selectedCode}
    }}";

            // Build the call site replacement
            string callSite;
            if (outputVariables.Count == 0)
            {
                var awaitPrefix = hasAwait ? "await " : "";
                callSite = $"{awaitPrefix}{methodName}({callArguments});";
            }
            else if (outputVariables.Count == 1)
            {
                var outVar = outputVariables[0];
                var awaitPrefix = hasAwait ? "await " : "";
                callSite = $"var {outVar.Name} = {awaitPrefix}{methodName}({callArguments});";
            }
            else
            {
                var awaitPrefix = hasAwait ? "await " : "";
                var varNames = string.Join(", ", outputVariables.Select(v => v.Name));
                callSite = $"var ({varNames}) = {awaitPrefix}{methodName}({callArguments});";
            }

            // Create the call site statement
            var callSiteStatement = SyntaxFactory.ParseStatement(callSite + Environment.NewLine)
                .WithLeadingTrivia(selectedStatements.First().GetLeadingTrivia());

            // Replace statements with call site
            var firstStatement = selectedStatements.First();
            var lastStatement = selectedStatements.Last();

            var newBody = containingMethod.Body!.ReplaceNodes(
                selectedStatements,
                (original, _) =>
                {
                    if (original == firstStatement)
                        return callSiteStatement;
                    return null!; // Remove other statements
                });

            // Remove null nodes (the other selected statements)
            var statementsToKeep = newBody.Statements
                .Where(s => s != null)
                .ToList();

            newBody = newBody.WithStatements(SyntaxFactory.List(statementsToKeep));

            // Update the containing method
            var newContainingMethod = containingMethod.WithBody(newBody);

            // Parse the new method
            var newMethodMember = SyntaxFactory.ParseMemberDeclaration(newMethodCode.Trim());
            if (newMethodMember == null)
            {
                return new ExtractMethodResult
                {
                    Success = false,
                    Error = "Failed to parse generated method code"
                };
            }

            // Insert new method after the containing method
            var methodIndex = containingType.Members.IndexOf(containingMethod);
            var newMembers = containingType.Members
                .Replace(containingMethod, newContainingMethod)
                .Insert(methodIndex + 1, newMethodMember);

            var newContainingType = containingType.WithMembers(newMembers);

            // Update the syntax tree
            var newRoot = syntaxRoot.ReplaceNode(containingType, newContainingType);

            // Format the code
            var formattedRoot = Formatter.Format(newRoot, workspace);

            // Write the updated file
            var newText = formattedRoot.ToFullString();
            await File.WriteAllTextAsync(filePath, newText);

            Console.Error.WriteLine($"Extracted method '{methodName}' at {filePath}");

            return new ExtractMethodResult
            {
                Success = true,
                FilePath = filePath,
                ExtractedMethod = new ExtractedMethodInfo
                {
                    Name = methodName,
                    Signature = $"{returnType} {methodName}({string.Join(", ", parameters)})",
                    IsStatic = isStatic,
                    IsAsync = hasAwait,
                    Accessibility = accessModifier
                },
                CallSite = new CallSiteInfo
                {
                    Replacement = callSite,
                    AtLine = startLine
                },
                Analysis = new DataFlowInfo
                {
                    InputVariables = inputVariables.Select(v => v.Name).ToList(),
                    OutputVariables = outputVariables.Select(v => v.Name).ToList(),
                    ReturnType = returnType
                }
            };
        }
        catch (Exception ex)
        {
            return new ExtractMethodResult
            {
                Success = false,
                Error = $"Failed to extract method: {ex.Message}"
            };
        }
    }

    private static string GetTypeDisplayName(ISymbol symbol)
    {
        if (symbol is ILocalSymbol local)
            return local.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (symbol is IParameterSymbol param)
            return param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (symbol is IFieldSymbol field)
            return field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return "object";
    }
}

/// <summary>
/// Result of extracting code into a new method.
/// </summary>
public class ExtractMethodResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? FilePath { get; init; }
    public ExtractedMethodInfo? ExtractedMethod { get; init; }
    public CallSiteInfo? CallSite { get; init; }
    public DataFlowInfo? Analysis { get; init; }
}

public class ExtractedMethodInfo
{
    public string? Name { get; init; }
    public string? Signature { get; init; }
    public bool IsStatic { get; init; }
    public bool IsAsync { get; init; }
    public string? Accessibility { get; init; }
}

public class CallSiteInfo
{
    public string? Replacement { get; init; }
    public int AtLine { get; init; }
}

public class DataFlowInfo
{
    public List<string>? InputVariables { get; init; }
    public List<string>? OutputVariables { get; init; }
    public string? ReturnType { get; init; }
}
