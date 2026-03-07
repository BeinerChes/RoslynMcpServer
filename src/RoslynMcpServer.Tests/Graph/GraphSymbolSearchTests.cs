using RoslynMcpServer.Graph;

namespace RoslynMcpServer.Tests.Graph;

/// <summary>
/// Tests for partial symbol name matching in GraphDatabase.
/// Issue: #33
/// </summary>
public class GraphSymbolSearchTests : IAsyncLifetime
{
    private GraphDatabase _db = null!;
    private SolutionRecord _solution = null!;

    public async ValueTask InitializeAsync()
    {
        _db = GraphDatabase.CreateInMemory();
        await _db.OpenAsync();
        _solution = await _db.GetOrCreateSolutionAsync(@"C:\test\MySolution.sln");

        // Create test symbols with full qualified names (like Roslyn generates)
        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Method,
            Name = "GetCallersAsync",
            QualifiedName = "MyApp.Services.UserService.GetCallersAsync(string, int)",
            FilePath = @"C:\test\UserService.cs",
            Line = 10,
            Column = 5
        });

        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Method,
            Name = "GetCallersAsync",
            QualifiedName = "MyApp.Services.OrderService.GetCallersAsync()",
            FilePath = @"C:\test\OrderService.cs",
            Line = 20,
            Column = 5
        });

        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Method,
            Name = "SaveAsync",
            QualifiedName = "MyApp.Services.UserService.SaveAsync(MyApp.Models.User)",
            FilePath = @"C:\test\UserService.cs",
            Line = 30,
            Column = 5
        });

        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Property,
            Name = "Name",
            QualifiedName = "MyApp.Models.User.Name",
            FilePath = @"C:\test\User.cs",
            Line = 5,
            Column = 5
        });
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Tests that exact qualified name match still works.
    /// Issue: #33
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_ExactMatch_ReturnsSymbol()
    {
        // Act
        var result = await _db.FindSymbolAsync(
            _solution.Id,
            "MyApp.Services.UserService.GetCallersAsync(string, int)");

        // Assert
        Assert.NotNull(result.Symbol);
        Assert.Equal("GetCallersAsync", result.Symbol.Name);
        Assert.Null(result.Error);
    }

    /// <summary>
    /// Tests that method name only finds matching symbols.
    /// Issue: #33
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_MethodNameOnly_FindsMatches()
    {
        // Act
        var result = await _db.FindSymbolAsync(_solution.Id, "SaveAsync");

        // Assert
        Assert.NotNull(result.Symbol);
        Assert.Equal("SaveAsync", result.Symbol.Name);
    }

    /// <summary>
    /// Tests that Type.Method pattern finds the right symbol.
    /// Issue: #33
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_TypeDotMethod_FindsSymbol()
    {
        // Act
        var result = await _db.FindSymbolAsync(_solution.Id, "UserService.GetCallersAsync");

        // Assert
        Assert.NotNull(result.Symbol);
        Assert.Contains("UserService", result.Symbol.QualifiedName);
        Assert.Equal("GetCallersAsync", result.Symbol.Name);
    }

    /// <summary>
    /// Tests that ambiguous name returns error with options.
    /// Issue: #33
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_AmbiguousName_ReturnsErrorWithOptions()
    {
        // Act - "GetCallersAsync" exists in both UserService and OrderService
        var result = await _db.FindSymbolAsync(_solution.Id, "GetCallersAsync");

        // Assert
        Assert.Null(result.Symbol);
        Assert.NotNull(result.Error);
        Assert.Contains("Multiple", result.Error);
        Assert.NotNull(result.Candidates);
        Assert.Equal(2, result.Candidates.Count);
    }

    /// <summary>
    /// Tests that non-existent symbol returns appropriate error.
    /// Issue: #33
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_NotFound_ReturnsError()
    {
        // Act
        var result = await _db.FindSymbolAsync(_solution.Id, "NonExistentMethod");

        // Assert
        Assert.Null(result.Symbol);
        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error.ToLower());
    }

    /// <summary>
    /// Tests that partial namespace matching works.
    /// Issue: #33
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_PartialNamespace_FindsSymbol()
    {
        // Act
        var result = await _db.FindSymbolAsync(_solution.Id, "Services.UserService.SaveAsync");

        // Assert
        Assert.NotNull(result.Symbol);
        Assert.Equal("SaveAsync", result.Symbol.Name);
    }

    /// <summary>
    /// Tests property lookup by simple name.
    /// Issue: #33
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_PropertyName_FindsProperty()
    {
        // Act
        var result = await _db.FindSymbolAsync(_solution.Id, "User.Name");

        // Assert
        Assert.NotNull(result.Symbol);
        Assert.Equal("Name", result.Symbol.Name);
        Assert.Equal(SymbolKind.Property, result.Symbol.Kind);
    }
}

/// <summary>
/// Tests for constructor search with .ctor syntax.
/// Issue: #65
/// </summary>
public class GraphConstructorSearchTests : IAsyncLifetime
{
    private GraphDatabase _db = null!;
    private SolutionRecord _solution = null!;

    public async ValueTask InitializeAsync()
    {
        _db = GraphDatabase.CreateInMemory();
        await _db.OpenAsync();
        _solution = await _db.GetOrCreateSolutionAsync(@"C:\test\MySolution.sln");

        // Create constructor symbols as Roslyn stores them (TypeName.TypeName(params))
        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Constructor,
            Name = ".ctor",
            QualifiedName = "MyApp.Models.User.User(string)",
            FilePath = @"C:\test\User.cs",
            Line = 10,
            Column = 5
        });

        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Constructor,
            Name = ".ctor",
            QualifiedName = "MyApp.Models.User.User(string, int)",
            FilePath = @"C:\test\User.cs",
            Line = 15,
            Column = 5
        });

        await _db.InsertSymbolAsync(new SymbolRecord
        {
            SolutionId = _solution.Id,
            Kind = SymbolKind.Constructor,
            Name = ".ctor",
            QualifiedName = "MyApp.Services.OrderService.OrderService()",
            FilePath = @"C:\test\OrderService.cs",
            Line = 5,
            Column = 5
        });
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Tests that .ctor syntax with full namespace and params finds the constructor.
    /// Issue: #65
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_CtorWithFullPathAndParams_FindsConstructor()
    {
        // Act - Query with .ctor syntax
        var result = await _db.FindSymbolAsync(
            _solution.Id,
            "MyApp.Models.User..ctor(string)");

        // Assert
        Assert.NotNull(result.Symbol);
        Assert.Equal(".ctor", result.Symbol.Name);
        Assert.Equal("MyApp.Models.User.User(string)", result.Symbol.QualifiedName);
        Assert.Equal(SymbolKind.Constructor, result.Symbol.Kind);
    }

    /// <summary>
    /// Tests that .ctor syntax with multiple params works.
    /// Issue: #65
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_CtorWithMultipleParams_FindsConstructor()
    {
        // Act
        var result = await _db.FindSymbolAsync(
            _solution.Id,
            "MyApp.Models.User..ctor(string, int)");

        // Assert
        Assert.NotNull(result.Symbol);
        Assert.Equal("MyApp.Models.User.User(string, int)", result.Symbol.QualifiedName);
    }

    /// <summary>
    /// Tests that .ctor without params on a type with single constructor finds it.
    /// Issue: #65
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_CtorNoParams_SingleConstructor_FindsIt()
    {
        // Act - OrderService has only one constructor
        var result = await _db.FindSymbolAsync(
            _solution.Id,
            "MyApp.Services.OrderService..ctor");

        // Assert
        Assert.NotNull(result.Symbol);
        Assert.Equal("MyApp.Services.OrderService.OrderService()", result.Symbol.QualifiedName);
    }

    /// <summary>
    /// Tests that .ctor without params on type with multiple constructors returns candidates.
    /// Issue: #65
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_CtorNoParams_MultipleConstructors_ReturnsCandidates()
    {
        // Act - User has two constructors
        var result = await _db.FindSymbolAsync(
            _solution.Id,
            "MyApp.Models.User..ctor");

        // Assert
        Assert.Null(result.Symbol);
        Assert.NotNull(result.Error);
        Assert.Contains("Multiple", result.Error);
        Assert.NotNull(result.Candidates);
        Assert.Equal(2, result.Candidates.Count);
    }

    /// <summary>
    /// Tests that parameterless constructor can be found with empty parens.
    /// Issue: #65
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_CtorWithEmptyParens_FindsParameterlessConstructor()
    {
        // Act
        var result = await _db.FindSymbolAsync(
            _solution.Id,
            "MyApp.Services.OrderService..ctor()");

        // Assert
        Assert.NotNull(result.Symbol);
        Assert.Equal("MyApp.Services.OrderService.OrderService()", result.Symbol.QualifiedName);
    }

    /// <summary>
    /// Tests partial type path with .ctor syntax.
    /// Issue: #65
    /// </summary>
    [Fact]
    public async Task FindSymbolAsync_CtorWithPartialPath_FindsConstructor()
    {
        // Act - Using just "User..ctor(string)" without full namespace
        var result = await _db.FindSymbolAsync(
            _solution.Id,
            "User..ctor(string)");

        // Assert
        Assert.NotNull(result.Symbol);
        Assert.Equal("MyApp.Models.User.User(string)", result.Symbol.QualifiedName);
    }
}
