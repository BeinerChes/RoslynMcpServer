namespace RoslynMcpServer.Tests.Services;

/// <summary>
/// Tests for the roslyn_setup_hooks tool.
/// Issue: #81
/// </summary>
public class SetupHooksTests : IDisposable
{
    private readonly string _tempDir;

    public SetupHooksTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SetupHooksTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void UpdateGitignore_NoExistingFile_CreatesWithEntries()
    {
        // Arrange
        var gitignorePath = Path.Combine(_tempDir, ".gitignore");

        // Act
        var result = InvokeUpdateGitignore(gitignorePath);

        // Assert
        Assert.Equal("created", result);
        Assert.True(File.Exists(gitignorePath));

        var content = File.ReadAllText(gitignorePath);
        Assert.Contains(".claude/", content);
        Assert.Contains(".roslyn-mcp/", content);
    }

    [Fact]
    public void UpdateGitignore_ExistingFile_AppendsEntries()
    {
        // Arrange
        var gitignorePath = Path.Combine(_tempDir, ".gitignore");
        File.WriteAllText(gitignorePath, "bin/\nobj/\n");

        // Act
        var result = InvokeUpdateGitignore(gitignorePath);

        // Assert
        Assert.Equal("updated", result);

        var content = File.ReadAllText(gitignorePath);
        Assert.Contains("bin/", content);
        Assert.Contains("obj/", content);
        Assert.Contains(".claude/", content);
        Assert.Contains(".roslyn-mcp/", content);
    }

    [Fact]
    public void UpdateGitignore_AlreadyHasEntries_SkipsUpdate()
    {
        // Arrange
        var gitignorePath = Path.Combine(_tempDir, ".gitignore");
        File.WriteAllText(gitignorePath, "bin/\n.claude/\n.roslyn-mcp/\n");

        // Act
        var result = InvokeUpdateGitignore(gitignorePath);

        // Assert
        Assert.Equal("skipped (entries already present)", result);
    }

    [Fact]
    public void UpdateGitignore_HasClaudeOnly_AddsRoslynMcp()
    {
        // Arrange
        var gitignorePath = Path.Combine(_tempDir, ".gitignore");
        File.WriteAllText(gitignorePath, "bin/\n.claude/\n");

        // Act
        var result = InvokeUpdateGitignore(gitignorePath);

        // Assert
        Assert.Equal("updated", result);

        var content = File.ReadAllText(gitignorePath);
        Assert.Contains(".roslyn-mcp/", content);
    }

    [Fact]
    public void UpdateGitignore_EntriesWithoutTrailingSlash_RecognizedAsPresent()
    {
        // Arrange
        var gitignorePath = Path.Combine(_tempDir, ".gitignore");
        File.WriteAllText(gitignorePath, "bin/\n.claude\n.roslyn-mcp\n");

        // Act
        var result = InvokeUpdateGitignore(gitignorePath);

        // Assert
        Assert.Equal("skipped (entries already present)", result);
    }

    /// <summary>
    /// Invokes the private UpdateGitignore method via reflection.
    /// </summary>
    private static string InvokeUpdateGitignore(string gitignorePath)
    {
        var method = typeof(RoslynTools).GetMethod(
            "UpdateGitignore",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var result = method.Invoke(null, [gitignorePath]);
        return (string)result!;
    }
}
