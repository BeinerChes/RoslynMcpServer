using RoslynMcpServer.Graph;

namespace RoslynMcpServer.Tests.Graph;

/// <summary>
/// Tests for GraphDatabase core functionality.
/// Issue: #13
/// </summary>
public class GraphDatabaseTests : IAsyncLifetime
{
    private GraphDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _db = GraphDatabase.CreateInMemory();
        await _db.OpenAsync();
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Tests that CreateInMemory creates a valid in-memory database.
    /// Issue: #13
    /// </summary>
    [Fact]
    public void CreateInMemory_ReturnsValidDatabase()
    {
        // Arrange & Act - done in InitializeAsync
        // Assert
        Assert.NotNull(_db);
        Assert.Equal(":memory:", _db.DatabasePath);
    }

    /// <summary>
    /// Tests that Exists returns true for in-memory databases.
    /// Issue: #13
    /// </summary>
    [Fact]
    public void Exists_ForInMemoryDatabase_ReturnsTrue()
    {
        // Assert
        Assert.True(_db.Exists());
    }
}

/// <summary>
/// Tests for GraphDatabase solution operations.
/// Issue: #13
/// </summary>
public class GraphDatabaseSolutionTests : IAsyncLifetime
{
    private GraphDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _db = GraphDatabase.CreateInMemory();
        await _db.OpenAsync();
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Tests that GetOrCreateSolutionAsync creates a new solution record.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetOrCreateSolutionAsync_NewSolution_CreatesSolution()
    {
        // Act
        var solution = await _db.GetOrCreateSolutionAsync(@"C:\test\MySolution.sln");

        // Assert
        Assert.NotNull(solution);
        Assert.True(solution.Id > 0);
        Assert.Equal("MySolution", solution.Name);
        Assert.Contains("MySolution.sln", solution.Path);
    }

    /// <summary>
    /// Tests that GetOrCreateSolutionAsync returns existing solution.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetOrCreateSolutionAsync_ExistingSolution_ReturnsSameSolution()
    {
        // Arrange
        var first = await _db.GetOrCreateSolutionAsync(@"C:\test\MySolution.sln");

        // Act
        var second = await _db.GetOrCreateSolutionAsync(@"C:\test\MySolution.sln");

        // Assert
        Assert.Equal(first.Id, second.Id);
    }

    /// <summary>
    /// Tests that GetSolutionAsync returns null for non-existent solution.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetSolutionAsync_NonExistent_ReturnsNull()
    {
        // Act
        var solution = await _db.GetSolutionAsync(@"C:\nonexistent\Solution.sln");

        // Assert
        Assert.Null(solution);
    }

    /// <summary>
    /// Tests that GetAllSolutionsAsync returns all tracked solutions.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetAllSolutionsAsync_MultipleSolutions_ReturnsAll()
    {
        // Arrange
        await _db.GetOrCreateSolutionAsync(@"C:\test\Solution1.sln");
        await _db.GetOrCreateSolutionAsync(@"C:\test\Solution2.sln");

        // Act
        var solutions = await _db.GetAllSolutionsAsync();

        // Assert
        Assert.Equal(2, solutions.Count);
    }

    /// <summary>
    /// Tests that UpdateSolutionAnalyzedAsync updates the timestamp.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task UpdateSolutionAnalyzedAsync_UpdatesTimestamp()
    {
        // Arrange
        var solution = await _db.GetOrCreateSolutionAsync(@"C:\test\MySolution.sln");
        Assert.Null(solution.LastAnalyzed);

        // Act
        await _db.UpdateSolutionAnalyzedAsync(solution.Id, "abc123");
        var updated = await _db.GetSolutionAsync(@"C:\test\MySolution.sln");

        // Assert
        Assert.NotNull(updated);
        Assert.NotNull(updated.LastAnalyzed);
        Assert.Equal("abc123", updated.SolutionHash);
    }

    /// <summary>
    /// Tests that DeleteSolutionAsync removes the solution.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task DeleteSolutionAsync_RemovesSolution()
    {
        // Arrange
        var solution = await _db.GetOrCreateSolutionAsync(@"C:\test\MySolution.sln");

        // Act
        await _db.DeleteSolutionAsync(solution.Id);
        var deleted = await _db.GetSolutionAsync(@"C:\test\MySolution.sln");

        // Assert
        Assert.Null(deleted);
    }
}

/// <summary>
/// Tests for GraphDatabase symbol operations.
/// Issue: #13
/// </summary>
public class GraphDatabaseSymbolTests : IAsyncLifetime
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
    /// Tests that InsertSymbolAsync creates a new symbol.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task InsertSymbolAsync_ValidSymbol_CreatesSymbol()
    {
        // Arrange
        var symbol = new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Method,
            Name = "DoWork",
            QualifiedName = "MyNamespace.MyClass.DoWork",
            FilePath = @"C:\test\MyClass.cs",
            Line = 10,
            Column = 5
        };

        // Act
        var id = await _db.InsertSymbolAsync(symbol);

        // Assert
        Assert.True(id > 0);
        Assert.Equal(id, symbol.Id);
    }

    /// <summary>
    /// Tests that GetSymbolByQualifiedNameAsync returns the symbol.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetSymbolByQualifiedNameAsync_ExistingSymbol_ReturnsSymbol()
    {
        // Arrange
        var symbol = new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Method,
            Name = "DoWork",
            QualifiedName = "MyNamespace.MyClass.DoWork",
            FilePath = @"C:\test\MyClass.cs",
            Line = 10,
            Column = 5
        };
        await _db.InsertSymbolAsync(symbol);

        // Act
        var found = await _db.GetSymbolByQualifiedNameAsync(_solution.Id, "MyNamespace.MyClass.DoWork");

        // Assert
        Assert.NotNull(found);
        Assert.Equal("DoWork", found.Name);
    }

    /// <summary>
    /// Tests that GetSymbolsAsync filters by kind.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetSymbolsAsync_FilterByKind_ReturnsFilteredSymbols()
    {
        // Arrange
        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id, Kind = SymbolKind.Method,
            Name = "Method1", QualifiedName = "A.Method1", FilePath = "A.cs", Line = 1, Column = 1
        });
        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id, Kind = SymbolKind.Property,
            Name = "Prop1", QualifiedName = "A.Prop1", FilePath = "A.cs", Line = 2, Column = 1
        });

        // Act
        var methods = await _db.GetSymbolsAsync(_solution.Id, kind: SymbolKind.Method);
        var properties = await _db.GetSymbolsAsync(_solution.Id, kind: SymbolKind.Property);

        // Assert
        Assert.Single(methods);
        Assert.Single(properties);
    }

    /// <summary>
    /// Tests that UpdateSymbolStatusAsync updates the status.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task UpdateSymbolStatusAsync_UpdatesStatus()
    {
        // Arrange
        var symbol = new SymbolRecord
        {
            SolutionId = _solution.Id, Kind = SymbolKind.Method,
            Name = "DoWork", QualifiedName = "A.DoWork", FilePath = "A.cs", Line = 1, Column = 1
        };
        await _db.InsertSymbolAsync(symbol);

        // Act
        await _db.UpdateSymbolStatusAsync(symbol.Id, SymbolStatus.Analyzed, "hash123");
        var updated = await _db.GetSymbolByQualifiedNameAsync(_solution.Id, "A.DoWork");

        // Assert
        Assert.NotNull(updated);
        Assert.Equal("hash123", updated.BodyHash);
    }

    /// <summary>
    /// Tests that GetSymbolStatsAsync returns correct counts.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetSymbolStatsAsync_ReturnsCorrectCounts()
    {
        // Arrange
        for (int i = 0; i < 3; i++)
        {
            await _db.InsertSymbolAsync(new SymbolRecord
            {
                SolutionId = _solution.Id, Kind = SymbolKind.Method,
                Name = $"Method{i}", QualifiedName = $"A.Method{i}", FilePath = "A.cs", Line = i, Column = 1,
                Status = SymbolStatus.Pending
            });
        }

        // Act
        var stats = await _db.GetSymbolStatsAsync(_solution.Id);

        // Assert
        Assert.Equal(3, stats.Total);
        Assert.Equal(3, stats.Pending);
        Assert.Equal(0, stats.Analyzed);
    }
}

/// <summary>
/// Tests for GraphDatabase edge operations and graph queries.
/// Issue: #13
/// </summary>
public class GraphDatabaseEdgeTests : IAsyncLifetime
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

    private async Task<SymbolRecord> CreateSymbolAsync(string name)
    {
        var symbol = new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Method,
            Name = name,
            QualifiedName = $"Test.{name}",
            FilePath = "Test.cs",
            Line = 1,
            Column = 1
        };
        await _db.InsertSymbolAsync(symbol);
        return symbol;
    }

    /// <summary>
    /// Tests that InsertEdgeAsync creates an edge between symbols.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task InsertEdgeAsync_ValidEdge_CreatesEdge()
    {
        // Arrange
        var caller = await CreateSymbolAsync("Caller");
        var callee = await CreateSymbolAsync("Callee");

        // Act
        await _db.InsertEdgeAsync(caller.Id, callee.Id, EdgeType.Calls);

        // Assert
        var callees = await _db.GetCalleesAsync(caller.Id);
        Assert.Single(callees);
        Assert.Equal("Callee", callees[0].Name);
    }

    /// <summary>
    /// Tests that GetCallersAsync returns direct callers.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetCallersAsync_HasCallers_ReturnsCallers()
    {
        // Arrange
        var a = await CreateSymbolAsync("A");
        var b = await CreateSymbolAsync("B");
        var c = await CreateSymbolAsync("C");
        await _db.InsertEdgeAsync(a.Id, c.Id, EdgeType.Calls); // A calls C
        await _db.InsertEdgeAsync(b.Id, c.Id, EdgeType.Calls); // B calls C

        // Act
        var callers = await _db.GetCallersAsync(c.Id);

        // Assert
        Assert.Equal(2, callers.Count);
    }

    /// <summary>
    /// Tests that GetCalleesAsync returns direct callees.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetCalleesAsync_HasCallees_ReturnsCallees()
    {
        // Arrange
        var a = await CreateSymbolAsync("A");
        var b = await CreateSymbolAsync("B");
        var c = await CreateSymbolAsync("C");
        await _db.InsertEdgeAsync(a.Id, b.Id, EdgeType.Calls); // A calls B
        await _db.InsertEdgeAsync(a.Id, c.Id, EdgeType.Calls); // A calls C

        // Act
        var callees = await _db.GetCalleesAsync(a.Id);

        // Assert
        Assert.Equal(2, callees.Count);
    }

    /// <summary>
    /// Tests that GetRecursiveCallersAsync returns all callers up the chain.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetRecursiveCallersAsync_ChainOfCallers_ReturnsAll()
    {
        // Arrange: A -> B -> C -> D (A calls B, B calls C, C calls D)
        var a = await CreateSymbolAsync("A");
        var b = await CreateSymbolAsync("B");
        var c = await CreateSymbolAsync("C");
        var d = await CreateSymbolAsync("D");
        await _db.InsertEdgeAsync(a.Id, b.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(b.Id, c.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(c.Id, d.Id, EdgeType.Calls);

        // Act - get all callers of D
        var callers = await _db.GetRecursiveCallersAsync(d.Id);

        // Assert - should find C, B, A
        Assert.Equal(3, callers.Count);
    }

    /// <summary>
    /// Tests that GetRecursiveCallersAsync respects maxDepth.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetRecursiveCallersAsync_WithMaxDepth_RespectsLimit()
    {
        // Arrange: A -> B -> C -> D
        var a = await CreateSymbolAsync("A");
        var b = await CreateSymbolAsync("B");
        var c = await CreateSymbolAsync("C");
        var d = await CreateSymbolAsync("D");
        await _db.InsertEdgeAsync(a.Id, b.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(b.Id, c.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(c.Id, d.Id, EdgeType.Calls);

        // Act - get callers of D with max depth 2
        var callers = await _db.GetRecursiveCallersAsync(d.Id, maxDepth: 2);

        // Assert - should find C, B (not A)
        Assert.Equal(2, callers.Count);
    }

    /// <summary>
    /// Tests that GetRecursiveCalleesAsync returns all callees down the chain.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetRecursiveCalleesAsync_ChainOfCallees_ReturnsAll()
    {
        // Arrange: A -> B -> C -> D
        var a = await CreateSymbolAsync("A");
        var b = await CreateSymbolAsync("B");
        var c = await CreateSymbolAsync("C");
        var d = await CreateSymbolAsync("D");
        await _db.InsertEdgeAsync(a.Id, b.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(b.Id, c.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(c.Id, d.Id, EdgeType.Calls);

        // Act - get all callees of A
        var callees = await _db.GetRecursiveCalleesAsync(a.Id);

        // Assert - should find B, C, D
        Assert.Equal(3, callees.Count);
    }

    /// <summary>
    /// Tests that GetEdgeStatsAsync returns correct counts.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetEdgeStatsAsync_ReturnsCorrectCounts()
    {
        // Arrange
        var a = await CreateSymbolAsync("A");
        var b = await CreateSymbolAsync("B");
        var c = await CreateSymbolAsync("C");
        await _db.InsertEdgeAsync(a.Id, b.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(a.Id, c.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(b.Id, c.Id, EdgeType.Reads);

        // Act
        var stats = await _db.GetEdgeStatsAsync(_solution.Id);

        // Assert
        Assert.Equal(3, stats.Total);
        Assert.Equal(2, stats.Calls);
        Assert.Equal(1, stats.Reads);
    }
}
