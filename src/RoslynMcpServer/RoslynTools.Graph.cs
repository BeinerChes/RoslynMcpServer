using System.Text.Json;
using RoslynMcpServer.Graph;

namespace RoslynMcpServer;

/// <summary>
/// Graph infrastructure: internal graph building and shared response helpers.
/// </summary>
public static partial class RoslynTools
{
    /// <summary>
    /// Analyzes a solution and builds/updates the call graph. When incremental=true, skips files whose content hash matches the graph. Also cleans up symbols from deleted files.
    /// </summary>
    /// <param name="solutionPath"></param>
    /// <param name="incremental"></param>
    /// <param name="projectFilter"></param>
    /// <returns></returns>
    private static async Task<GraphAnalyzeResult> AnalyzeGraphAsync(
        string solutionPath, bool incremental, string? projectFilter)
    {
        using var db = new GraphDatabase(solutionPath);
        await db.OpenAsync();

        var solution = await db.GetOrCreateSolutionAsync(solutionPath);
        var analyzer = new GraphAnalyzer(db);
        var solutionDir = Path.GetDirectoryName(solutionPath);

        // Load the Roslyn solution
        var roslynSolution = await SolutionAnalyzerService.LoadSolutionAsync(solutionPath);
        if (roslynSolution == null)
        {
            return new GraphAnalyzeResult
            {
                Success = false,
                Error = "Failed to load solution"
            };
        }

        // In incremental mode, build a lookup of known file hashes from the graph
        Dictionary<string, string>? knownHashes = null;
        if (incremental && !string.IsNullOrEmpty(solutionDir))
        {
            var allSymbols = await db.GetAllSymbolsAsync(solution.Id, projectFilter);
            knownHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sym in allSymbols)
            {
                if (sym.FilePath != "external" && sym.FileHash != null)
                    knownHashes.TryAdd(sym.FilePath, sym.FileHash);
            }
        }

        var documentsAnalyzed = 0;
        var skippedDocuments = 0;

        foreach (var project in roslynSolution.Projects)
        {
            if (!string.IsNullOrEmpty(projectFilter) &&
                !project.Name.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var document in project.Documents)
            {
                if (document.FilePath == null) continue;
                if (document.FilePath.EndsWith(".g.cs") || document.FilePath.EndsWith(".designer.cs"))
                    continue;

                // Incremental: skip files whose hash hasn't changed
                if (knownHashes != null && !string.IsNullOrEmpty(solutionDir))
                {
                    var relativePath = document.FilePath;
                    if (document.FilePath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = document.FilePath.Substring(solutionDir.Length)
                            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                    }

                    try
                    {
                        var currentHash = GraphDatabase.ComputeFileHash(document.FilePath);

                        // Check Symbols table first, then Files table (for files with no type declarations)
                        if (knownHashes.TryGetValue(relativePath, out var storedHash))
                        {
                            if (currentHash == storedHash)
                            {
                                skippedDocuments++;
                                continue;
                            }
                            // Stale: delete old symbols first
                            await db.DeleteSymbolsByFileAsync(solution.Id, relativePath);
                        }
                        else
                        {
                            // Not in Symbols — check Files table (covers files with no type declarations)
                            var fileRecord = await db.GetFileAsync(solution.Id, relativePath);
                            if (fileRecord != null && fileRecord.ContentHash == currentHash)
                            {
                                skippedDocuments++;
                                continue;
                            }
                        }
                    }
                    catch { /* can't read → re-analyze */ }
                }

                await analyzer.AnalyzeDocumentAsync(document, solution.Id, solutionDir);
                documentsAnalyzed++;
            }
        }

        // Clean up symbols from files no longer in the solution
        var analyzedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in roslynSolution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath != null && !string.IsNullOrEmpty(solutionDir) &&
                    doc.FilePath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase))
                {
                    analyzedRelativePaths.Add(doc.FilePath.Substring(solutionDir.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
                }
            }
        }
        await db.CleanupSymbolsFromDeletedFilesAsync(solution.Id, analyzedRelativePaths);

        await db.UpdateSolutionAnalyzedAsync(solution.Id);

        var stats = await db.GetSymbolStatsAsync(solution.Id);
        var edgeStats = await db.GetEdgeStatsAsync(solution.Id);

        return new GraphAnalyzeResult
        {
            Success = true,
            SolutionPath = solutionPath,
            DocumentsAnalyzed = documentsAnalyzed,
            SymbolsFound = stats.Total,
            EdgesFound = edgeStats.Total,
            SymbolStats = stats,
            EdgeStats = edgeStats
        };
    }

    private static object CreateJsonResponse(object result, bool isError = false)
    {
        return new
        {
            content = new[]
            {
                new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) }
            },
            isError
        };
    }

    private static object CreateErrorResponse(string message)
    {
        return new
        {
            content = new[] { new { type = "text", text = message } },
            isError = false
        };
    }
}

// DTO for graph analysis
public class GraphAnalyzeResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? SolutionPath { get; set; }
    public int DocumentsAnalyzed { get; set; }
    public int SymbolsFound { get; set; }
    public int EdgesFound { get; set; }
    public SymbolStats? SymbolStats { get; set; }
    public EdgeStats? EdgeStats { get; set; }
}
