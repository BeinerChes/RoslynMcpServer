using Dapper;

namespace RoslynMcpServer.Graph;

/// <summary>
/// Symbol-related database operations.
/// </summary>
public sealed partial class GraphDatabase
{
    /// <summary>
    /// Inserts a new symbol into the database.
    /// </summary>
    public async Task<long> InsertSymbolAsync(SymbolRecord symbol)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var id = await _connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO Symbols (SolutionId, Kind, Name, QualifiedName, FilePath, Line, Column, BodyHash, ContainingTypeId, Status, LastAnalyzed)
            VALUES (@SolutionId, @Kind, @Name, @QualifiedName, @FilePath, @Line, @Column, @BodyHash, @ContainingTypeId, @Status, @LastAnalyzed);
            SELECT last_insert_rowid();
            """,
            new
            {
                symbol.SolutionId,
                Kind = symbol.Kind.ToString(),
                symbol.Name,
                symbol.QualifiedName,
                symbol.FilePath,
                symbol.Line,
                symbol.Column,
                symbol.BodyHash,
                symbol.ContainingTypeId,
                Status = symbol.Status.ToString(),
                LastAnalyzed = symbol.LastAnalyzed?.ToString("o")
            });

        symbol.Id = id;
        return id;
    }

    /// <summary>
    /// Gets a symbol by its qualified name (exact match).
    /// </summary>
    public async Task<SymbolRecord?> GetSymbolByQualifiedNameAsync(long solutionId, string qualifiedName)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        return await _connection.QuerySingleOrDefaultAsync<SymbolRecord>(
            """
            SELECT Id, SolutionId, Kind, Name, QualifiedName, FilePath, Line, Column, BodyHash, ContainingTypeId, Status, LastAnalyzed
            FROM Symbols
            WHERE SolutionId = @SolutionId AND QualifiedName = @QualifiedName
            """,
            new { SolutionId = solutionId, QualifiedName = qualifiedName });
    }

    /// <summary>
    /// Finds a symbol by partial name with smart matching.
    /// Supports: exact match, method name only, Type.Method, or partial namespace.
    /// Returns error with candidates if multiple matches found.
    /// Issue: #33
    /// </summary>
    public async Task<SymbolSearchResult> FindSymbolAsync(long solutionId, string searchName)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        // 1. Try exact match first
        var exact = await GetSymbolByQualifiedNameAsync(solutionId, searchName);
        if (exact != null)
        {
            return new SymbolSearchResult { Symbol = exact };
        }

        // 2. Try partial matching - search for symbols ending with the search term
        // This handles: "MethodName", "Type.MethodName", "Namespace.Type.MethodName"
        var candidates = await _connection.QueryAsync<SymbolRecord>(
            """
            SELECT Id, SolutionId, Kind, Name, QualifiedName, FilePath, Line, Column, BodyHash, ContainingTypeId, Status, LastAnalyzed
            FROM Symbols
            WHERE SolutionId = @SolutionId
              AND (QualifiedName LIKE '%.' || @SearchName || '(%'
                   OR QualifiedName LIKE '%.' || @SearchName
                   OR Name = @SearchName)
            """,
            new { SolutionId = solutionId, SearchName = searchName });

        var matches = candidates.ToList();

        if (matches.Count == 0)
        {
            return new SymbolSearchResult
            {
                Error = $"Symbol '{searchName}' not found in graph."
            };
        }

        if (matches.Count == 1)
        {
            return new SymbolSearchResult { Symbol = matches[0] };
        }

        // Multiple matches - return error with candidates
        return new SymbolSearchResult
        {
            Error = $"Multiple symbols match '{searchName}'. Please be more specific.",
            Candidates = matches
        };
    }

    /// <summary>
    /// Gets all symbols for a solution.
    /// </summary>
    public async Task<IReadOnlyList<SymbolRecord>> GetSymbolsAsync(long solutionId, SymbolKind? kind = null, SymbolStatus? status = null)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var sql = "SELECT * FROM Symbols WHERE SolutionId = @SolutionId";
        var parameters = new DynamicParameters();
        parameters.Add("SolutionId", solutionId);

        if (kind.HasValue)
        {
            sql += " AND Kind = @Kind";
            parameters.Add("Kind", kind.Value.ToString());
        }

        if (status.HasValue)
        {
            sql += " AND Status = @Status";
            parameters.Add("Status", status.Value.ToString());
        }

        var result = await _connection.QueryAsync<SymbolRecord>(sql, parameters);
        return result.ToList();
    }

    /// <summary>
    /// Updates a symbol's status and hash.
    /// </summary>
    public async Task UpdateSymbolStatusAsync(long symbolId, SymbolStatus status, string? bodyHash = null)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        await _connection.ExecuteAsync(
            """
            UPDATE Symbols
            SET Status = @Status, BodyHash = @BodyHash, LastAnalyzed = @LastAnalyzed
            WHERE Id = @Id
            """,
            new
            {
                Id = symbolId,
                Status = status.ToString(),
                BodyHash = bodyHash,
                LastAnalyzed = DateTime.UtcNow.ToString("o")
            });
    }

    /// <summary>
    /// Marks symbols as dirty for a specific file (used after file changes).
    /// </summary>
    public async Task MarkSymbolsDirtyByFileAsync(long solutionId, string filePath)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        await _connection.ExecuteAsync(
            """
            UPDATE Symbols
            SET Status = 'Dirty'
            WHERE SolutionId = @SolutionId AND FilePath = @FilePath
            """,
            new { SolutionId = solutionId, FilePath = filePath });
    }

    /// <summary>
    /// Deletes all symbols for a file (used before re-analyzing).
    /// </summary>
    public async Task DeleteSymbolsByFileAsync(long solutionId, string filePath)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        await _connection.ExecuteAsync(
            "DELETE FROM Symbols WHERE SolutionId = @SolutionId AND FilePath = @FilePath",
            new { SolutionId = solutionId, FilePath = filePath });
    }

    /// <summary>
    /// Gets statistics about symbols for a solution.
    /// </summary>
    public async Task<SymbolStats> GetSymbolStatsAsync(long solutionId)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var result = await _connection.QueryAsync<(string Status, int Count)>(
            """
            SELECT Status, COUNT(*) as Count
            FROM Symbols
            WHERE SolutionId = @SolutionId
            GROUP BY Status
            """,
            new { SolutionId = solutionId });

        var stats = new SymbolStats();
        foreach (var (status, count) in result)
        {
            switch (status)
            {
                case "Analyzed": stats.Analyzed = count; break;
                case "Pending": stats.Pending = count; break;
                case "Dirty": stats.Dirty = count; break;
            }
        }

        stats.Total = stats.Analyzed + stats.Pending + stats.Dirty;
        return stats;
    }
}

/// <summary>
/// Statistics about symbols in the graph.
/// </summary>
public sealed class SymbolStats
{
    public int Total { get; set; }
    public int Analyzed { get; set; }
    public int Pending { get; set; }
    public int Dirty { get; set; }
}

/// <summary>
/// Result of a symbol search operation.
/// Issue: #33
/// </summary>
public sealed class SymbolSearchResult
{
    /// <summary>
    /// The found symbol (null if not found or ambiguous).
    /// </summary>
    public SymbolRecord? Symbol { get; set; }

    /// <summary>
    /// Error message if symbol not found or ambiguous.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// List of candidates when multiple symbols match.
    /// </summary>
    public List<SymbolRecord>? Candidates { get; set; }
}
