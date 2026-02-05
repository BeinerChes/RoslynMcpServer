using Dapper;

namespace RoslynMcpServer.Graph;

/// <summary>
/// Database operations for web visualization.
/// </summary>
public sealed partial class GraphDatabase
{
    /// <summary>
    /// Gets all unique project names from file paths.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetProjectsAsync(long solutionId)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        // Extract project name from file path - assumes path contains project folder
        var symbols = await _connection.QueryAsync<string>(
            "SELECT DISTINCT FilePath FROM Symbols WHERE SolutionId = @SolutionId",
            new { SolutionId = solutionId });

        var projects = symbols
            .Select(ExtractProjectName)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        return projects!;
    }

    private static string? ExtractProjectName(string filePath)
    {
        // Try to extract project name from path like "C:\...\ProjectName\..."
        var parts = filePath.Replace('/', '\\').Split('\\');

        // Look for common patterns - find the folder before "src", "Source", or containing .csproj
        for (int i = parts.Length - 2; i >= 0; i--)
        {
            var part = parts[i];
            // Skip common non-project folders
            if (part is "src" or "Source" or "source" or "obj" or "bin" or "Debug" or "Release" or "net8.0" or "net10.0")
                continue;

            // If it looks like a project name (contains dots or is PascalCase)
            if (part.Contains('.') || (part.Length > 2 && char.IsUpper(part[0])))
            {
                return part;
            }
        }

        return parts.Length > 1 ? parts[^2] : null;
    }

    /// <summary>
    /// Gets all symbols for a solution, optionally filtered by project.
    /// </summary>
    public async Task<IReadOnlyList<SymbolRecord>> GetAllSymbolsAsync(long solutionId, string? projectFilter = null)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        if (string.IsNullOrEmpty(projectFilter))
        {
            var result = await _connection.QueryAsync<SymbolRecord>(
                "SELECT * FROM Symbols WHERE SolutionId = @SolutionId",
                new { SolutionId = solutionId });
            return result.ToList();
        }

        // Filter by project name in file path
        var filtered = await _connection.QueryAsync<SymbolRecord>(
            "SELECT * FROM Symbols WHERE SolutionId = @SolutionId AND FilePath LIKE @Pattern",
            new { SolutionId = solutionId, Pattern = $"%{projectFilter}%" });
        return filtered.ToList();
    }

    /// <summary>
    /// Gets all edges for symbols in a solution, optionally filtered by project.
    /// </summary>
    public async Task<IReadOnlyList<EdgeRecord>> GetAllEdgesAsync(long solutionId, string? projectFilter = null)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        if (string.IsNullOrEmpty(projectFilter))
        {
            var result = await _connection.QueryAsync<EdgeRecord>(
                """
                SELECT e.FromSymbolId, e.ToSymbolId, e.EdgeType
                FROM Edges e
                INNER JOIN Symbols s ON e.FromSymbolId = s.Id
                WHERE s.SolutionId = @SolutionId
                """,
                new { SolutionId = solutionId });
            return result.ToList();
        }

        // Filter by project name in file path
        var filtered = await _connection.QueryAsync<EdgeRecord>(
            """
            SELECT e.FromSymbolId, e.ToSymbolId, e.EdgeType
            FROM Edges e
            INNER JOIN Symbols s ON e.FromSymbolId = s.Id
            WHERE s.SolutionId = @SolutionId AND s.FilePath LIKE @Pattern
            """,
            new { SolutionId = solutionId, Pattern = $"%{projectFilter}%" });
        return filtered.ToList();
    }

    /// <summary>
    /// Gets a symbol by its ID.
    /// </summary>
    public async Task<SymbolRecord?> GetSymbolByIdAsync(long symbolId)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        return await _connection.QuerySingleOrDefaultAsync<SymbolRecord>(
            "SELECT * FROM Symbols WHERE Id = @Id",
            new { Id = symbolId });
    }

    /// <summary>
    /// Gets combined statistics for a solution.
    /// </summary>
    public async Task<GraphStats> GetStatsAsync(long solutionId)
    {
        var symbolStats = await GetSymbolStatsAsync(solutionId);
        var edgeStats = await GetEdgeStatsAsync(solutionId);

        return new GraphStats
        {
            Symbols = symbolStats,
            Edges = edgeStats
        };
    }
}

/// <summary>
/// Combined graph statistics.
/// </summary>
public sealed class GraphStats
{
    public required SymbolStats Symbols { get; set; }
    public required EdgeStats Edges { get; set; }
}
