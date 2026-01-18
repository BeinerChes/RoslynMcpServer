using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Security.Cryptography;
using System.Text;

namespace RoslynMcpServer.Graph;

/// <summary>
/// Analyzes Roslyn compilations to extract symbols and their relationships.
/// </summary>
public sealed class GraphAnalyzer
{
    private readonly GraphDatabase _db;
    private readonly Dictionary<string, long> _symbolCache = new();

    public GraphAnalyzer(GraphDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// Analyzes a single document and extracts symbols and edges.
    /// Also records file metadata for change detection.
    /// </summary>
    public async Task AnalyzeDocumentAsync(Document document, long solutionId)
    {
        var syntaxTree = await document.GetSyntaxTreeAsync();
        var semanticModel = await document.GetSemanticModelAsync();
        if (syntaxTree == null || semanticModel == null) return;

        var root = await syntaxTree.GetRootAsync();
        var filePath = document.FilePath ?? document.Name;

        // Record file metadata for change detection
        if (!string.IsNullOrEmpty(document.FilePath) && File.Exists(document.FilePath))
        {
            var fileRecord = GraphDatabase.CreateFileRecord(solutionId, document.FilePath);
            await _db.UpsertFileAsync(fileRecord);
        }

        // Find all type declarations
        var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

        foreach (var typeDecl in types)
        {
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
            if (typeSymbol == null) continue;

            // Process type members
            await ProcessTypeMembersAsync(typeDecl, typeSymbol, semanticModel, solutionId, filePath);
        }
    }

    private async Task ProcessTypeMembersAsync(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        SemanticModel semanticModel,
        long solutionId,
        string filePath)
    {
        foreach (var member in typeDecl.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    await ProcessMethodAsync(method, typeSymbol, semanticModel, solutionId, filePath);
                    break;
                case PropertyDeclarationSyntax property:
                    await ProcessPropertyAsync(property, typeSymbol, semanticModel, solutionId, filePath);
                    break;
                case FieldDeclarationSyntax field:
                    await ProcessFieldAsync(field, typeSymbol, semanticModel, solutionId, filePath);
                    break;
                case ConstructorDeclarationSyntax ctor:
                    await ProcessConstructorAsync(ctor, typeSymbol, semanticModel, solutionId, filePath);
                    break;
            }
        }
    }

    private async Task ProcessMethodAsync(
        MethodDeclarationSyntax method,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        long solutionId,
        string filePath)
    {
        var methodSymbol = semanticModel.GetDeclaredSymbol(method);
        if (methodSymbol == null) return;

        var qualifiedName = GetQualifiedName(methodSymbol);
        var bodyHash = ComputeBodyHash(method.Body?.ToString() ?? method.ExpressionBody?.ToString() ?? "");
        var lineSpan = method.GetLocation().GetLineSpan();

        var symbolId = await GetOrCreateSymbolAsync(new SymbolRecord
        {
            SolutionId = solutionId,
            Kind = SymbolKind.Method,
            Name = methodSymbol.Name,
            QualifiedName = qualifiedName,
            FilePath = filePath,
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1,
            BodyHash = bodyHash,
            Status = SymbolStatus.Analyzed
        });

        // Analyze method body for edges
        if (method.Body != null)
        {
            await AnalyzeBodyAsync(method.Body, symbolId, solutionId, semanticModel);
        }
        else if (method.ExpressionBody != null)
        {
            await AnalyzeExpressionAsync(method.ExpressionBody.Expression, symbolId, solutionId, semanticModel);
        }
    }

    private async Task ProcessPropertyAsync(
        PropertyDeclarationSyntax property,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        long solutionId,
        string filePath)
    {
        var propertySymbol = semanticModel.GetDeclaredSymbol(property);
        if (propertySymbol == null) return;

        var qualifiedName = GetQualifiedName(propertySymbol);
        var lineSpan = property.GetLocation().GetLineSpan();

        var symbolId = await GetOrCreateSymbolAsync(new SymbolRecord
        {
            SolutionId = solutionId,
            Kind = SymbolKind.Property,
            Name = propertySymbol.Name,
            QualifiedName = qualifiedName,
            FilePath = filePath,
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1,
            Status = SymbolStatus.Analyzed
        });

        // Analyze accessors
        if (property.AccessorList != null)
        {
            foreach (var accessor in property.AccessorList.Accessors)
            {
                if (accessor.Body != null)
                {
                    await AnalyzeBodyAsync(accessor.Body, symbolId, solutionId, semanticModel);
                }
                else if (accessor.ExpressionBody != null)
                {
                    await AnalyzeExpressionAsync(accessor.ExpressionBody.Expression, symbolId, solutionId, semanticModel);
                }
            }
        }
        else if (property.ExpressionBody != null)
        {
            await AnalyzeExpressionAsync(property.ExpressionBody.Expression, symbolId, solutionId, semanticModel);
        }
    }

    private async Task ProcessFieldAsync(
        FieldDeclarationSyntax field,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        long solutionId,
        string filePath)
    {
        foreach (var variable in field.Declaration.Variables)
        {
            var fieldSymbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
            if (fieldSymbol == null) continue;

            var qualifiedName = GetQualifiedName(fieldSymbol);
            var lineSpan = variable.GetLocation().GetLineSpan();

            await GetOrCreateSymbolAsync(new SymbolRecord
            {
                SolutionId = solutionId,
                Kind = SymbolKind.Field,
                Name = fieldSymbol.Name,
                QualifiedName = qualifiedName,
                FilePath = filePath,
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1,
                Status = SymbolStatus.Analyzed
            });
        }
    }

    private async Task ProcessConstructorAsync(
        ConstructorDeclarationSyntax ctor,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        long solutionId,
        string filePath)
    {
        var ctorSymbol = semanticModel.GetDeclaredSymbol(ctor);
        if (ctorSymbol == null) return;

        var qualifiedName = GetQualifiedName(ctorSymbol);
        var bodyHash = ComputeBodyHash(ctor.Body?.ToString() ?? ctor.ExpressionBody?.ToString() ?? "");
        var lineSpan = ctor.GetLocation().GetLineSpan();

        var symbolId = await GetOrCreateSymbolAsync(new SymbolRecord
        {
            SolutionId = solutionId,
            Kind = SymbolKind.Constructor,
            Name = ".ctor",
            QualifiedName = qualifiedName,
            FilePath = filePath,
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1,
            BodyHash = bodyHash,
            Status = SymbolStatus.Analyzed
        });

        if (ctor.Body != null)
        {
            await AnalyzeBodyAsync(ctor.Body, symbolId, solutionId, semanticModel);
        }
        else if (ctor.ExpressionBody != null)
        {
            await AnalyzeExpressionAsync(ctor.ExpressionBody.Expression, symbolId, solutionId, semanticModel);
        }
    }

    private async Task AnalyzeBodyAsync(BlockSyntax body, long fromSymbolId, long solutionId, SemanticModel semanticModel)
    {
        var edges = new List<EdgeRecord>();

        foreach (var node in body.DescendantNodes())
        {
            var edge = await AnalyzeNodeAsync(node, fromSymbolId, solutionId, semanticModel);
            if (edge != null) edges.Add(edge);
        }

        if (edges.Count > 0)
        {
            await _db.InsertEdgesAsync(edges);
        }
    }

    private async Task AnalyzeExpressionAsync(ExpressionSyntax expr, long fromSymbolId, long solutionId, SemanticModel semanticModel)
    {
        var edges = new List<EdgeRecord>();

        foreach (var node in expr.DescendantNodesAndSelf())
        {
            var edge = await AnalyzeNodeAsync(node, fromSymbolId, solutionId, semanticModel);
            if (edge != null) edges.Add(edge);
        }

        if (edges.Count > 0)
        {
            await _db.InsertEdgesAsync(edges);
        }
    }

    private async Task<EdgeRecord?> AnalyzeNodeAsync(SyntaxNode node, long fromSymbolId, long solutionId, SemanticModel semanticModel)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation:
                return await CreateEdgeForInvocationAsync(invocation, fromSymbolId, solutionId, semanticModel);

            case MemberAccessExpressionSyntax memberAccess:
                return await CreateEdgeForMemberAccessAsync(memberAccess, fromSymbolId, solutionId, semanticModel);

            case IdentifierNameSyntax identifier:
                return await CreateEdgeForIdentifierAsync(identifier, fromSymbolId, solutionId, semanticModel);
        }

        return null;
    }

    private async Task<EdgeRecord?> CreateEdgeForInvocationAsync(
        InvocationExpressionSyntax invocation, long fromSymbolId, long solutionId, SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            var targetId = await FindOrCreateTargetSymbolAsync(method, solutionId);
            if (targetId.HasValue)
            {
                return new EdgeRecord { FromSymbolId = fromSymbolId, ToSymbolId = targetId.Value, EdgeType = EdgeType.Calls };
            }
        }
        return null;
    }

    private async Task<EdgeRecord?> CreateEdgeForMemberAccessAsync(
        MemberAccessExpressionSyntax memberAccess, long fromSymbolId, long solutionId, SemanticModel semanticModel)
    {
        // Skip if this is part of an invocation (handled separately)
        if (memberAccess.Parent is InvocationExpressionSyntax) return null;

        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
        var symbol = symbolInfo.Symbol;

        if (symbol is IPropertySymbol property)
        {
            var targetId = await FindOrCreateTargetSymbolAsync(property, solutionId);
            if (targetId.HasValue)
            {
                return new EdgeRecord { FromSymbolId = fromSymbolId, ToSymbolId = targetId.Value, EdgeType = EdgeType.Accesses };
            }
        }
        else if (symbol is IFieldSymbol field)
        {
            var targetId = await FindOrCreateTargetSymbolAsync(field, solutionId);
            if (targetId.HasValue)
            {
                var edgeType = IsWriteContext(memberAccess) ? EdgeType.Writes : EdgeType.Reads;
                return new EdgeRecord { FromSymbolId = fromSymbolId, ToSymbolId = targetId.Value, EdgeType = edgeType };
            }
        }

        return null;
    }

    private async Task<EdgeRecord?> CreateEdgeForIdentifierAsync(
        IdentifierNameSyntax identifier, long fromSymbolId, long solutionId, SemanticModel semanticModel)
    {
        // Skip if part of member access or invocation
        if (identifier.Parent is MemberAccessExpressionSyntax || identifier.Parent is InvocationExpressionSyntax)
            return null;

        var symbolInfo = semanticModel.GetSymbolInfo(identifier);
        var symbol = symbolInfo.Symbol;

        if (symbol is IFieldSymbol field && !field.IsConst)
        {
            var targetId = await FindOrCreateTargetSymbolAsync(field, solutionId);
            if (targetId.HasValue)
            {
                var edgeType = IsWriteContext(identifier) ? EdgeType.Writes : EdgeType.Reads;
                return new EdgeRecord { FromSymbolId = fromSymbolId, ToSymbolId = targetId.Value, EdgeType = edgeType };
            }
        }

        return null;
    }

    private bool IsWriteContext(SyntaxNode node)
    {
        var parent = node.Parent;
        if (parent is AssignmentExpressionSyntax assignment)
        {
            return assignment.Left == node || assignment.Left.DescendantNodesAndSelf().Contains(node);
        }
        if (parent is PrefixUnaryExpressionSyntax prefix)
        {
            return prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression);
        }
        if (parent is PostfixUnaryExpressionSyntax postfix)
        {
            return postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression);
        }
        return false;
    }

    private async Task<long?> FindOrCreateTargetSymbolAsync(ISymbol symbol, long solutionId)
    {
        var qualifiedName = GetQualifiedName(symbol);

        // Check cache first
        if (_symbolCache.TryGetValue(qualifiedName, out var cachedId))
            return cachedId;

        // Check database
        var existing = await _db.GetSymbolByQualifiedNameAsync(solutionId, qualifiedName);
        if (existing != null)
        {
            _symbolCache[qualifiedName] = existing.Id;
            return existing.Id;
        }

        // Create placeholder for external symbols
        var location = symbol.Locations.FirstOrDefault();
        var filePath = location?.SourceTree?.FilePath ?? "external";
        var line = location?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
        var column = location?.GetLineSpan().StartLinePosition.Character + 1 ?? 0;

        var kind = symbol switch
        {
            IMethodSymbol => SymbolKind.Method,
            IPropertySymbol => SymbolKind.Property,
            IFieldSymbol => SymbolKind.Field,
            _ => SymbolKind.Method
        };

        var newSymbol = new SymbolRecord
        {
            SolutionId = solutionId,
            Kind = kind,
            Name = symbol.Name,
            QualifiedName = qualifiedName,
            FilePath = filePath,
            Line = line,
            Column = column,
            Status = SymbolStatus.Pending
        };

        var id = await _db.InsertSymbolAsync(newSymbol);
        _symbolCache[qualifiedName] = id;
        return id;
    }

    private async Task<long> GetOrCreateSymbolAsync(SymbolRecord symbol)
    {
        if (_symbolCache.TryGetValue(symbol.QualifiedName, out var cachedId))
            return cachedId;

        var existing = await _db.GetSymbolByQualifiedNameAsync(symbol.SolutionId, symbol.QualifiedName);
        if (existing != null)
        {
            _symbolCache[symbol.QualifiedName] = existing.Id;
            return existing.Id;
        }

        var id = await _db.InsertSymbolAsync(symbol);
        _symbolCache[symbol.QualifiedName] = id;
        return id;
    }

    private static string GetQualifiedName(ISymbol symbol)
    {
        // Use default ToDisplayString() to match the format used by roslyn_find_symbol
        return symbol.ToDisplayString();
    }

    private static string ComputeBodyHash(string body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        var normalized = NormalizeWhitespace(body);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..16]; // First 16 chars is enough
    }

    private static string NormalizeWhitespace(string text)
    {
        return string.Join(" ", text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
    }
}
