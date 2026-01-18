using Microsoft.Data.Sqlite;
using Dapper;

namespace RoslynMcpServer.Graph;

/// <summary>
/// Manages the SQLite graph database for a solution.
/// </summary>
public sealed partial class GraphDatabase : IDisposable
{
    private const string DbFolderName = ".roslyn-mcp";
    private const string DbFileName = "graph.db";

    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public string DatabasePath => _dbPath;

    /// <summary>
    /// Creates a GraphDatabase instance for the specified solution.
    /// </summary>
    /// <param name="solutionPath">Path to the .sln file</param>
    public GraphDatabase(string solutionPath)
    {
        var solutionDir = Path.GetDirectoryName(solutionPath)
            ?? throw new ArgumentException("Invalid solution path", nameof(solutionPath));

        var dbFolder = Path.Combine(solutionDir, DbFolderName);
        _dbPath = Path.Combine(dbFolder, DbFileName);
    }

    /// <summary>
    /// Creates a GraphDatabase instance with an explicit database path.
    /// Useful for testing with in-memory databases.
    /// </summary>
    internal GraphDatabase(string dbPath, bool isExplicitPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>
    /// Creates an in-memory database for testing.
    /// </summary>
    public static GraphDatabase CreateInMemory()
    {
        return new GraphDatabase(":memory:", isExplicitPath: true);
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
            CREATE TABLE IF NOT EXISTS Solutions (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                Path            TEXT UNIQUE NOT NULL,
                Name            TEXT NOT NULL,
                LastAnalyzed    TEXT,
                SolutionHash    TEXT
            );

            CREATE TABLE IF NOT EXISTS Symbols (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SolutionId      INTEGER NOT NULL,
                Kind            TEXT NOT NULL,
                Name            TEXT NOT NULL,
                QualifiedName   TEXT NOT NULL,
                FilePath        TEXT NOT NULL,
                Line            INTEGER NOT NULL,
                Column          INTEGER NOT NULL,
                BodyHash        TEXT,
                ContainingTypeId INTEGER,
                Status          TEXT NOT NULL DEFAULT 'Pending',
                LastAnalyzed    TEXT,
                FOREIGN KEY (SolutionId) REFERENCES Solutions(Id) ON DELETE CASCADE,
                FOREIGN KEY (ContainingTypeId) REFERENCES Symbols(Id)
            );

            CREATE TABLE IF NOT EXISTS Edges (
                FromSymbolId    INTEGER NOT NULL,
                ToSymbolId      INTEGER NOT NULL,
                EdgeType        TEXT NOT NULL,
                PRIMARY KEY (FromSymbolId, ToSymbolId, EdgeType),
                FOREIGN KEY (FromSymbolId) REFERENCES Symbols(Id) ON DELETE CASCADE,
                FOREIGN KEY (ToSymbolId) REFERENCES Symbols(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Files (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SolutionId      INTEGER NOT NULL,
                FilePath        TEXT NOT NULL,
                LastModified    TEXT NOT NULL,
                ContentHash     TEXT NOT NULL,
                LastAnalyzed    TEXT NOT NULL,
                FOREIGN KEY (SolutionId) REFERENCES Solutions(Id) ON DELETE CASCADE,
                UNIQUE (SolutionId, FilePath)
            );

            CREATE INDEX IF NOT EXISTS idx_symbols_solution ON Symbols(SolutionId);
            CREATE INDEX IF NOT EXISTS idx_symbols_qualified ON Symbols(QualifiedName);
            CREATE INDEX IF NOT EXISTS idx_symbols_file ON Symbols(FilePath);
            CREATE INDEX IF NOT EXISTS idx_symbols_status ON Symbols(Status);
            CREATE INDEX IF NOT EXISTS idx_edges_from ON Edges(FromSymbolId);
            CREATE INDEX IF NOT EXISTS idx_edges_to ON Edges(ToSymbolId);
            CREATE INDEX IF NOT EXISTS idx_files_solution ON Files(SolutionId);
            CREATE INDEX IF NOT EXISTS idx_files_path ON Files(FilePath);
            """;

        await _connection.ExecuteAsync(schema);
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }
}
