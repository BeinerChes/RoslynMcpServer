using Dapper;

namespace RoslynMcpServer.Graph;

/// <summary>
/// Edge-related database operations and graph queries.
/// </summary>
public sealed partial class GraphDatabase
{
    /// <summary>
    /// Inserts an edge between two symbols.
    /// </summary>
    public async Task InsertEdgeAsync(long fromSymbolId, long toSymbolId, EdgeType edgeType)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        await _connection.ExecuteAsync(
            """
            INSERT OR IGNORE INTO Edges (FromSymbolId, ToSymbolId, EdgeType)
            VALUES (@FromSymbolId, @ToSymbolId, @EdgeType)
            """,
            new { FromSymbolId = fromSymbolId, ToSymbolId = toSymbolId, EdgeType = edgeType.ToString() });
    }

    /// <summary>
    /// Inserts multiple edges in a batch.
    /// </summary>
    public async Task InsertEdgesAsync(IEnumerable<EdgeRecord> edges)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        await _connection.ExecuteAsync(
            """
            INSERT OR IGNORE INTO Edges (FromSymbolId, ToSymbolId, EdgeType)
            VALUES (@FromSymbolId, @ToSymbolId, @EdgeType)
            """,
            edges.Select(e => new { e.FromSymbolId, e.ToSymbolId, EdgeType = e.EdgeType.ToString() }));
    }

    /// <summary>
    /// Deletes all outgoing edges from a symbol (used before re-analyzing).
    /// </summary>
    public async Task DeleteEdgesFromSymbolAsync(long symbolId)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        await _connection.ExecuteAsync(
            "DELETE FROM Edges WHERE FromSymbolId = @SymbolId",
            new { SymbolId = symbolId });
    }

    /// <summary>
    /// Gets direct callers of a symbol (one level up).
    /// </summary>
    public async Task<IReadOnlyList<SymbolRecord>> GetCallersAsync(long symbolId, EdgeType? edgeType = null)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var sql = """
            SELECT s.* FROM Symbols s
            INNER JOIN Edges e ON e.FromSymbolId = s.Id
            WHERE e.ToSymbolId = @SymbolId
            """;

        var parameters = new DynamicParameters();
        parameters.Add("SymbolId", symbolId);

        if (edgeType.HasValue)
        {
            sql += " AND e.EdgeType = @EdgeType";
            parameters.Add("EdgeType", edgeType.Value.ToString());
        }

        var result = await _connection.QueryAsync<SymbolRecord>(sql, parameters);
        return result.ToList();
    }

    /// <summary>
    /// Gets direct callees of a symbol (one level down).
    /// </summary>
    public async Task<IReadOnlyList<SymbolRecord>> GetCalleesAsync(long symbolId, EdgeType? edgeType = null)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var sql = """
            SELECT s.* FROM Symbols s
            INNER JOIN Edges e ON e.ToSymbolId = s.Id
            WHERE e.FromSymbolId = @SymbolId
            """;

        var parameters = new DynamicParameters();
        parameters.Add("SymbolId", symbolId);

        if (edgeType.HasValue)
        {
            sql += " AND e.EdgeType = @EdgeType";
            parameters.Add("EdgeType", edgeType.Value.ToString());
        }

        var result = await _connection.QueryAsync<SymbolRecord>(sql, parameters);
        return result.ToList();
    }

    /// <summary>
    /// Gets recursive callers up to a maximum depth.
    /// Uses iterative approach to avoid deep recursion.
    /// </summary>
    public async Task<IReadOnlyList<SymbolRecord>> GetRecursiveCallersAsync(
        long symbolId, int maxDepth = -1, EdgeType? edgeType = null)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var visited = new HashSet<long> { symbolId };
        var result = new List<SymbolRecord>();
        var currentLevel = new List<long> { symbolId };
        var depth = 0;

        while (currentLevel.Count > 0 && (maxDepth < 0 || depth < maxDepth))
        {
            var callers = await GetCallerIdsAsync(currentLevel, edgeType);
            var newCallers = callers.Where(id => visited.Add(id)).ToList();

            if (newCallers.Count > 0)
            {
                var symbols = await GetSymbolsByIdsAsync(newCallers);
                result.AddRange(symbols);
            }

            currentLevel = newCallers;
            depth++;
        }

        return result;
    }

    /// <summary>
    /// Gets recursive callees up to a maximum depth.
    /// Uses iterative approach to avoid deep recursion.
    /// </summary>
    public async Task<IReadOnlyList<SymbolRecord>> GetRecursiveCalleesAsync(
        long symbolId, int maxDepth = -1, EdgeType? edgeType = null)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var visited = new HashSet<long> { symbolId };
        var result = new List<SymbolRecord>();
        var currentLevel = new List<long> { symbolId };
        var depth = 0;

        while (currentLevel.Count > 0 && (maxDepth < 0 || depth < maxDepth))
        {
            var callees = await GetCalleeIdsAsync(currentLevel, edgeType);
            var newCallees = callees.Where(id => visited.Add(id)).ToList();

            if (newCallees.Count > 0)
            {
                var symbols = await GetSymbolsByIdsAsync(newCallees);
                result.AddRange(symbols);
            }

            currentLevel = newCallees;
            depth++;
        }

        return result;
    }

    private async Task<IReadOnlyList<long>> GetCallerIdsAsync(IEnumerable<long> symbolIds, EdgeType? edgeType)
    {
        var sql = "SELECT DISTINCT FromSymbolId FROM Edges WHERE ToSymbolId IN @SymbolIds";
        var parameters = new DynamicParameters();
        parameters.Add("SymbolIds", symbolIds);

        if (edgeType.HasValue)
        {
            sql += " AND EdgeType = @EdgeType";
            parameters.Add("EdgeType", edgeType.Value.ToString());
        }

        var result = await _connection!.QueryAsync<long>(sql, parameters);
        return result.ToList();
    }

    private async Task<IReadOnlyList<long>> GetCalleeIdsAsync(IEnumerable<long> symbolIds, EdgeType? edgeType)
    {
        var sql = "SELECT DISTINCT ToSymbolId FROM Edges WHERE FromSymbolId IN @SymbolIds";
        var parameters = new DynamicParameters();
        parameters.Add("SymbolIds", symbolIds);

        if (edgeType.HasValue)
        {
            sql += " AND EdgeType = @EdgeType";
            parameters.Add("EdgeType", edgeType.Value.ToString());
        }

        var result = await _connection!.QueryAsync<long>(sql, parameters);
        return result.ToList();
    }

    private async Task<IReadOnlyList<SymbolRecord>> GetSymbolsByIdsAsync(IEnumerable<long> ids)
    {
        var result = await _connection!.QueryAsync<SymbolRecord>(
            "SELECT * FROM Symbols WHERE Id IN @Ids",
            new { Ids = ids });
        return result.ToList();
    }

    /// <summary>
    /// Gets edge statistics for a solution.
    /// </summary>
    public async Task<EdgeStats> GetEdgeStatsAsync(long solutionId)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var result = await _connection.QueryAsync<(string EdgeType, int Count)>(
            """
            SELECT e.EdgeType, COUNT(*) as Count
            FROM Edges e
            INNER JOIN Symbols s ON e.FromSymbolId = s.Id
            WHERE s.SolutionId = @SolutionId
            GROUP BY e.EdgeType
            """,
            new { SolutionId = solutionId });

        var stats = new EdgeStats();
        foreach (var (edgeType, count) in result)
        {
            switch (edgeType)
            {
                case "Calls": stats.Calls = count; break;
                case "Reads": stats.Reads = count; break;
                case "Writes": stats.Writes = count; break;
                case "Implements": stats.Implements = count; break;
                case "Overrides": stats.Overrides = count; break;
                case "Accesses": stats.Accesses = count; break;
            }
        }

        stats.Total = stats.Calls + stats.Reads + stats.Writes + stats.Implements + stats.Overrides + stats.Accesses;
        return stats;
    }
}

/// <summary>
/// Statistics about edges in the graph.
/// </summary>
public sealed class EdgeStats
{
    public int Total { get; set; }
    public int Calls { get; set; }
    public int Reads { get; set; }
    public int Writes { get; set; }
    public int Implements { get; set; }
    public int Overrides { get; set; }
    public int Accesses { get; set; }
}
