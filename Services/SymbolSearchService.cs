using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcpServer.Services;

public class SymbolSearchService
{


    public async Task<FindSymbolResult> SearchSymbolsAsync(
        Solution solution, string solutionPath,
        string pattern,
        SymbolKindFilter kindFilter = SymbolKindFilter.All,
        MatchType matchType = MatchType.Contains,
        int maxResults = 100,
        bool compact = true)
    {
        Console.Error.WriteLine($"Searching for symbols matching '{pattern}'...");

        // Convert our filter to Roslyn's SymbolFilter
        var symbolFilter = ConvertToSymbolFilter(kindFilter);

        // Use SymbolFinder to search for declarations
        Func<string, bool> predicate = matchType switch
        {
            MatchType.Exact => name => name.Equals(pattern, StringComparison.Ordinal),
            MatchType.ExactIgnoreCase => name => name.Equals(pattern, StringComparison.OrdinalIgnoreCase),
            MatchType.Contains => name => name.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            MatchType.Prefix => name => name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            MatchType.Suffix => name => name.EndsWith(pattern, StringComparison.OrdinalIgnoreCase),
            _ => name => name.Contains(pattern, StringComparison.OrdinalIgnoreCase)
        };

        var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
            solution,
            predicate,
            symbolFilter);

        var results = new List<SymbolInfo>();
        foreach (var symbol in symbols.Take(maxResults))
        {
            // Get location info
            var location = symbol.Locations.FirstOrDefault();
            var lineSpan = location?.GetLineSpan();

            var info = new SymbolInfo
            {
                // Core fields - always included
                Name = symbol.Name,
                FullyQualifiedName = symbol.ToDisplayString(),
                Kind = GetSymbolKind(symbol),
                FilePath = lineSpan?.Path,
                Line = lineSpan?.StartLinePosition.Line + 1, // 1-based
                                                             // Optional fields - only in detailed mode
                Column = compact ? null : lineSpan?.StartLinePosition.Character + 1,
                ContainingType = compact ? null : symbol.ContainingType?.ToDisplayString(),
                Accessibility = compact ? null : symbol.DeclaredAccessibility.ToString(),
                IsStatic = compact ? null : symbol.IsStatic,
                Signature = compact ? null : GetSignature(symbol)
            };

            results.Add(info);
        }

        Console.Error.WriteLine($"Found {results.Count} symbols");

        return new FindSymbolResult
        {
            Success = true, SolutionPath = solutionPath,
            Pattern = pattern,
            TotalFound = symbols.Count(),
            Symbols = results
        };
    }


    public async Task<string?> GetSymbolQualifiedNameAsync(
        Solution solution, Workspace workspace,
        string filePath,
        int line,
        int column)
    {
        // Find the document
        var normalizedPath = Path.GetFullPath(filePath);
        var document = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(
                Path.GetFullPath(d.FilePath ?? ""),
                normalizedPath,
                StringComparison.OrdinalIgnoreCase));

        if (document == null) return null;

        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null) return null;

        var text = await document.GetTextAsync();
        var position = text.Lines[line - 1].Start + (column - 1);

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, workspace);
        return symbol?.ToDisplayString();
    }


    private static SymbolFilter ConvertToSymbolFilter(SymbolKindFilter filter)
    {
        return filter switch
        {
            SymbolKindFilter.Type => SymbolFilter.Type,
            SymbolKindFilter.Member => SymbolFilter.Member,
            SymbolKindFilter.Namespace => SymbolFilter.Namespace,
            SymbolKindFilter.TypeAndMember => SymbolFilter.TypeAndMember,
            _ => SymbolFilter.All
        };
    }


    public static string GetSymbolKind(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol typeSymbol => typeSymbol.TypeKind.ToString(),
            IMethodSymbol => "Method",
            IPropertySymbol => "Property",
            IFieldSymbol => "Field",
            IEventSymbol => "Event",
            INamespaceSymbol => "Namespace",
            ILocalSymbol => "Local",
            IParameterSymbol => "Parameter",
            _ => symbol.Kind.ToString()
        };
    }


    public static string GetSignature(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => $"{method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {method.Name}({string.Join(", ", method.Parameters.Select(p => $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}"))})",
            IPropertySymbol property => $"{property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {property.Name} {{ {(property.GetMethod != null ? "get; " : "")}{(property.SetMethod != null ? "set; " : "")}}}",
            IFieldSymbol field => $"{field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {field.Name}",
            IEventSymbol evt => $"event {evt.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {evt.Name}",
            INamedTypeSymbol type => $"{type.TypeKind.ToString().ToLower()} {type.Name}{(type.TypeParameters.Length > 0 ? $"<{string.Join(", ", type.TypeParameters.Select(t => t.Name))}>" : "")}",
            _ => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
        };
    }
}

