namespace RoslynMcpServer.Tests.Web;

public class GraphApiSecurityTests
{


    [Fact]
    public void IsValidSolutionPath_NullPath_ReturnsFalse()
    {
        // Arrange & Act
        var result = RoslynMcpServer.Web.GraphApi.IsValidSolutionPath(null, out var normalizedPath, out var errorMessage);

        // Assert
        Assert.False(result);
        Assert.Null(normalizedPath);
        Assert.Equal("solutionPath query parameter is required", errorMessage);
    }


    [Fact]
    public void IsValidSolutionPath_EmptyPath_ReturnsFalse()
    {
        // Arrange & Act
        var result = RoslynMcpServer.Web.GraphApi.IsValidSolutionPath("", out var normalizedPath, out var errorMessage);

        // Assert
        Assert.False(result);
        Assert.Null(normalizedPath);
        Assert.Equal("solutionPath query parameter is required", errorMessage);
    }


    [Theory]
    [InlineData("C:\\test\\file.txt")]
    [InlineData("C:\\test\\file.cs")]
    [InlineData("C:\\test\\file.csproj")]
    [InlineData("C:\\test\\file.dll")]
    [InlineData("C:\\test\\file.exe")]
    [InlineData("/etc/passwd")]
    public void IsValidSolutionPath_NonSolutionExtension_ReturnsFalse(string path)
    {
        // Arrange & Act
        var result = RoslynMcpServer.Web.GraphApi.IsValidSolutionPath(path, out var normalizedPath, out var errorMessage);

        // Assert
        Assert.False(result);
        Assert.Null(normalizedPath);
        Assert.Equal("Only .sln and .slnx files are allowed", errorMessage);
    }


    [Theory]
    [InlineData("C:\\test\\..\\..\\..\\Windows\\System32\\config\\SAM.sln")]
    [InlineData("C:\\test\\folder\\..\\..\\secret.sln")]
    public void IsValidSolutionPath_PathTraversal_NormalizesAndRejectsNonexistent(string path)
    {
        // Arrange & Act
        // Path traversal sequences are normalized by Path.GetFullPath()
        var result = RoslynMcpServer.Web.GraphApi.IsValidSolutionPath(path, out var normalizedPath, out var errorMessage);

        // Assert
        Assert.False(result);
        // The path is normalized (traversal resolved) then checked for existence
        Assert.Equal("Solution file not found", errorMessage);
    }


    [Theory]
    [InlineData("C:\\nonexistent\\solution.sln")]
    [InlineData("C:\\nonexistent\\solution.slnx")]
    public void IsValidSolutionPath_NonExistentFile_ReturnsFalse(string path)
    {
        // Arrange & Act
        var result = RoslynMcpServer.Web.GraphApi.IsValidSolutionPath(path, out var normalizedPath, out var errorMessage);

        // Assert
        Assert.False(result);
        Assert.Null(normalizedPath);
        Assert.Equal("Solution file not found", errorMessage);
    }


    [Theory]
    [InlineData(".sln")]
    [InlineData(".SLN")]
    [InlineData(".slnx")]
    [InlineData(".SLNX")]
    public void IsValidSolutionPath_CaseInsensitiveExtension_AcceptsValidExtensions(string extension)
    {
        // Arrange
        var path = $"C:\\test\\solution{extension}";

        // Act
        var result = RoslynMcpServer.Web.GraphApi.IsValidSolutionPath(path, out var normalizedPath, out var errorMessage);

        // Assert
        Assert.False(result); // File doesn't exist
        Assert.Equal("Solution file not found", errorMessage); // But extension was accepted
    }
}