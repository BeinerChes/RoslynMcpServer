using Dapper;

namespace RoslynMcpServer.Graph;

/// <summary>
/// CRUD operations for knowledge entries.
/// </summary>
public partial class KnowledgeDatabase
{
    /// <summary>
    /// Adds a new knowledge entry with optional symbol links and tags.
    /// </summary>
    public async Task<KnowledgeEntry> AddEntryAsync(AddKnowledgeInput input)
    {
        var conn = GetConnection();
        var now = DateTime.UtcNow.ToString("o");

        // Validate category
        if (!KnowledgeCategories.IsValid(input.Category))
        {
            throw new ArgumentException(
                $"Invalid category '{input.Category}'. Valid categories: {string.Join(", ", KnowledgeCategories.All)}",
                nameof(input));
        }

        // Generate embedding if provider is available
        byte[]? embedding = null;
        string? embeddingModel = null;

        if (_embeddingProvider != null)
        {
            var textToEmbed = $"{input.Title}. {input.Content}";
            var vector = await _embeddingProvider.EmbedAsync(textToEmbed);
            embedding = FloatArrayToBytes(vector);
            embeddingModel = _embeddingProvider.ModelName;
        }

        // Insert main entry
        const string insertSql = """
            INSERT INTO KnowledgeEntries (Category, Title, Content, Embedding, EmbeddingModel, Confidence, CreatedAt, UpdatedAt)
            VALUES (@Category, @Title, @Content, @Embedding, @EmbeddingModel, @Confidence, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
            """;

        var id = await conn.ExecuteScalarAsync<long>(insertSql, new
        {
            input.Category,
            input.Title,
            input.Content,
            Embedding = embedding,
            EmbeddingModel = embeddingModel,
            input.Confidence,
            CreatedAt = now,
            UpdatedAt = now
        });

        // Insert symbol links
        if (input.SymbolLinks?.Count > 0)
        {
            const string linkSql = "INSERT INTO KnowledgeSymbolLinks (KnowledgeId, SymbolName, LinkType) VALUES (@KnowledgeId, @SymbolName, @LinkType)";
            foreach (var symbol in input.SymbolLinks)
            {
                await conn.ExecuteAsync(linkSql, new { KnowledgeId = id, SymbolName = symbol, LinkType = KnowledgeLinkTypes.About });
            }
        }

        // Insert tags
        if (input.Tags?.Count > 0)
        {
            const string tagSql = "INSERT INTO KnowledgeTags (KnowledgeId, Tag) VALUES (@KnowledgeId, @Tag)";
            foreach (var tag in input.Tags)
            {
                await conn.ExecuteAsync(tagSql, new { KnowledgeId = id, Tag = tag.ToLowerInvariant() });
            }
        }

        return await GetEntryByIdAsync(id) ?? throw new InvalidOperationException("Failed to retrieve created entry");
    }

    /// <summary>
    /// Gets a knowledge entry by ID with its symbol links and tags.
    /// </summary>
    public async Task<KnowledgeEntry?> GetEntryByIdAsync(long id)
    {
        var conn = GetConnection();

        const string sql = "SELECT * FROM KnowledgeEntries WHERE Id = @Id";
        var entry = await conn.QuerySingleOrDefaultAsync<KnowledgeEntry>(sql, new { Id = id });

        if (entry != null)
        {
            await LoadRelatedDataAsync(entry);
        }

        return entry;
    }

    /// <summary>
    /// Lists all knowledge entries with optional filtering.
    /// </summary>
    public async Task<List<KnowledgeEntry>> ListEntriesAsync(
        string? category = null,
        string? tag = null,
        int limit = 100,
        int offset = 0)
    {
        var conn = GetConnection();

        var sql = "SELECT DISTINCT e.* FROM KnowledgeEntries e";
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(tag))
        {
            sql += " JOIN KnowledgeTags t ON e.Id = t.KnowledgeId";
            conditions.Add("t.Tag = @Tag");
            parameters.Add("Tag", tag.ToLowerInvariant());
        }

        if (!string.IsNullOrEmpty(category))
        {
            conditions.Add("e.Category = @Category");
            parameters.Add("Category", category);
        }

        if (conditions.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", conditions);
        }

        sql += " ORDER BY e.UpdatedAt DESC LIMIT @Limit OFFSET @Offset";
        parameters.Add("Limit", limit);
        parameters.Add("Offset", offset);

        var entries = (await conn.QueryAsync<KnowledgeEntry>(sql, parameters)).ToList();

        foreach (var entry in entries)
        {
            await LoadRelatedDataAsync(entry);
        }

        return entries;
    }

    /// <summary>
    /// Updates an existing knowledge entry.
    /// </summary>
    public async Task<KnowledgeEntry?> UpdateEntryAsync(
        long id,
        string? title = null,
        string? content = null,
        string? category = null,
        double? confidence = null,
        List<string>? symbolLinks = null,
        List<string>? tags = null)
    {
        var conn = GetConnection();
        var existing = await GetEntryByIdAsync(id);
        if (existing == null) return null;

        var now = DateTime.UtcNow.ToString("o");
        var updates = new List<string> { "UpdatedAt = @UpdatedAt" };
        var parameters = new DynamicParameters();
        parameters.Add("Id", id);
        parameters.Add("UpdatedAt", now);

        if (title != null)
        {
            updates.Add("Title = @Title");
            parameters.Add("Title", title);
        }

        if (content != null)
        {
            updates.Add("Content = @Content");
            parameters.Add("Content", content);
        }

        if (category != null)
        {
            if (!KnowledgeCategories.IsValid(category))
            {
                throw new ArgumentException($"Invalid category '{category}'", nameof(category));
            }
            updates.Add("Category = @Category");
            parameters.Add("Category", category);
        }

        if (confidence != null)
        {
            updates.Add("Confidence = @Confidence");
            parameters.Add("Confidence", confidence);
        }

        // Re-generate embedding if title or content changed
        if ((title != null || content != null) && _embeddingProvider != null)
        {
            var newTitle = title ?? existing.Title;
            var newContent = content ?? existing.Content;
            var textToEmbed = $"{newTitle}. {newContent}";
            var vector = await _embeddingProvider.EmbedAsync(textToEmbed);

            updates.Add("Embedding = @Embedding");
            updates.Add("EmbeddingModel = @EmbeddingModel");
            parameters.Add("Embedding", FloatArrayToBytes(vector));
            parameters.Add("EmbeddingModel", _embeddingProvider.ModelName);
        }

        var sql = $"UPDATE KnowledgeEntries SET {string.Join(", ", updates)} WHERE Id = @Id";
        await conn.ExecuteAsync(sql, parameters);

        // Update symbol links if provided
        if (symbolLinks != null)
        {
            await conn.ExecuteAsync("DELETE FROM KnowledgeSymbolLinks WHERE KnowledgeId = @Id", new { Id = id });
            const string linkSql = "INSERT INTO KnowledgeSymbolLinks (KnowledgeId, SymbolName, LinkType) VALUES (@KnowledgeId, @SymbolName, @LinkType)";
            foreach (var symbol in symbolLinks)
            {
                await conn.ExecuteAsync(linkSql, new { KnowledgeId = id, SymbolName = symbol, LinkType = KnowledgeLinkTypes.About });
            }
        }

        // Update tags if provided
        if (tags != null)
        {
            await conn.ExecuteAsync("DELETE FROM KnowledgeTags WHERE KnowledgeId = @Id", new { Id = id });
            const string tagSql = "INSERT INTO KnowledgeTags (KnowledgeId, Tag) VALUES (@KnowledgeId, @Tag)";
            foreach (var tag in tags)
            {
                await conn.ExecuteAsync(tagSql, new { KnowledgeId = id, Tag = tag.ToLowerInvariant() });
            }
        }

        return await GetEntryByIdAsync(id);
    }

    /// <summary>
    /// Deletes a knowledge entry.
    /// </summary>
    public async Task<bool> DeleteEntryAsync(long id)
    {
        var conn = GetConnection();
        var affected = await conn.ExecuteAsync("DELETE FROM KnowledgeEntries WHERE Id = @Id", new { Id = id });
        return affected > 0;
    }

    /// <summary>
    /// Gets all entries linked to a specific symbol.
    /// </summary>
    public async Task<List<KnowledgeEntry>> GetEntriesForSymbolAsync(string symbolName)
    {
        var conn = GetConnection();

        const string sql = """
            SELECT e.* FROM KnowledgeEntries e
            JOIN KnowledgeSymbolLinks l ON e.Id = l.KnowledgeId
            WHERE l.SymbolName = @SymbolName
            ORDER BY e.Confidence DESC, e.UpdatedAt DESC
            """;

        var entries = (await conn.QueryAsync<KnowledgeEntry>(sql, new { SymbolName = symbolName })).ToList();

        foreach (var entry in entries)
        {
            await LoadRelatedDataAsync(entry);
        }

        return entries;
    }

    /// <summary>
    /// Gets the count of knowledge entries.
    /// </summary>
    public async Task<int> GetEntryCountAsync()
    {
        var conn = GetConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM KnowledgeEntries");
    }

    private async Task LoadRelatedDataAsync(KnowledgeEntry entry)
    {
        var conn = GetConnection();

        // Load symbol links
        const string linksSql = "SELECT SymbolName FROM KnowledgeSymbolLinks WHERE KnowledgeId = @Id";
        entry.SymbolLinks = (await conn.QueryAsync<string>(linksSql, new { Id = entry.Id })).ToList();

        // Load tags
        const string tagsSql = "SELECT Tag FROM KnowledgeTags WHERE KnowledgeId = @Id";
        entry.Tags = (await conn.QueryAsync<string>(tagsSql, new { Id = entry.Id })).ToList();
    }

    private static byte[] FloatArrayToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    internal static float[] BytesToFloatArray(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }
}
