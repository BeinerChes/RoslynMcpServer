namespace RoslynMcpServer.Tests.Services;

/// <summary>
/// Tests for the GetCallers functionality.
/// </summary>
public class GetCallersTests
{
    /// <summary>
    /// Tests that GetCallersAsync returns error for non-existent solution.
    /// Issue: #7
    /// </summary>
    [Fact]
    public async Task GetCallersAsync_WithInvalidSolution_ReturnsError()
    {
        // Arrange
        var service = new SolutionAnalyzerService();
        var fakePath = @"C:\nonexistent\fake.sln";

        // Act
        var result = await SolutionAnalyzerService.GetCallersAsync(
            fakePath,
            "SomeMethod");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that GetCallersAsync returns error for non-existent file.
    /// Issue: #7
    /// </summary>
    [Fact]
    public async Task GetCallersAsync_WithNonExistentSymbol_ReturnsEmpty()
    {
        // Arrange
        var service = new SolutionAnalyzerService();
        var solutionPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RoslynMcpServer.slnx"));

        // Act
        var result = await SolutionAnalyzerService.GetCallersAsync(
            solutionPath,
            "NonExistentMethodXyz123");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.TotalCallers);
    }

    /// <summary>
    /// Tests that CallerInfo has required compact properties.
    /// Issue: #7
    /// </summary>
    [Fact]
    public void CallerInfo_HasCompactProperties()
    {
        // Arrange & Act
        var caller = new CallerInfo
        {
            File = "test.cs",
            Line = 42,
            Method = "DoWork"
        };

        // Assert
        Assert.Equal("test.cs", caller.File);
        Assert.Equal(42, caller.Line);
        Assert.Equal("DoWork", caller.Method);
        Assert.Null(caller.Type); // Optional field
    }

    /// <summary>
    /// Tests that GetCallersResult includes pagination info.
    /// Issue: #7
    /// </summary>
    [Fact]
    public void GetCallersResult_IncludesPaginationFields()
    {
        // Arrange & Act
        var result = new GetCallersResult
        {
            Success = true,
            Symbol = "MyClass.MyMethod()",
            TotalCallers = 100,
            ReturnedCount = 50,
            Callers = []
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal(100, result.TotalCallers);
        Assert.Equal(50, result.ReturnedCount);
    }
}
