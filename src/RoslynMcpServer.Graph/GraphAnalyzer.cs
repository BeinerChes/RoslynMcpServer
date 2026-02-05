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


    private string? _solutionDir;
    public GraphAnalyzer(GraphDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// Analyzes a single document and extracts symbols and edges.
    /// Also records file metadata for change detection.
    /// </summary>
    public async Task AnalyzeDocumentAsync(Document document, long solutionId, string? solutionDir = null)
    {
        _solutionDir = solutionDir;  // Store for use in FindOrCreateTargetSymbolAsync

        var syntaxTree = await document.GetSyntaxTreeAsync();
        var semanticModel = await document.GetSemanticModelAsync();
        if (syntaxTree == null || semanticModel == null) return;

        var root = await syntaxTree.GetRootAsync();
        var absolutePath = document.FilePath ?? document.Name;

        // Convert to relative path for storage
        var filePath = ToRelativePath(absolutePath);

        // Compute file hash from disk - must match how EnsureGraphFreshAsync computes hashes
        string? fileHash = null;
        if (!string.IsNullOrEmpty(document.FilePath) && File.Exists(document.FilePath))
        {
            try
            {
                var content = File.ReadAllText(document.FilePath);
                fileHash = GraphDatabase.ComputeContentHash(content);
            }
            catch
            {
                // If we can't read from disk, skip hash
            }
        }

        // Find all type declarations
        // Track file in Files table (even for files with no type declarations)
        if (fileHash != null)
        {
            await _db.UpsertFileAsync(new FileRecord
            {
                SolutionId = solutionId,
                FilePath = filePath,
                LastModified = !string.IsNullOrEmpty(document.FilePath) ? new FileInfo(document.FilePath).LastWriteTimeUtc : DateTime.UtcNow,
                ContentHash = fileHash,
                LastAnalyzed = DateTime.UtcNow
            });
        }

        var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

        foreach (var typeDecl in types)
        {
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
            if (typeSymbol == null) continue;

            await ProcessTypeMembersAsync(typeDecl, typeSymbol, semanticModel, solutionId, filePath, fileHash);
        }

        // Handle top-level statements (C# 9+) — these produce GlobalStatementSyntax nodes
        // at the compilation unit level, outside any TypeDeclarationSyntax.
        // The compiler synthesizes a Program.<Main>$ method for them.
        var globalStatements = root.ChildNodes().OfType<GlobalStatementSyntax>().ToList();
        if (globalStatements.Count > 0)
        {
            await ProcessGlobalStatementsAsync(globalStatements, semanticModel, solutionId, filePath, fileHash);
        }
    }

    private async Task ProcessTypeMembersAsync(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        SemanticModel semanticModel,
        long solutionId,
        string filePath,
        string? fileHash)
    {
        foreach (var member in typeDecl.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    await ProcessMethodAsync(method, typeSymbol, semanticModel, solutionId, filePath, fileHash);
                    break;
                case PropertyDeclarationSyntax property:
                    await ProcessPropertyAsync(property, typeSymbol, semanticModel, solutionId, filePath, fileHash);
                    break;
                case FieldDeclarationSyntax field:
                    await ProcessFieldAsync(field, typeSymbol, semanticModel, solutionId, filePath, fileHash);
                    break;
                case ConstructorDeclarationSyntax ctor:
                    await ProcessConstructorAsync(ctor, typeSymbol, semanticModel, solutionId, filePath, fileHash);
                    break;
            }
        }
    }

    private async Task ProcessMethodAsync(
        MethodDeclarationSyntax method,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        long solutionId,
        string filePath,
        string? fileHash)
    {
        var methodSymbol = semanticModel.GetDeclaredSymbol(method);
        if (methodSymbol == null) return;

        var qualifiedName = GetQualifiedName(methodSymbol);
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
            FileHash = fileHash,
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
        string filePath,
        string? fileHash)
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
            FileHash = fileHash,
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
        string filePath,
        string? fileHash)
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
                FileHash = fileHash,
                Status = SymbolStatus.Analyzed
            });
        }
    }

    private async Task ProcessConstructorAsync(
        ConstructorDeclarationSyntax ctor,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        long solutionId,
        string filePath,
        string? fileHash)
    {
        var ctorSymbol = semanticModel.GetDeclaredSymbol(ctor);
        if (ctorSymbol == null) return;

        var qualifiedName = GetQualifiedName(ctorSymbol);
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
            FileHash = fileHash,
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
            var nodeEdges = await AnalyzeNodeAsync(node, fromSymbolId, solutionId, semanticModel);
            edges.AddRange(nodeEdges);
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
            var nodeEdges = await AnalyzeNodeAsync(node, fromSymbolId, solutionId, semanticModel);
            edges.AddRange(nodeEdges);
        }

        if (edges.Count > 0)
        {
            await _db.InsertEdgesAsync(edges);
        }
    }

    private async Task<List<EdgeRecord>> AnalyzeNodeAsync(SyntaxNode node, long fromSymbolId, long solutionId, SemanticModel semanticModel)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation:
                return await CreateEdgesForInvocationAsync(invocation, fromSymbolId, solutionId, semanticModel);

            case MemberAccessExpressionSyntax memberAccess:
                return await CreateEdgesForMemberAccessAsync(memberAccess, fromSymbolId, solutionId, semanticModel);

            case IdentifierNameSyntax identifier:
                var edge = await CreateEdgeForIdentifierAsync(identifier, fromSymbolId, solutionId, semanticModel);
                return edge != null ? [edge] : [];
        }

        return [];
    }

    private async Task<List<EdgeRecord>> CreateEdgesForInvocationAsync(
        InvocationExpressionSyntax invocation, long fromSymbolId, long solutionId, SemanticModel semanticModel)
    {
        var edges = new List<EdgeRecord>();
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);

        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            // Create edge to the called method (interface or concrete)
            var targetId = await FindOrCreateTargetSymbolAsync(method, solutionId);
            if (targetId.HasValue)
            {
                edges.Add(new EdgeRecord { FromSymbolId = fromSymbolId, ToSymbolId = targetId.Value, EdgeType = EdgeType.Calls });
            }

            // If calling an interface method, also create edges to all implementations
            if (method.ContainingType?.TypeKind == TypeKind.Interface)
            {
                var implementationEdges = await CreateEdgesForInterfaceImplementationsAsync(
                    method, fromSymbolId, solutionId, semanticModel);
                edges.AddRange(implementationEdges);
            }
        }

        return edges;
    }

    private async Task<List<EdgeRecord>> CreateEdgesForMemberAccessAsync(
        MemberAccessExpressionSyntax memberAccess, long fromSymbolId, long solutionId, SemanticModel semanticModel)
    {
        var edges = new List<EdgeRecord>();

        // Skip if this is part of an invocation (handled separately)
        if (memberAccess.Parent is InvocationExpressionSyntax) return edges;

        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
        var symbol = symbolInfo.Symbol;

        if (symbol is IPropertySymbol property)
        {
            var targetId = await FindOrCreateTargetSymbolAsync(property, solutionId);
            if (targetId.HasValue)
            {
                edges.Add(new EdgeRecord { FromSymbolId = fromSymbolId, ToSymbolId = targetId.Value, EdgeType = EdgeType.Accesses });
            }

            // If accessing an interface property, also create edges to all implementations
            if (property.ContainingType?.TypeKind == TypeKind.Interface)
            {
                var implementationEdges = await CreateEdgesForInterfacePropertyImplementationsAsync(
                    property, fromSymbolId, solutionId, semanticModel);
                edges.AddRange(implementationEdges);
            }
        }
        else if (symbol is IFieldSymbol field)
        {
            var targetId = await FindOrCreateTargetSymbolAsync(field, solutionId);
            if (targetId.HasValue)
            {
                var edgeType = IsWriteContext(memberAccess) ? EdgeType.Writes : EdgeType.Reads;
                edges.Add(new EdgeRecord { FromSymbolId = fromSymbolId, ToSymbolId = targetId.Value, EdgeType = edgeType });
            }
        }

        return edges;
    }

    private async Task<EdgeRecord?> CreateEdgeForIdentifierAsync(
        IdentifierNameSyntax identifier, long fromSymbolId, long solutionId, SemanticModel semanticModel)
    {
        // Skip if part of member access or invocation (those are handled separately)
        if (identifier.Parent is MemberAccessExpressionSyntax || identifier.Parent is InvocationExpressionSyntax)
            return null;

        var symbolInfo = semanticModel.GetSymbolInfo(identifier);
        var symbol = symbolInfo.Symbol;

        // Handle fields
        if (symbol is IFieldSymbol field && !field.IsConst)
        {
            var targetId = await FindOrCreateTargetSymbolAsync(field, solutionId);
            if (targetId.HasValue)
            {
                var edgeType = IsWriteContext(identifier) ? EdgeType.Writes : EdgeType.Reads;
                return new EdgeRecord { FromSymbolId = fromSymbolId, ToSymbolId = targetId.Value, EdgeType = edgeType };
            }
        }

        // Handle properties (for object initializers like: new Foo { Property = value })
        if (symbol is IPropertySymbol property)
        {
            var targetId = await FindOrCreateTargetSymbolAsync(property, solutionId);
            if (targetId.HasValue)
            {
                var edgeType = IsWriteContext(identifier) ? EdgeType.Writes : EdgeType.Accesses;
                return new EdgeRecord { FromSymbolId = fromSymbolId, ToSymbolId = targetId.Value, EdgeType = edgeType };
            }
        }

        // Handle method references (delegates/method groups like: server.AddTool("name", HandleFooAsync))
        if (symbol is IMethodSymbol method)
        {
            var targetId = await FindOrCreateTargetSymbolAsync(method, solutionId);
            if (targetId.HasValue)
            {
                return new EdgeRecord { FromSymbolId = fromSymbolId, ToSymbolId = targetId.Value, EdgeType = EdgeType.References };
            }
        }

        return null;
    }

    private static bool IsWriteContext(SyntaxNode node)
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
        // Check if symbol has source code in the solution
        var location = symbol.Locations.FirstOrDefault();
        var absolutePath = location?.SourceTree?.FilePath;

        // Skip external symbols - no source file means BCL/NuGet/external
        if (string.IsNullOrEmpty(absolutePath))
            return null;

        // Convert to relative path
        var filePath = ToRelativePath(absolutePath);

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

        // Create new symbol record for solution code
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
        {
            // Symbol exists - update FileHash and mark as Analyzed
            if (symbol.FileHash != null)
            {
                await _db.UpdateSymbolFileHashAsync(cachedId, symbol.FileHash);
            }
            return cachedId;
        }

        var existing = await _db.GetSymbolByQualifiedNameAsync(symbol.SolutionId, symbol.QualifiedName);
        if (existing != null)
        {
            _symbolCache[symbol.QualifiedName] = existing.Id;
            if (symbol.FileHash != null)
            {
                await _db.UpdateSymbolFileHashAsync(existing.Id, symbol.FileHash);
            }
            return existing.Id;
        }

        var id = await _db.InsertSymbolAsync(symbol);
        _symbolCache[symbol.QualifiedName] = id;
        return id;
    }

    private static string GetQualifiedName(ISymbol symbol)
    {
        // Use default ToDisplayString() to match the format used by FindSymbol
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


    private string ToRelativePath(string absolutePath)
    {
        if (_solutionDir != null && absolutePath.StartsWith(_solutionDir, StringComparison.OrdinalIgnoreCase))
        {
            // Normalize path separators to match EnsureGraphFreshAsync
            return absolutePath.Substring(_solutionDir.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
        return absolutePath;
    }


    private async Task<List<EdgeRecord>> CreateEdgesForInterfaceImplementationsAsync(
            IMethodSymbol interfaceMethod, long fromSymbolId, long solutionId, SemanticModel semanticModel)
    {
        var edges = new List<EdgeRecord>();
        var interfaceType = interfaceMethod.ContainingType;
        if (interfaceType == null) return edges;

        // Find all types in the compilation that implement this interface
        var compilation = semanticModel.Compilation;
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var treeSemanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync();

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeSymbol = treeSemanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                if (typeSymbol == null || typeSymbol.TypeKind == TypeKind.Interface) continue;

                // Check if this type implements the interface
                if (!typeSymbol.AllInterfaces.Contains(interfaceType, SymbolEqualityComparer.Default)) continue;

                // Find the implementation of this interface method
                var implementation = typeSymbol.FindImplementationForInterfaceMember(interfaceMethod);
                if (implementation is IMethodSymbol implMethod)
                {
                    var targetId = await FindOrCreateTargetSymbolAsync(implMethod, solutionId);
                    if (targetId.HasValue)
                    {
                        edges.Add(new EdgeRecord { FromSymbolId = fromSymbolId, ToSymbolId = targetId.Value, EdgeType = EdgeType.Calls });
                    }
                }
            }
        }

        return edges;
    }


    private async Task<List<EdgeRecord>> CreateEdgesForInterfacePropertyImplementationsAsync(
            IPropertySymbol interfaceProperty, long fromSymbolId, long solutionId, SemanticModel semanticModel)
    {
        var edges = new List<EdgeRecord>();
        var interfaceType = interfaceProperty.ContainingType;
        if (interfaceType == null) return edges;

        // Find all types in the compilation that implement this interface
        var compilation = semanticModel.Compilation;
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var treeSemanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync();

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeSymbol = treeSemanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                if (typeSymbol == null || typeSymbol.TypeKind == TypeKind.Interface) continue;

                // Check if this type implements the interface
                if (!typeSymbol.AllInterfaces.Contains(interfaceType, SymbolEqualityComparer.Default)) continue;

                // Find the implementation of this interface property
                var implementation = typeSymbol.FindImplementationForInterfaceMember(interfaceProperty);
                if (implementation is IPropertySymbol implProperty)
                {
                    var targetId = await FindOrCreateTargetSymbolAsync(implProperty, solutionId);
                    if (targetId.HasValue)
                    {
                        edges.Add(new EdgeRecord { FromSymbolId = fromSymbolId, ToSymbolId = targetId.Value, EdgeType = EdgeType.Accesses });
                    }
                }
            }
        }

        return edges;
    }

    /// <summary>
    /// Processes top-level statements (C# 9+) by creating a symbol for the synthesized entry point and analyzing call edges from global statements.
    /// </summary>
    /// <param name="globalStatements"></param>
    /// <param name="semanticModel"></param>
    /// <param name="solutionId"></param>
    /// <param name="filePath"></param>
    /// <param name="fileHash"></param>
    private async Task ProcessGlobalStatementsAsync(
        List<GlobalStatementSyntax> globalStatements,
        SemanticModel semanticModel,
        long solutionId,
        string filePath,
        string? fileHash)
    {
        // Get the synthesized entry point method (Program.<Main>$)
        var entryPoint = semanticModel.Compilation.GetEntryPoint(default);
        if (entryPoint == null) return;

        var qualifiedName = GetQualifiedName(entryPoint);
        var firstStatement = globalStatements[0];
        var lineSpan = firstStatement.GetLocation().GetLineSpan();

        var symbolId = await GetOrCreateSymbolAsync(new SymbolRecord
        {
            SolutionId = solutionId,
            Kind = SymbolKind.Method,
            Name = entryPoint.Name,
            QualifiedName = qualifiedName,
            FilePath = filePath,
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1,
            FileHash = fileHash,
            Status = SymbolStatus.Analyzed
        });

        // Analyze each global statement for call edges
        var edges = new List<EdgeRecord>();
        foreach (var globalStatement in globalStatements)
        {
            foreach (var node in globalStatement.DescendantNodes())
            {
                var nodeEdges = await AnalyzeNodeAsync(node, symbolId, solutionId, semanticModel);
                edges.AddRange(nodeEdges);
            }
        }

        if (edges.Count > 0)
        {
            await _db.InsertEdgesAsync(edges);
        }
    }
}
