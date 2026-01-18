using Dapper;
using System.Security.Cryptography;
using System.Text;

namespace RoslynMcpServer.Graph;

/// <summary>
/// File tracking operations for change detection.
/// </summary>
public sealed partial class GraphDatabase
{
    /// <summary>
    /// Gets a tracked file record by path.
    /// </summary>
    public async Task<FileRecord?> GetFileAsync(long solutionId, string filePath)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        return await _connection.QuerySingleOrDefaultAsync<FileRecord>(
            "SELECT * FROM Files WHERE SolutionId = @SolutionId AND FilePath = @FilePath",
            new { SolutionId = solutionId, FilePath = filePath });
    }

    /// <summary>
    /// Inserts or updates a file tracking record.
    /// </summary>
    public async Task UpsertFileAsync(FileRecord file)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        await _connection.ExecuteAsync(
            """
            INSERT INTO Files (SolutionId, FilePath, LastModified, ContentHash, LastAnalyzed)
            VALUES (@SolutionId, @FilePath, @LastModified, @ContentHash, @LastAnalyzed)
            ON CONFLICT (SolutionId, FilePath) DO UPDATE SET
                LastModified = @LastModified,
                ContentHash = @ContentHash,
                LastAnalyzed = @LastAnalyzed
            """,
            new
            {
                file.SolutionId,
                file.FilePath,
                LastModified = file.LastModified.ToString("o"),
                file.ContentHash,
                LastAnalyzed = file.LastAnalyzed.ToString("o")
            });
    }

    /// <summary>
    /// Checks if a file has changed since last analysis.
    /// Returns true if file needs re-analysis.
    /// </summary>
    public async Task<bool> IsFileStaleAsync(long solutionId, string filePath)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var tracked = await GetFileAsync(solutionId, filePath);
        if (tracked == null) return true; // New file, needs analysis

        if (!File.Exists(filePath)) return false; // File deleted, skip

        var fileInfo = new FileInfo(filePath);

        // Quick check: if timestamp unchanged, file is fresh
        if (fileInfo.LastWriteTimeUtc == tracked.LastModified)
            return false;

        // Timestamp changed, verify with hash
        var currentHash = ComputeFileHash(filePath);
        return currentHash != tracked.ContentHash;
    }

    /// <summary>
    /// Gets all stale files for a solution.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetStaleFilesAsync(long solutionId, IEnumerable<string> filePaths)
    {
        var staleFiles = new List<string>();

        foreach (var filePath in filePaths)
        {
            if (await IsFileStaleAsync(solutionId, filePath))
            {
                staleFiles.Add(filePath);
            }
        }

        return staleFiles;
    }

    /// <summary>
    /// Gets all tracked files for a solution.
    /// </summary>
    public async Task<IReadOnlyList<FileRecord>> GetAllFilesAsync(long solutionId)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var result = await _connection.QueryAsync<FileRecord>(
            "SELECT * FROM Files WHERE SolutionId = @SolutionId",
            new { SolutionId = solutionId });
        return result.ToList();
    }

    /// <summary>
    /// Removes file tracking records for files that no longer exist.
    /// </summary>
    public async Task<int> CleanupDeletedFilesAsync(long solutionId)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var files = await GetAllFilesAsync(solutionId);
        var deletedFiles = files.Where(f => !File.Exists(f.FilePath)).ToList();

        if (deletedFiles.Count == 0) return 0;

        var deletedPaths = deletedFiles.Select(f => f.FilePath).ToList();
        await _connection.ExecuteAsync(
            "DELETE FROM Files WHERE SolutionId = @SolutionId AND FilePath IN @Paths",
            new { SolutionId = solutionId, Paths = deletedPaths });

        return deletedFiles.Count;
    }

    /// <summary>
    /// Removes symbols from deleted files.
    /// </summary>
    public async Task<int> CleanupSymbolsFromDeletedFilesAsync(long solutionId)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        // Get all unique file paths from symbols
        var symbolFilePaths = await _connection.QueryAsync<string>(
            "SELECT DISTINCT FilePath FROM Symbols WHERE SolutionId = @SolutionId AND FilePath != 'external'",
            new { SolutionId = solutionId });

        var deletedPaths = symbolFilePaths.Where(p => !File.Exists(p)).ToList();
        if (deletedPaths.Count == 0) return 0;

        var deletedCount = await _connection.ExecuteAsync(
            "DELETE FROM Symbols WHERE SolutionId = @SolutionId AND FilePath IN @Paths",
            new { SolutionId = solutionId, Paths = deletedPaths });

        return deletedCount;
    }

    /// <summary>
    /// Checks if a file has any symbols in the graph.
    /// Issue: #35
    /// </summary>
    public async Task<bool> IsFileInGraphAsync(long solutionId, string filePath)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var count = await _connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Symbols WHERE SolutionId = @SolutionId AND FilePath = @FilePath",
            new { SolutionId = solutionId, FilePath = filePath });

        return count > 0;
    }

    /// <summary>
    /// Updates the content hash for a file. Used for testing.
    /// Issue: #35
    /// </summary>
    public async Task UpdateFileHashAsync(long solutionId, string filePath, string contentHash)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var existing = await GetFileAsync(solutionId, filePath);
        if (existing == null)
        {
            await _connection.ExecuteAsync(
                """
                INSERT INTO Files (SolutionId, FilePath, LastModified, ContentHash, LastAnalyzed)
                VALUES (@SolutionId, @FilePath, @LastModified, @ContentHash, @LastAnalyzed)
                """,
                new
                {
                    SolutionId = solutionId,
                    FilePath = filePath,
                    LastModified = DateTime.UtcNow.ToString("o"),
                    ContentHash = contentHash,
                    LastAnalyzed = DateTime.UtcNow.ToString("o")
                });
        }
        else
        {
            await _connection.ExecuteAsync(
                "UPDATE Files SET ContentHash = @ContentHash WHERE SolutionId = @SolutionId AND FilePath = @FilePath",
                new { SolutionId = solutionId, FilePath = filePath, ContentHash = contentHash });
        }
    }

    /// <summary>
    /// Gets files that need analysis: either not in graph or stale (hash mismatch).
    /// Issue: #35
    /// </summary>
    public async Task<IReadOnlyList<string>> GetFilesNeedingAnalysisAsync(
        long solutionId, IDictionary<string, string> currentFileHashes)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        var needsAnalysis = new List<string>();

        foreach (var (filePath, currentHash) in currentFileHashes)
        {
            // Check if file is tracked
            var tracked = await GetFileAsync(solutionId, filePath);
            if (tracked == null)
            {
                // File not in graph - needs analysis
                needsAnalysis.Add(filePath);
                continue;
            }

            // Check if hash changed (stale)
            if (tracked.ContentHash != currentHash)
            {
                needsAnalysis.Add(filePath);
            }
        }

        return needsAnalysis;
    }

    /// <summary>
    /// Computes SHA256 hash of file content (first 16 hex chars).
    /// </summary>
    public static string ComputeFileHash(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>
    /// Creates a FileRecord for a file with current metadata.
    /// </summary>
    public static FileRecord CreateFileRecord(long solutionId, string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        return new FileRecord
        {
            SolutionId = solutionId,
            FilePath = filePath,
            LastModified = fileInfo.LastWriteTimeUtc,
            ContentHash = ComputeFileHash(filePath),
            LastAnalyzed = DateTime.UtcNow
        };
    }
}
