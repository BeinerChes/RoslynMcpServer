using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Finds all callers of a method at the given position.
    /// Unlike FindReferences, this returns only actual call sites, not declarations or other references.
    /// </summary>
    public static async Task<GetCallersResult> GetCallersAsync(
        string solutionPath,
        string filePath,
        int line,
        int column,
        int maxResults = 100,
        int offset = 0,
        string? projectFilter = null,
        string? fileFilter = null)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new GetCallersResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        if (!File.Exists(filePath))
        {
            return new GetCallersResult
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
                return new GetCallersResult
                {
                    Success = false,
                    Error = $"File not found in solution: {filePath}"
                };
            }

            // Get semantic model and find symbol at position
            var semanticModel = await document.GetSemanticModelAsync();
            if (semanticModel == null)
            {
                return new GetCallersResult
                {
                    Success = false,
                    Error = "Failed to get semantic model"
                };
            }

            // Convert 1-based line/column to 0-based position
            var text = await document.GetTextAsync();
            var position = text.Lines[line - 1].Start + (column - 1);

            // Use tolerant symbol finder (Issue #68)
            var symbol = await FindSymbolAtPositionWithToleranceAsync(semanticModel, position, workspace);

            if (symbol == null)
            {
                return new GetCallersResult
                {
                    Success = false,
                    Error = $"No symbol found at {filePath}:{line}:{column}"
                };
            }

            // Only methods, properties, and events can have callers
            if (symbol is not (IMethodSymbol or IPropertySymbol or IEventSymbol))
            {
                return new GetCallersResult
                {
                    Success = false,
                    Error = $"Symbol '{symbol.Name}' is a {symbol.Kind}, not a method/property/event. Only callable symbols have callers."
                };
            }

            Console.Error.WriteLine($"Finding callers of: {symbol.ToDisplayString()}");

            // Find all callers
            var callers = await SymbolFinder.FindCallersAsync(symbol, solution);
            var callerList = callers.ToList();

            // Flatten to individual call locations
            var allLocations = new List<(ISymbol CallingSymbol, Location Location, Document Document)>();
            foreach (var caller in callerList)
            {
                foreach (var location in caller.Locations)
                {
                    var callerDoc = solution.GetDocument(location.SourceTree);
                    if (callerDoc != null)
                    {
                        allLocations.Add((caller.CallingSymbol, location, callerDoc));
                    }
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
                    .Where(loc => MatchesPattern(loc.Document.FilePath ?? "", fileFilter))
                    .ToList();
            }

            var totalAfterFilters = allLocations.Count;

            // Sort by file path, then line
            allLocations = allLocations
                .OrderBy(loc => loc.Document.FilePath)
                .ThenBy(loc => loc.Location.GetLineSpan().StartLinePosition.Line)
                .ToList();

            // Apply pagination
            var paginatedLocations = allLocations
                .Skip(offset)
                .Take(maxResults)
                .ToList();

            // Build compact result list
            var solutionDir = Path.GetDirectoryName(solutionPath) ?? "";
            var results = new List<CallerInfo>();

            foreach (var (callingSymbol, location, callerDoc) in paginatedLocations)
            {
                var lineSpan = location.GetLineSpan();
                var callerFilePath = callerDoc.FilePath ?? "unknown";

                // Make path relative if possible
                var displayPath = callerFilePath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase)
                    ? callerFilePath[(solutionDir.Length + 1)..]
                    : callerFilePath;

                results.Add(new CallerInfo
                {
                    File = displayPath,
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Method = callingSymbol.Name,
                    Type = callingSymbol.ContainingType?.Name
                });
            }

            Console.Error.WriteLine($"Found {totalAfterFilters} callers (returning {results.Count})");

            // Build symbol signature
            var symbolSignature = symbol.ContainingType != null
                ? $"{symbol.ContainingType.Name}.{symbol.Name}"
                : symbol.Name;

            if (symbol is IMethodSymbol method)
            {
                var paramTypes = string.Join(", ", method.Parameters.Select(p => p.Type.Name));
                symbolSignature += $"({paramTypes})";
            }

            return new GetCallersResult
            {
                Success = true,
                Symbol = symbolSignature,
                TotalCallers = totalAfterFilters,
                ReturnedCount = results.Count,
                Callers = results
            };
        }
        catch (Exception ex)
        {
            return new GetCallersResult
            {
                Success = false,
                Error = $"Failed to find callers: {ex.Message}"
            };
        }
    }
}
