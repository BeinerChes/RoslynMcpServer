using Dapper;

namespace RoslynMcpServer.Graph;

/// <summary>
/// Solution-related database operations.
/// </summary>
public sealed partial class GraphDatabase
{
    /// <summary>
    /// Gets or creates a solution record.
    /// </summary>
    public async Task<SolutionRecord> GetOrCreateSolutionAsync(string solutionPath)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var name = Path.GetFileNameWithoutExtension(solutionPath);
        var normalizedPath = Path.GetFullPath(solutionPath);

        var existing = await _connection.QuerySingleOrDefaultAsync<SolutionRecord>(
            "SELECT * FROM Solutions WHERE Path = @Path",
            new { Path = normalizedPath });

        if (existing != null) return existing;

        var id = await _connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO Solutions (Path, Name) VALUES (@Path, @Name);
            SELECT last_insert_rowid();
            """,
            new { Path = normalizedPath, Name = name });

        return new SolutionRecord
        {
            Id = id,
            Path = normalizedPath,
            Name = name
        };
    }

    /// <summary>
    /// Gets a solution by path.
    /// </summary>
    public async Task<SolutionRecord?> GetSolutionAsync(string solutionPath)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var normalizedPath = Path.GetFullPath(solutionPath);
        return await _connection.QuerySingleOrDefaultAsync<SolutionRecord>(
            "SELECT * FROM Solutions WHERE Path = @Path",
            new { Path = normalizedPath });
    }

    /// <summary>
    /// Gets all tracked solutions.
    /// </summary>
    public async Task<IReadOnlyList<SolutionRecord>> GetAllSolutionsAsync()
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var result = await _connection.QueryAsync<SolutionRecord>("SELECT * FROM Solutions");
        return result.ToList();
    }

    /// <summary>
    /// Updates the solution's last analyzed time and hash.
    /// </summary>
    public async Task UpdateSolutionAnalyzedAsync(long solutionId, string? hash = null)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        await _connection.ExecuteAsync(
            """
            UPDATE Solutions
            SET LastAnalyzed = @LastAnalyzed, SolutionHash = @Hash
            WHERE Id = @Id
            """,
            new { Id = solutionId, LastAnalyzed = DateTime.UtcNow.ToString("o"), Hash = hash });
    }

    /// <summary>
    /// Deletes a solution and all its symbols/edges (cascade).
    /// </summary>
    public async Task DeleteSolutionAsync(long solutionId)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        await _connection.ExecuteAsync(
            "DELETE FROM Solutions WHERE Id = @Id",
            new { Id = solutionId });
    }
}
