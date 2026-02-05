using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Matches a value against a simple wildcard pattern (* for any chars).
    /// </summary>
    private static bool MatchesPattern(string value, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return true;
        if (string.IsNullOrEmpty(value)) return false;

        // Simple wildcard matching: * matches any sequence of characters
        if (pattern.Contains('*'))
        {
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(
                value, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // No wildcard - do contains match (case-insensitive)
        return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds all references to a symbol at the given position.
    /// </summary>
    public static async Task<FindReferencesResult> FindReferencesAsync(
        string solutionPath,
        string filePath,
        int line,
        int column,
        int maxResults = 100,
        string? projectFilter = null,
        string? fileFilter = null)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new FindReferencesResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        if (!File.Exists(filePath))
        {
            return new FindReferencesResult
            {
                Success = false,
                Error = $"Source file not found: {filePath}"
            };
        }

        using var workspace = CreateWorkspace();

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Find the document
            var normalizedPath = Path.GetFullPath(filePath);
            var document = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => string.Equals(
                    Path.GetFullPath(d.FilePath ?? ""),
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase));

            if (document == null)
            {
                return new FindReferencesResult
                {
                    Success = false,
                    Error = $"File not found in solution: {filePath}"
                };
            }

            // Get semantic model and find symbol at position
            var semanticModel = await document.GetSemanticModelAsync();
            var syntaxRoot = await document.GetSyntaxRootAsync();

            if (semanticModel == null || syntaxRoot == null)
            {
                return new FindReferencesResult
                {
                    Success = false,
                    Error = "Failed to get semantic model or syntax tree"
                };
            }

            // Convert 1-based line/column to 0-based position
            var text = await document.GetTextAsync();
            var position = text.Lines[line - 1].Start + (column - 1);

            // Use tolerant symbol finder (Issue #68)
            var symbol = await FindSymbolAtPositionWithToleranceAsync(semanticModel, position, workspace);

            if (symbol == null)
            {
                return new FindReferencesResult
                {
                    Success = false,
                    Error = $"No symbol found at {filePath}:{line}:{column}"
                };
            }

            Console.Error.WriteLine($"Finding references to: {symbol.ToDisplayString()}");

            // Find all references
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution);

            // Collect all reference locations
            var allLocations = new List<(Document Document, FileLinePositionSpan LineSpan)>();
            foreach (var refSymbol in references)
            {
                foreach (var location in refSymbol.Locations)
                {
                    allLocations.Add((location.Document, location.Location.GetLineSpan()));
                }
            }

            // Apply project filter
            if (!string.IsNullOrEmpty(projectFilter))
            {
                allLocations = allLocations
                    .Where(loc => MatchesPattern(loc.Document.Project.Name, projectFilter))
                    .ToList();
            }

            // Apply file filter
            if (!string.IsNullOrEmpty(fileFilter))
            {
                allLocations = allLocations
                    .Where(loc => MatchesPattern(loc.LineSpan.Path ?? "", fileFilter))
                    .ToList();
            }

            var totalAfterFilters = allLocations.Count;

            // Sort by file path, then line, before applying limit
            allLocations = allLocations
                .OrderBy(loc => loc.LineSpan.Path)
                .ThenBy(loc => loc.LineSpan.StartLinePosition.Line)
                .ToList();

            // Apply maxResults limit
            var limitedLocations = allLocations.Take(maxResults).ToList();

            // Build result list with previews
            var results = new List<ReferenceInfo>();
            foreach (var (refDocument, lineSpan) in limitedLocations)
            {
                // Get the source line text for context
                var refText = await refDocument.GetTextAsync();
                var refLine = lineSpan.StartLinePosition.Line;
                var lineText = refLine < refText.Lines.Count
                    ? refText.Lines[refLine].ToString().Trim()
                    : null;

                results.Add(new ReferenceInfo
                {
                    FilePath = refDocument.FilePath ?? "unknown",
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    EndColumn = lineSpan.EndLinePosition.Character + 1,
                    ProjectName = refDocument.Project.Name,
                    Preview = lineText
                });
            }

            Console.Error.WriteLine($"Found {totalAfterFilters} references (returning {results.Count})");

            return new FindReferencesResult
            {
                Success = true,
                SolutionPath = solutionPath,
                Symbol = new SymbolInfo
                {
                    Name = symbol.Name,
                    FullyQualifiedName = symbol.ToDisplayString(),
                    Kind = Services.SymbolSearchService.GetSymbolKind(symbol),
                    FilePath = filePath,
                    Line = line,
                    Column = column,
                    ContainingType = symbol.ContainingType?.ToDisplayString(),
                    Accessibility = symbol.DeclaredAccessibility.ToString(),
                    IsStatic = symbol.IsStatic,
                    Signature = Services.SymbolSearchService.GetSignature(symbol)
                },
                TotalFound = totalAfterFilters,
                ReturnedCount = results.Count,
                References = results
            };
        }
        catch (Exception ex)
        {
            return new FindReferencesResult
            {
                Success = false,
                Error = $"Failed to find references: {ex.Message}"
            };
        }
    }
}
