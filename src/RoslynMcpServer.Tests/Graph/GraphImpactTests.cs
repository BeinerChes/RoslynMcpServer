using RoslynMcpServer.Graph;

namespace RoslynMcpServer.Tests.Graph;

/// <summary>
/// Tests for GraphDatabase impact analysis (supporting GraphImpact tool).
/// Issue: #13
/// </summary>
public class GraphImpactAnalysisTests : IAsyncLifetime
{
    private GraphDatabase _db = null!;
    private SolutionRecord _solution = null!;

    public async ValueTask InitializeAsync()
    {
        _db = GraphDatabase.CreateInMemory();
        await _db.OpenAsync();
        _solution = await _db.GetOrCreateSolutionAsync(@"C:\test\MySolution.sln");
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<SymbolRecord> CreateSymbolAsync(string name, string filePath = "Test.cs")
    {
        var symbol = new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Method,
            Name = name,
            QualifiedName = $"Test.{name}",
            FilePath = filePath,
            Line = 1,
            Column = 1
        };
        await _db.InsertSymbolAsync(symbol);
        return symbol;
    }

    /// <summary>
    /// Tests that impact analysis finds all transitive callers.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetRecursiveCallersAsync_DeepCallChain_FindsAllCallers()
    {
        // Arrange: Main -> Service -> Repository -> Helper
        var main = await CreateSymbolAsync("Main", @"C:\test\Program.cs");
        var service = await CreateSymbolAsync("ServiceMethod", @"C:\test\Service.cs");
        var repo = await CreateSymbolAsync("RepoMethod", @"C:\test\Repository.cs");
        var helper = await CreateSymbolAsync("HelperMethod", @"C:\test\Helper.cs");

        await _db.InsertEdgeAsync(main.Id, service.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(service.Id, repo.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(repo.Id, helper.Id, EdgeType.Calls);

        // Act - if Helper changes, what's affected?
        var affected = await _db.GetRecursiveCallersAsync(helper.Id, maxDepth: 10);

        // Assert - Repository, Service, and Main are all affected
        Assert.Equal(3, affected.Count);
        Assert.Contains(affected, s => s.Name == "RepoMethod");
        Assert.Contains(affected, s => s.Name == "ServiceMethod");
        Assert.Contains(affected, s => s.Name == "Main");
    }

    /// <summary>
    /// Tests that impact analysis handles multiple callers at same level.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetRecursiveCallersAsync_MultipleCallers_FindsAll()
    {
        // Arrange: A, B, C all call SharedHelper
        var a = await CreateSymbolAsync("MethodA");
        var b = await CreateSymbolAsync("MethodB");
        var c = await CreateSymbolAsync("MethodC");
        var helper = await CreateSymbolAsync("SharedHelper");

        await _db.InsertEdgeAsync(a.Id, helper.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(b.Id, helper.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(c.Id, helper.Id, EdgeType.Calls);

        // Act
        var affected = await _db.GetRecursiveCallersAsync(helper.Id, maxDepth: 10);

        // Assert
        Assert.Equal(3, affected.Count);
    }

    /// <summary>
    /// Tests that impact analysis handles diamond dependency patterns.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetRecursiveCallersAsync_DiamondPattern_NoDuplicates()
    {
        // Arrange: Top -> Left, Top -> Right, Left -> Bottom, Right -> Bottom
        var top = await CreateSymbolAsync("Top");
        var left = await CreateSymbolAsync("Left");
        var right = await CreateSymbolAsync("Right");
        var bottom = await CreateSymbolAsync("Bottom");

        await _db.InsertEdgeAsync(top.Id, left.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(top.Id, right.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(left.Id, bottom.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(right.Id, bottom.Id, EdgeType.Calls);

        // Act - if Bottom changes, what's affected?
        var affected = await _db.GetRecursiveCallersAsync(bottom.Id, maxDepth: 10);

        // Assert - Left, Right, Top (no duplicates)
        Assert.Equal(3, affected.Count);
    }

    /// <summary>
    /// Tests that impact analysis handles circular call patterns without infinite loop.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetRecursiveCallersAsync_CircularCalls_HandlesGracefully()
    {
        // Arrange: A -> B -> C -> A (circular)
        var a = await CreateSymbolAsync("A");
        var b = await CreateSymbolAsync("B");
        var c = await CreateSymbolAsync("C");

        await _db.InsertEdgeAsync(a.Id, b.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(b.Id, c.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(c.Id, a.Id, EdgeType.Calls);

        // Act - should not infinite loop
        var affected = await _db.GetRecursiveCallersAsync(c.Id, maxDepth: 10);

        // Assert - should find callers without getting stuck
        Assert.Contains(affected, s => s.Name == "B");
    }
}

/// <summary>
/// Tests for dead code detection (supporting FindDeadCode tool).
/// Issue: #13
/// </summary>
public class DeadCodeDetectionTests : IAsyncLifetime
{
    private GraphDatabase _db = null!;
    private SolutionRecord _solution = null!;

    public async ValueTask InitializeAsync()
    {
        _db = GraphDatabase.CreateInMemory();
        await _db.OpenAsync();
        _solution = await _db.GetOrCreateSolutionAsync(@"C:\test\MySolution.sln");
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<SymbolRecord> CreateSymbolAsync(
        string name,
        SymbolKind kind = SymbolKind.Method,
        string filePath = "Test.cs")
    {
        var symbol = new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = kind,
            Name = name,
            QualifiedName = $"Test.{name}",
            FilePath = filePath,
            Line = 1,
            Column = 1
        };
        await _db.InsertSymbolAsync(symbol);
        return symbol;
    }

    /// <summary>
    /// Tests that GetCallersAsync returns empty for unused methods.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetCallersAsync_UnusedMethod_ReturnsEmpty()
    {
        // Arrange - create method with no callers
        var unused = await CreateSymbolAsync("UnusedMethod");

        // Act
        var callers = await _db.GetCallersAsync(unused.Id);

        // Assert
        Assert.Empty(callers);
    }

    /// <summary>
    /// Tests that used methods have callers.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetCallersAsync_UsedMethod_ReturnsCallers()
    {
        // Arrange
        var caller = await CreateSymbolAsync("Caller");
        var used = await CreateSymbolAsync("UsedMethod");
        await _db.InsertEdgeAsync(caller.Id, used.Id, EdgeType.Calls);

        // Act
        var callers = await _db.GetCallersAsync(used.Id);

        // Assert
        Assert.Single(callers);
    }

    /// <summary>
    /// Tests that GetSymbolsAsync can filter by kind for dead code detection.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task GetSymbolsAsync_FilterByMethodKind_ReturnsOnlyMethods()
    {
        // Arrange
        await CreateSymbolAsync("Method1", SymbolKind.Method);
        await CreateSymbolAsync("Method2", SymbolKind.Method);
        await CreateSymbolAsync("Property1", SymbolKind.Property);
        await CreateSymbolAsync("Field1", SymbolKind.Field);

        // Act
        var methods = await _db.GetSymbolsAsync(_solution.Id, kind: SymbolKind.Method);

        // Assert
        Assert.Equal(2, methods.Count);
        Assert.All(methods, m => Assert.Equal(SymbolKind.Method, m.Kind));
    }

    /// <summary>
    /// Tests identifying multiple dead code candidates.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task FindDeadCode_MultipleCandidates_FindsAll()
    {
        // Arrange - create mix of used and unused methods
        var entryPoint = await CreateSymbolAsync("Main");
        var used1 = await CreateSymbolAsync("UsedMethod1");
        var used2 = await CreateSymbolAsync("UsedMethod2");
        var unused1 = await CreateSymbolAsync("UnusedMethod1");
        var unused2 = await CreateSymbolAsync("UnusedMethod2");

        await _db.InsertEdgeAsync(entryPoint.Id, used1.Id, EdgeType.Calls);
        await _db.InsertEdgeAsync(used1.Id, used2.Id, EdgeType.Calls);

        // Act - find all methods
        var allMethods = await _db.GetSymbolsAsync(_solution.Id, kind: SymbolKind.Method);

        // Find methods with no callers (excluding entry point)
        var deadCandidates = new List<SymbolRecord>();
        foreach (var method in allMethods)
        {
            if (method.Name == "Main") continue; // Skip entry point
            var callers = await _db.GetCallersAsync(method.Id);
            if (callers.Count == 0)
                deadCandidates.Add(method);
        }

        // Assert - unused1, unused2, and entryPoint itself have no callers
        // But we skip entryPoint, so only unused1 and unused2
        Assert.Equal(2, deadCandidates.Count);
        Assert.Contains(deadCandidates, s => s.Name == "UnusedMethod1");
        Assert.Contains(deadCandidates, s => s.Name == "UnusedMethod2");
    }

    /// <summary>
    /// Tests that properties can also be detected as unused.
    /// Issue: #13
    /// </summary>
    [Fact]
    public async Task FindDeadCode_UnusedProperty_Detected()
    {
        // Arrange
        var unused = await CreateSymbolAsync("UnusedProperty", SymbolKind.Property);

        // Act
        var callers = await _db.GetCallersAsync(unused.Id);

        // Assert
        Assert.Empty(callers);
    }

    /// <summary>
    /// Tests that external symbols (BCL, framework) are filtered out.
    /// Issue: #36
    /// </summary>
    [Fact]
    public async Task GetSymbolsAsync_ExternalSymbols_FilteredByFilePath()
    {
        // Arrange - create local and external symbols
        await CreateSymbolAsync("LocalMethod", SymbolKind.Method, @"C:\test\Service.cs");
        await CreateSymbolAsync("ExternalMethod", SymbolKind.Method, "external");

        // Act - get all symbols
        var allSymbols = await _db.GetSymbolsAsync(_solution.Id, kind: SymbolKind.Method);

        // Filter like FindDeadCodeAsync does
        var localSymbols = allSymbols
            .Where(s => !string.IsNullOrEmpty(s.FilePath) && s.FilePath != "external")
            .ToList();

        // Assert - external symbols filtered out
        Assert.Single(localSymbols);
        Assert.Equal("LocalMethod", localSymbols[0].Name);
    }

    /// <summary>
    /// Tests that properties accessed via Reads edge are not flagged as dead.
    /// Issue: #36 - Properties use Reads/Accesses edges, not Calls.
    /// </summary>
    [Fact]
    public async Task GetCallersAsync_PropertyWithReadsEdge_ReturnsCallers()
    {
        // Arrange - create property and reader
        var reader = await CreateSymbolAsync("ReaderMethod", SymbolKind.Method);
        var property = await CreateSymbolAsync("MyProperty", SymbolKind.Property);

        // Property is read, not called
        await _db.InsertEdgeAsync(reader.Id, property.Id, EdgeType.Reads);

        // Act - check callers with null edgeType (all edges)
        var callers = await _db.GetCallersAsync(property.Id, edgeType: null);

        // Assert - should find the reader
        Assert.Single(callers);
        Assert.Equal("ReaderMethod", callers[0].Name);
    }

    /// <summary>
    /// Tests that properties accessed via Accesses edge are not flagged as dead.
    /// Issue: #36
    /// </summary>
    [Fact]
    public async Task GetCallersAsync_PropertyWithAccessesEdge_ReturnsCallers()
    {
        // Arrange
        var accessor = await CreateSymbolAsync("AccessorMethod", SymbolKind.Method);
        var property = await CreateSymbolAsync("MyProperty", SymbolKind.Property);

        await _db.InsertEdgeAsync(accessor.Id, property.Id, EdgeType.Accesses);

        // Act - check callers with null edgeType
        var callers = await _db.GetCallersAsync(property.Id, edgeType: null);

        // Assert
        Assert.Single(callers);
    }

    /// <summary>
    /// Tests that Calls-only check misses property reads.
    /// Issue: #36 - This was the original bug.
    /// </summary>
    [Fact]
    public async Task GetCallersAsync_PropertyWithReadsEdge_CallsOnlyMissesIt()
    {
        // Arrange
        var reader = await CreateSymbolAsync("ReaderMethod", SymbolKind.Method);
        var property = await CreateSymbolAsync("MyProperty", SymbolKind.Property);
        await _db.InsertEdgeAsync(reader.Id, property.Id, EdgeType.Reads);

        // Act - check with Calls edge type only (old behavior)
        var callersCallsOnly = await _db.GetCallersAsync(property.Id, edgeType: EdgeType.Calls);

        // Assert - Calls-only misses the read
        Assert.Empty(callersCallsOnly);
    }
}
