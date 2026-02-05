using Microsoft.Data.Sqlite;
using Dapper;

namespace RoslynMcpServer.Graph;

/// <summary>
/// Manages the SQLite knowledge database for storing code insights, gotchas, and patterns.
/// </summary>
public sealed partial class KnowledgeDatabase : IDisposable
{
    // Database stored in exe directory (typically .roslyn-mcp/)
    private const string DbFileName = "knowledge.db";

    private readonly string _dbPath;
    private SqliteConnection? _connection;
    private IEmbeddingProvider? _embeddingProvider;

    public string DatabasePath => _dbPath;

    /// <summary>
    /// Creates a KnowledgeDatabase instance for the specified solution.
    /// </summary>
    /// <param name="solutionPath">Path to the .sln file</param>
    public KnowledgeDatabase(string solutionPath)
    {
        var exeDir = AppContext.BaseDirectory;
        _dbPath = Path.Combine(exeDir, DbFileName);

    }

    /// <summary>
    /// Creates a KnowledgeDatabase instance with an explicit database path.
    /// Useful for testing with in-memory databases.
    /// </summary>
    internal KnowledgeDatabase(string dbPath, bool isExplicitPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>
    /// Sets the embedding provider for semantic search.
    /// If not set, only symbol links and FTS5 search will be available.
    /// </summary>
    public void SetEmbeddingProvider(IEmbeddingProvider provider)
    {
        _embeddingProvider = provider;
    }

    /// <summary>
    /// Opens the database connection and ensures schema exists.
    /// </summary>
    public async Task OpenAsync()
    {
        if (_connection != null) return;

        // Create directory if needed (not for in-memory)
        if (_dbPath != ":memory:")
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync();
        await InitializeSchemaAsync();
    }

    /// <summary>
    /// Checks if the database file exists (for non-in-memory databases).
    /// </summary>
    public bool Exists() => _dbPath == ":memory:" || File.Exists(_dbPath);

    private async Task InitializeSchemaAsync()
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        const string schema = """
            CREATE TABLE IF NOT EXISTS KnowledgeEntries (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                Category        TEXT NOT NULL,
                Title           TEXT NOT NULL,
                Content         TEXT NOT NULL,
                Embedding       BLOB,
                EmbeddingModel  TEXT,
                Confidence      REAL DEFAULT 1.0,
                CreatedAt       TEXT NOT NULL,
                UpdatedAt       TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS KnowledgeSymbolLinks (
                KnowledgeId     INTEGER NOT NULL,
                SymbolName      TEXT NOT NULL,
                LinkType        TEXT DEFAULT 'related',
                PRIMARY KEY (KnowledgeId, SymbolName),
                FOREIGN KEY (KnowledgeId) REFERENCES KnowledgeEntries(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS KnowledgeTags (
                KnowledgeId     INTEGER NOT NULL,
                Tag             TEXT NOT NULL,
                PRIMARY KEY (KnowledgeId, Tag),
                FOREIGN KEY (KnowledgeId) REFERENCES KnowledgeEntries(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_knowledge_category ON KnowledgeEntries(Category);
            CREATE INDEX IF NOT EXISTS idx_knowledge_created ON KnowledgeEntries(CreatedAt);
            CREATE INDEX IF NOT EXISTS idx_knowledge_confidence ON KnowledgeEntries(Confidence);
            CREATE INDEX IF NOT EXISTS idx_knowledge_links_symbol ON KnowledgeSymbolLinks(SymbolName);
            CREATE INDEX IF NOT EXISTS idx_knowledge_tags ON KnowledgeTags(Tag);
            """;

        await _connection.ExecuteAsync(schema);

        // Create FTS5 virtual table for full-text search
        const string ftsSchema = """
            CREATE VIRTUAL TABLE IF NOT EXISTS KnowledgeFts USING fts5(
                Title,
                Content,
                content=KnowledgeEntries,
                content_rowid=Id
            );

            -- Triggers to keep FTS in sync
            CREATE TRIGGER IF NOT EXISTS knowledge_ai AFTER INSERT ON KnowledgeEntries BEGIN
                INSERT INTO KnowledgeFts(rowid, Title, Content) VALUES (new.Id, new.Title, new.Content);
            END;

            CREATE TRIGGER IF NOT EXISTS knowledge_ad AFTER DELETE ON KnowledgeEntries BEGIN
                INSERT INTO KnowledgeFts(KnowledgeFts, rowid, Title, Content) VALUES('delete', old.Id, old.Title, old.Content);
            END;

            CREATE TRIGGER IF NOT EXISTS knowledge_au AFTER UPDATE ON KnowledgeEntries BEGIN
                INSERT INTO KnowledgeFts(KnowledgeFts, rowid, Title, Content) VALUES('delete', old.Id, old.Title, old.Content);
                INSERT INTO KnowledgeFts(rowid, Title, Content) VALUES (new.Id, new.Title, new.Content);
            END;
            """;

        await _connection.ExecuteAsync(ftsSchema);
    }

    internal SqliteConnection GetConnection()
    {
        return _connection ?? throw new InvalidOperationException("Database not open");
    }

    internal IEmbeddingProvider? GetEmbeddingProvider() => _embeddingProvider;

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }
}
