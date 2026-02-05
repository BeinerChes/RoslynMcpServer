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
        string symbolName,
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

        using var workspace = CreateWorkspace();

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Parse symbolName: could be "Method", "Type.Method", or "Namespace.Type.Method"
            // Extract the short name for SymbolFinder (last segment before any parentheses)
            var nameWithoutParams = symbolName.Contains('(')
                ? symbolName[..symbolName.IndexOf('(')]
                : symbolName;
            var segments = nameWithoutParams.Split('.');
            var shortName = segments[^1];

            // Find declarations matching the short name across all projects
            var declarations = new List<ISymbol>();
            foreach (var project in solution.Projects)
            {
                var projectDecls = await SymbolFinder.FindDeclarationsAsync(
                    project, shortName, ignoreCase: false);
                declarations.AddRange(projectDecls);
            }

            // Filter to callable symbols (methods, properties, events)
            var callables = declarations
                .Where(s => s is IMethodSymbol or IPropertySymbol or IEventSymbol)
                .ToList();

            if (callables.Count == 0)
            {
                return new GetCallersResult
                {
                    Success = true,
                    Symbol = symbolName,
                    TotalCallers = 0,
                    ReturnedCount = 0,
                    Callers = []
                };
            }

            // Match against the full symbolName for precision
            ISymbol? targetSymbol = null;
            foreach (var sym in callables)
            {
                var qualifiedName = sym.ContainingType != null
                    ? $"{sym.ContainingType.Name}.{sym.Name}"
                    : sym.Name;
                var fullQualifiedName = sym.ToDisplayString();

                if (string.Equals(qualifiedName, nameWithoutParams, StringComparison.Ordinal) ||
                    string.Equals(fullQualifiedName, nameWithoutParams, StringComparison.Ordinal) ||
                    string.Equals(sym.Name, nameWithoutParams, StringComparison.Ordinal))
                {
                    targetSymbol = sym;
                    break;
                }
            }

            // If no exact match, take first callable
            targetSymbol ??= callables[0];

            Console.Error.WriteLine($"Finding callers of: {targetSymbol.ToDisplayString()}");

            // Find all callers
            var callers = await SymbolFinder.FindCallersAsync(targetSymbol, solution);
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
            var symbolSignature = targetSymbol.ContainingType != null
                ? $"{targetSymbol.ContainingType.Name}.{targetSymbol.Name}"
                : targetSymbol.Name;

            if (targetSymbol is IMethodSymbol method)
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
