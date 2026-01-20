using Dapper;
using Microsoft.Data.Sqlite;

namespace RoslynMcpServer.Graph;

/// <summary>
/// Search operations for knowledge entries using three-layer approach:
/// 1. Symbol links (exact match)
/// 2. FTS5 full-text search (keyword)
/// 3. Vector similarity search (semantic)
/// </summary>
public partial class KnowledgeDatabase
{
    /// <summary>
    /// Searches for knowledge entries using all three layers.
    /// </summary>
    /// <param name="query">Search query text</param>
    /// <param name="symbols">Optional symbols to search for exact matches</param>
    /// <param name="limit">Maximum results to return</param>
    /// <returns>Ranked search results</returns>
    public async Task<List<KnowledgeSearchResult>> SearchAsync(
        string query,
        string[]? symbols = null,
        int limit = 10)
    {
        var results = new List<ScoredEntry>();

        // Layer 1: Exact symbol matches (highest priority, weight 1.0)
        if (symbols?.Length > 0)
        {
            var symbolResults = await SearchBySymbolsAsync(symbols);
            results.AddRange(symbolResults.Select(e => new ScoredEntry(e, 1.0, "symbol")));
        }

        // Layer 2: Full-text search (weight 0.7)
        if (!string.IsNullOrWhiteSpace(query))
        {
            var ftsResults = await SearchByFtsAsync(query);
            results.AddRange(ftsResults.Select(e => new ScoredEntry(e.Entry, e.Score * 0.7, "fts")));
        }

        // Layer 3: Vector similarity search (weight 0.8)
        if (!string.IsNullOrWhiteSpace(query) && _embeddingProvider != null)
        {
            var vectorResults = await SearchByVectorAsync(query);
            results.AddRange(vectorResults.Select(e => new ScoredEntry(e.Entry, e.Score * 0.8, "vector")));
        }

        // Merge, dedupe, and rank by combined score
        var merged = results
            .GroupBy(r => r.Entry.Id)
            .Select(g => new KnowledgeSearchResult
            {
                Entry = g.First().Entry,
                Score = g.Sum(r => r.Score),
                MatchSource = string.Join("+", g.Select(r => r.Source).Distinct())
            })
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();

        // Load related data for results
        foreach (var result in merged)
        {
            await LoadRelatedDataAsync(result.Entry);
        }

        return merged;
    }

    /// <summary>
    /// Searches for entries linked to any of the specified symbols.
    /// </summary>
    private async Task<List<KnowledgeEntry>> SearchBySymbolsAsync(string[] symbols)
    {
        var conn = GetConnection();

        // Match exact symbol names or namespace prefixes
        var conditions = symbols.Select((_, i) => $"l.SymbolName = @Symbol{i} OR l.SymbolName LIKE @SymbolPrefix{i}");
        var sql = $"""
            SELECT DISTINCT e.* FROM KnowledgeEntries e
            JOIN KnowledgeSymbolLinks l ON e.Id = l.KnowledgeId
            WHERE {string.Join(" OR ", conditions)}
            ORDER BY e.Confidence DESC
            """;

        var parameters = new DynamicParameters();
        for (int i = 0; i < symbols.Length; i++)
        {
            parameters.Add($"Symbol{i}", symbols[i]);
            parameters.Add($"SymbolPrefix{i}", symbols[i] + ".%");
        }

        return (await conn.QueryAsync<KnowledgeEntry>(sql, parameters)).ToList();
    }

    /// <summary>
    /// Searches using SQLite FTS5 full-text search.
    /// </summary>
    private async Task<List<(KnowledgeEntry Entry, double Score)>> SearchByFtsAsync(string query)
    {
        var conn = GetConnection();

        // Escape special FTS5 characters and prepare query
        var ftsQuery = PrepareFtsQuery(query);

        const string sql = """
            SELECT e.*, bm25(KnowledgeFts) as rank
            FROM KnowledgeFts fts
            JOIN KnowledgeEntries e ON fts.rowid = e.Id
            WHERE KnowledgeFts MATCH @Query
            ORDER BY rank
            LIMIT 20
            """;

        try
        {
            var results = await conn.QueryAsync<KnowledgeEntry, double, (KnowledgeEntry, double)>(
                sql,
                (entry, rank) => (entry, -rank), // BM25 returns negative scores, lower is better
                new { Query = ftsQuery },
                splitOn: "rank");

            // Normalize scores to 0-1 range
            var list = results.ToList();
            if (list.Count == 0) return [];

            var maxScore = list.Max(r => r.Item2);
            var minScore = list.Min(r => r.Item2);
            var range = maxScore - minScore;

            return list.Select(r => (
                r.Item1,
                range > 0 ? (r.Item2 - minScore) / range : 1.0
            )).ToList();
        }
        catch (SqliteException)
        {
            // FTS query syntax error - return empty results
            return [];
        }
    }

    /// <summary>
    /// Searches using vector similarity (cosine similarity).
    /// </summary>
    private async Task<List<(KnowledgeEntry Entry, double Score)>> SearchByVectorAsync(string query)
    {
        if (_embeddingProvider == null) return [];

        var conn = GetConnection();

        // Generate query embedding
        var queryVector = await _embeddingProvider.EmbedAsync(query);

        // Get all entries with embeddings
        const string sql = "SELECT * FROM KnowledgeEntries WHERE Embedding IS NOT NULL";
        var entries = (await conn.QueryAsync<KnowledgeEntry>(sql)).ToList();

        if (entries.Count == 0) return [];

        // Calculate similarities
        var scored = entries
            .Select(e => (
                Entry: e,
                Score: (double)_embeddingProvider.Similarity(queryVector, BytesToFloatArray(e.Embedding!))
            ))
            .Where(r => r.Score > 0.3) // Threshold for relevance
            .OrderByDescending(r => r.Score)
            .Take(20)
            .ToList();

        return scored;
    }

    /// <summary>
    /// Prepares a query string for FTS5 search.
    /// </summary>
    private static string PrepareFtsQuery(string query)
    {
        // Remove special FTS5 operators for safety
        var cleaned = query
            .Replace("\"", " ")
            .Replace("*", " ")
            .Replace("(", " ")
            .Replace(")", " ")
            .Replace(":", " ")
            .Replace("^", " ")
            .Trim();

        // Split into words and join with OR for more flexible matching
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0) return "";
        if (words.Length == 1) return $"{words[0]}*"; // Prefix match for single word

        // Use OR between words for flexible matching
        return string.Join(" OR ", words.Select(w => $"{w}*"));
    }

    /// <summary>
    /// Gets contextual knowledge for a task description.
    /// Useful at session start to load relevant knowledge.
    /// </summary>
    public async Task<List<KnowledgeSearchResult>> GetContextAsync(
        string taskDescription,
        string[]? symbols = null,
        int limit = 10)
    {
        return await SearchAsync(taskDescription, symbols, limit);
    }

    private record ScoredEntry(KnowledgeEntry Entry, double Score, string Source);
}
