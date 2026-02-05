namespace RoslynMcpServer.Tests.Services;

/// <summary>
/// Tests for the MCP server protocol handling.
/// </summary>
public class McpServerTests
{
    /// <summary>
    /// Tests that the server version is correctly read from assembly metadata.
    /// Issue: #3
    /// </summary>
    [Fact]
    public void Version_ReadsFromAssembly_ReturnsValidVersion()
    {
        // Arrange & Act
        var version = McpServer.Version;

        // Assert
        Assert.NotNull(version);
        Assert.NotEqual("unknown", version);
        Assert.Contains(".", version); // Version format: X.X.X or X.X.X-rc
    }

    /// <summary>
    /// Tests that version string has expected format (X.X.X or X.X.X-rc).
    /// Issue: #3
    /// </summary>
    [Fact]
    public void Version_HasExpectedFormat()
    {
        // Arrange & Act
        var version = McpServer.Version;

        // Assert
        // Version should match semantic versioning pattern
        Assert.Matches(@"^\d+\.\d+\.\d+(-\w+)?$", version);
    }
}
