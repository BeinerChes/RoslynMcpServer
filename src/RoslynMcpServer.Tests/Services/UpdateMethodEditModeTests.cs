namespace RoslynMcpServer.Tests.Services;

/// <summary>
/// Tests for UpdateMethod edit mode (oldText/newText).
/// Issue: #127
/// </summary>
public class UpdateMethodEditModeTests : IAsyncLifetime
{
    private string _tempDir = "";
    private string _solutionPath = "";
    private string _projectPath = "";
    private string _sourceFilePath = "";

    public async Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UpdateMethodEditTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _solutionPath = Path.Combine(_tempDir, "Test.sln");
        _projectPath = Path.Combine(_tempDir, "Test.csproj");
        _sourceFilePath = Path.Combine(_tempDir, "Calculator.cs");

        await File.WriteAllTextAsync(_solutionPath, """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Test", "Test.csproj", "{12345678-1234-1234-1234-123456789012}"
            EndProject
            """);

        await File.WriteAllTextAsync(_projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); }
            catch { /* Ignore cleanup errors */ }
        }
        return Task.CompletedTask;
    }

    private async Task WriteTestClass(string source)
    {
        await File.WriteAllTextAsync(_sourceFilePath, source);
    }

    [Fact]
    public async Task EditMode_SingleOccurrence_ReplacesText()
    {
        await WriteTestClass("""
            namespace Test;

            public class Calculator
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """);

        var result = await SolutionAnalyzerService.UpdateMethodAsync(
            _solutionPath,
            "Calculator",
            "Add",
            newSourceCode: null,
            oldText: "return a + b;",
            newText: "return a + b + 0;");

        Assert.True(result.Success, result.Error);

        var content = await File.ReadAllTextAsync(_sourceFilePath);
        Assert.Contains("return a + b + 0;", content);
        Assert.DoesNotContain("return a + b;", content);
    }

    [Fact]
    public async Task EditMode_Deletion_RemovesText()
    {
        await WriteTestClass("""
            namespace Test;

            public class Calculator
            {
                public int Add(int a, int b)
                {
                    // TODO: remove this comment
                    return a + b;
                }
            }
            """);

        var result = await SolutionAnalyzerService.UpdateMethodAsync(
            _solutionPath,
            "Calculator",
            "Add",
            newSourceCode: null,
            oldText: "// TODO: remove this comment",
            newText: "");

        Assert.True(result.Success, result.Error);

        var content = await File.ReadAllTextAsync(_sourceFilePath);
        Assert.DoesNotContain("TODO", content);
        Assert.Contains("return a + b;", content);
    }

    [Fact]
    public async Task EditMode_MultilineReplacement_Works()
    {
        await WriteTestClass("""
            namespace Test;

            public class Calculator
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """);

        var result = await SolutionAnalyzerService.UpdateMethodAsync(
            _solutionPath,
            "Calculator",
            "Add",
            newSourceCode: null,
            oldText: "return a + b;",
            newText: """
                var result = a + b;
                        return result;
                """);

        Assert.True(result.Success, result.Error);

        var content = await File.ReadAllTextAsync(_sourceFilePath);
        Assert.Contains("var result = a + b;", content);
        Assert.Contains("return result;", content);
    }

    [Fact]
    public async Task EditMode_OldTextNotFound_ReturnsError()
    {
        await WriteTestClass("""
            namespace Test;

            public class Calculator
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """);

        var result = await SolutionAnalyzerService.UpdateMethodAsync(
            _solutionPath,
            "Calculator",
            "Add",
            newSourceCode: null,
            oldText: "this text does not exist",
            newText: "replacement");

        Assert.False(result.Success);
        Assert.Contains("oldText not found", result.Error);
    }

    [Fact]
    public async Task EditMode_MultipleOccurrences_WithoutReplaceAll_ReturnsError()
    {
        await WriteTestClass("""
            namespace Test;

            public class Calculator
            {
                public int Add(int a, int b)
                {
                    var x = a + b;
                    var y = a + b;
                    return x + y;
                }
            }
            """);

        var result = await SolutionAnalyzerService.UpdateMethodAsync(
            _solutionPath,
            "Calculator",
            "Add",
            newSourceCode: null,
            oldText: "a + b",
            newText: "a - b");

        Assert.False(result.Success);
        Assert.Contains("2 times", result.Error);
        Assert.Contains("replaceAll", result.Error);
    }

    [Fact]
    public async Task EditMode_MultipleOccurrences_WithReplaceAll_ReplacesAll()
    {
        await WriteTestClass("""
            namespace Test;

            public class Calculator
            {
                public int Add(int a, int b)
                {
                    var x = a + b;
                    var y = a + b;
                    return x + y;
                }
            }
            """);

        var result = await SolutionAnalyzerService.UpdateMethodAsync(
            _solutionPath,
            "Calculator",
            "Add",
            newSourceCode: null,
            oldText: "a + b",
            newText: "a - b",
            replaceAll: true);

        Assert.True(result.Success, result.Error);

        var content = await File.ReadAllTextAsync(_sourceFilePath);
        Assert.DoesNotContain("a + b", content);
        // Both occurrences replaced
        var count = content.Split("a - b").Length - 1;
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task EditMode_PreservesMethodSignature()
    {
        await WriteTestClass("""
            namespace Test;

            public class Calculator
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """);

        var result = await SolutionAnalyzerService.UpdateMethodAsync(
            _solutionPath,
            "Calculator",
            "Add",
            newSourceCode: null,
            oldText: "return a + b;",
            newText: "return a + b + 1;");

        Assert.True(result.Success, result.Error);
        Assert.Equal("int Add(int a, int b)", result.OldSignature);

        var content = await File.ReadAllTextAsync(_sourceFilePath);
        Assert.Contains("public int Add(int a, int b)", content);
    }

    [Fact]
    public async Task EditMode_MethodNotFound_ReturnsError()
    {
        await WriteTestClass("""
            namespace Test;

            public class Calculator
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """);

        var result = await SolutionAnalyzerService.UpdateMethodAsync(
            _solutionPath,
            "Calculator",
            "NonExistent",
            newSourceCode: null,
            oldText: "something",
            newText: "other");

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
    }
}
