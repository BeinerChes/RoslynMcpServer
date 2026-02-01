using Microsoft.Data.Sqlite;
using Dapper;

namespace RoslynMcpServer.Graph;

/// <summary>
/// Manages the SQLite graph database for a solution.
/// </summary>
public sealed partial class GraphDatabase : IDisposable
{
    // Database stored in exe directory (typically .roslyn-mcp/)
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
        var exeDir = AppContext.BaseDirectory;
        _dbPath = Path.Combine(exeDir, DbFileName);

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
                FileHash        TEXT,
                Status          TEXT NOT NULL DEFAULT 'Analyzed',
                FOREIGN KEY (SolutionId) REFERENCES Solutions(Id) ON DELETE CASCADE
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
            CREATE INDEX IF NOT EXISTS idx_symbols_filehash ON Symbols(FileHash);
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


    public async Task<IReadOnlyList<SymbolRecord>> GetSymbolsWithNoCallersAsync(
            long solutionId, SymbolKind[]? kinds = null)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var sql = """
            SELECT s.* FROM Symbols s
            WHERE s.SolutionId = @SolutionId
              AND s.FilePath IS NOT NULL
              AND s.FilePath != 'external'
              AND NOT EXISTS (
                SELECT 1 FROM Edges e WHERE e.ToSymbolId = s.Id
              )
            """;

        var parameters = new DynamicParameters();
        parameters.Add("SolutionId", solutionId);

        if (kinds != null && kinds.Length > 0)
        {
            var kindStrings = kinds.Select(k => k.ToString()).ToArray();
            sql += " AND s.Kind IN @Kinds";
            parameters.Add("Kinds", kindStrings);
        }

        var result = await _connection.QueryAsync<SymbolRecord>(sql, parameters);
        return result.ToList();
    }


    public async Task<IReadOnlyList<SymbolRecord>> GetSymbolsByFileAsync(long solutionId, string filePath)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var result = await _connection.QueryAsync<SymbolRecord>(
            "SELECT * FROM Symbols WHERE SolutionId = @SolutionId AND FilePath = @FilePath",
            new { SolutionId = solutionId, FilePath = filePath });
        return result.ToList();
    }


    public async Task UpdateSymbolsFileHashAsync(long solutionId, string filePath, string fileHash)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        await _connection.ExecuteAsync(
            "UPDATE Symbols SET FileHash = @FileHash WHERE SolutionId = @SolutionId AND FilePath = @FilePath",
            new { SolutionId = solutionId, FilePath = filePath, FileHash = fileHash });
    }


    public async Task<IReadOnlyList<string>> GetStaleSymbolFilesAsync(long solutionId, IDictionary<string, string> currentFileHashes)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        // Get files with non-null FileHash only
        var storedHashes = await _connection.QueryAsync<(string FilePath, string FileHash)>(
            """
            SELECT DISTINCT FilePath, FileHash
            FROM Symbols
            WHERE SolutionId = @SolutionId 
              AND FilePath != 'external'
              AND FileHash IS NOT NULL
            """,
            new { SolutionId = solutionId });

        var staleFiles = new List<string>();
        var debugLog = new System.Text.StringBuilder();
        debugLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] Checking {storedHashes.Count()} stored hashes against {currentFileHashes.Count} current files");

        foreach (var (filePath, storedHash) in storedHashes)
        {
            if (!currentFileHashes.TryGetValue(filePath, out var currentHash))
            {
                debugLog.AppendLine($"  NOT FOUND: '{filePath}'");
                staleFiles.Add(filePath);
            }
            else if (storedHash != currentHash)
            {
                debugLog.AppendLine($"  STALE: '{filePath}' stored={storedHash} current={currentHash}");
                staleFiles.Add(filePath);
            }
        }

        debugLog.AppendLine($"  Total stale: {staleFiles.Count}");
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "debug.log"), debugLog.ToString());
        return staleFiles;
    }


    public async Task<IReadOnlyList<string>> GetTransitiveCallerFilesAsync(IEnumerable<long> symbolIds)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var visitedSymbolIds = new HashSet<long>(symbolIds);
        var callerFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentLevel = symbolIds.ToList();

        // Traverse callers recursively until no new callers are found
        while (currentLevel.Count > 0)
        {
            if (currentLevel.Count == 0) break;

            // Get all callers of current level
            var callerIds = await _connection.QueryAsync<long>(
                "SELECT DISTINCT FromSymbolId FROM Edges WHERE ToSymbolId IN @SymbolIds",
                new { SymbolIds = currentLevel });

            var newCallerIds = callerIds.Where(id => !visitedSymbolIds.Contains(id)).ToList();
            if (newCallerIds.Count == 0) break;

            // Get file paths for new callers
            var callerSymbols = await _connection.QueryAsync<string>(
                "SELECT DISTINCT FilePath FROM Symbols WHERE Id IN @Ids AND FilePath != 'external'",
                new { Ids = newCallerIds });

            foreach (var filePath in callerSymbols)
            {
                callerFiles.Add(filePath);
            }

            // Mark as visited and prepare next level
            foreach (var id in newCallerIds)
            {
                visitedSymbolIds.Add(id);
            }
            currentLevel = newCallerIds;
        }

        return callerFiles.ToList();
    }


    public async Task<IReadOnlyList<long>> GetSymbolIdsForFilesAsync(long solutionId, IEnumerable<string> filePaths)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var fileList = filePaths.ToList();
        if (fileList.Count == 0) return Array.Empty<long>();

        var result = await _connection.QueryAsync<long>(
            "SELECT Id FROM Symbols WHERE SolutionId = @SolutionId AND FilePath IN @FilePaths",
            new { SolutionId = solutionId, FilePaths = fileList });
        return result.ToList();
    }
}
