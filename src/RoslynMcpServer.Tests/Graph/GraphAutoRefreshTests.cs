using RoslynMcpServer.Graph;

namespace RoslynMcpServer.Tests.Graph;

/// <summary>
/// Tests for automatic graph refresh when symbols are not found.
/// Issue: #35
/// </summary>
public class GraphAutoRefreshTests : IAsyncLifetime
{
    private GraphDatabase _db = null!;
    private SolutionRecord _solution = null!;

    public async Task InitializeAsync()
    {
        _db = GraphDatabase.CreateInMemory();
        await _db.OpenAsync();
        _solution = await _db.GetOrCreateSolutionAsync(@"C:\test\MySolution.sln");
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Tests that a file not in the graph is detected as needing analysis.
    /// Issue: #35
    /// </summary>
    [Fact]
    public async Task IsFileInGraphAsync_FileNotTracked_ReturnsFalse()
    {
        // Arrange - file not added to graph
        var filePath = @"C:\test\NewFile.cs";

        // Act
        var isInGraph = await _db.IsFileInGraphAsync(_solution.Id, filePath);

        // Assert
        Assert.False(isInGraph);
    }

    /// <summary>
    /// Tests that a file with symbols is detected as being in the graph.
    /// Issue: #35
    /// </summary>
    [Fact]
    public async Task IsFileInGraphAsync_FileWithSymbols_ReturnsTrue()
    {
        // Arrange - add a symbol from a file
        var filePath = @"C:\test\ExistingFile.cs";
        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Method,
            Name = "ExistingMethod",
            QualifiedName = "Test.ExistingMethod()",
            FilePath = filePath,
            Line = 10
        });

        // Act
        var isInGraph = await _db.IsFileInGraphAsync(_solution.Id, filePath);

        // Assert
        Assert.True(isInGraph);
    }

    /// <summary>
    /// Tests getting all files that need analysis (not in graph or stale).
    /// Issue: #35
    /// </summary>
    [Fact]
    public async Task GetFilesNeedingAnalysisAsync_MixedFiles_ReturnsCorrectList()
    {
        // Arrange
        var existingFile = @"C:\test\Existing.cs";
        var staleFile = @"C:\test\Stale.cs";
        var newFile = @"C:\test\New.cs";

        // Add existing file with current hash
        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Method,
            Name = "Method1",
            QualifiedName = "Test.Method1()",
            FilePath = existingFile,
            Line = 10
        });
        await _db.UpdateFileHashAsync(_solution.Id, existingFile, "hash1");

        // Add stale file with old hash
        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Method,
            Name = "Method2",
            QualifiedName = "Test.Method2()",
            FilePath = staleFile,
            Line = 10
        });
        await _db.UpdateFileHashAsync(_solution.Id, staleFile, "old_hash");

        // Provide current hashes (staleFile has different hash now)
        var currentHashes = new Dictionary<string, string>
        {
            { existingFile, "hash1" },      // Same - not stale
            { staleFile, "new_hash" },      // Different - stale
            { newFile, "hash3" }            // New file - not in graph
        };

        // Act
        var needsAnalysis = await _db.GetFilesNeedingAnalysisAsync(_solution.Id, currentHashes);

        // Assert
        Assert.Equal(2, needsAnalysis.Count);
        Assert.Contains(staleFile, needsAnalysis);
        Assert.Contains(newFile, needsAnalysis);
        Assert.DoesNotContain(existingFile, needsAnalysis);
    }

    /// <summary>
    /// Tests that FindSymbolWithRefreshAsync finds a symbol after refresh hint.
    /// This tests the database-level support for the refresh workflow.
    /// Issue: #35
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_AfterInsert_FindsNewSymbol()
    {
        // Arrange - empty graph initially
        var result1 = await _db.FindSymbolAsync(_solution.Id, "NewMethod");
        Assert.Null(result1.Symbol);
        Assert.Contains("not found", result1.Error!.ToLower());

        // Act - simulate refresh by inserting the symbol
        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Method,
            Name = "NewMethod",
            QualifiedName = "Test.Class.NewMethod()",
            FilePath = @"C:\test\File.cs",
            Line = 10
        });

        var result2 = await _db.FindSymbolAsync(_solution.Id, "NewMethod");

        // Assert - now found
        Assert.NotNull(result2.Symbol);
        Assert.Equal("NewMethod", result2.Symbol.Name);
    }
}
