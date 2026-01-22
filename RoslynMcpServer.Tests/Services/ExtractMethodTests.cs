namespace RoslynMcpServer.Tests.Services;

/// <summary>
/// Tests for the roslyn_extract_method tool.
/// Issue: #77
/// </summary>
public class ExtractMethodTests : IAsyncLifetime
{
    private string _tempDir = "";
    private string _solutionPath = "";
    private string _projectPath = "";
    private string _sourceFilePath = "";
    private SolutionAnalyzerService _service = null!;

    public async Task InitializeAsync()
    {
        _service = new SolutionAnalyzerService();

        // Create a temporary solution with a test class
        _tempDir = Path.Combine(Path.GetTempPath(), $"ExtractMethodTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _solutionPath = Path.Combine(_tempDir, "Test.sln");
        _projectPath = Path.Combine(_tempDir, "Test.csproj");
        _sourceFilePath = Path.Combine(_tempDir, "Calculator.cs");

        // Create solution file
        await File.WriteAllTextAsync(_solutionPath, """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Test", "Test.csproj", "{12345678-1234-1234-1234-123456789012}"
            EndProject
            """);

        // Create project file
        await File.WriteAllTextAsync(_projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        await Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        // Cleanup temp directory
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); }
            catch { /* Ignore cleanup errors */ }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Tests extracting simple statements with no parameters or return values.
    /// </summary>
    [Fact]
    public async Task ExtractMethodAsync_SimpleStatements_ExtractsVoidMethod()
    {
        // Arrange
        await File.WriteAllTextAsync(_sourceFilePath, """
            namespace Test;

            public class Calculator
            {
                public void Process()
                {
                    var x = 1;
                    Console.WriteLine("Start");
                    Console.WriteLine("Processing...");
                    Console.WriteLine("End");
                    var y = 2;
                }
            }
            """);

        // Act - extract lines 8-9 (the two middle Console.WriteLine statements)
        var result = await _service.ExtractMethodAsync(
            _solutionPath,
            _sourceFilePath,
            startLine: 8,
            endLine: 9,
            methodName: "LogProgress");

        // Assert
        Assert.True(result.Success, result.Error);
        Assert.Equal("LogProgress", result.ExtractedMethod?.Name);
        Assert.Contains("void", result.Analysis?.ReturnType);

        // Verify the new method exists and call site is correct
        var content = await File.ReadAllTextAsync(_sourceFilePath);
        Assert.Contains("LogProgress()", content);
        Assert.Contains("private", content);
    }

    /// <summary>
    /// Tests extracting code that uses external variables (should become parameters).
    /// </summary>
    [Fact]
    public async Task ExtractMethodAsync_WithInputVariables_CreatesParameters()
    {
        // Arrange
        await File.WriteAllTextAsync(_sourceFilePath, """
            namespace Test;

            public class Calculator
            {
                public void Calculate()
                {
                    int a = 5;
                    int b = 10;
                    var sum = a + b;
                    var product = a * b;
                    Console.WriteLine(sum);
                }
            }
            """);

        // Act - extract lines 9-10 (sum and product calculations)
        var result = await _service.ExtractMethodAsync(
            _solutionPath,
            _sourceFilePath,
            startLine: 9,
            endLine: 10,
            methodName: "ComputeValues");

        // Assert
        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Analysis?.InputVariables);
        Assert.Contains("a", result.Analysis.InputVariables);
        Assert.Contains("b", result.Analysis.InputVariables);
    }

    /// <summary>
    /// Tests extracting code that assigns a variable used later (should return it).
    /// </summary>
    [Fact]
    public async Task ExtractMethodAsync_WithOutputVariable_ReturnsValue()
    {
        // Arrange
        await File.WriteAllTextAsync(_sourceFilePath, """
            namespace Test;

            public class Calculator
            {
                public void Calculate()
                {
                    int a = 5;
                    int b = 10;
                    var result = a + b;
                    Console.WriteLine(result);
                }
            }
            """);

        // Act - extract line 9 (result assignment)
        var result = await _service.ExtractMethodAsync(
            _solutionPath,
            _sourceFilePath,
            startLine: 9,
            endLine: 9,
            methodName: "ComputeSum");

        // Assert
        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Analysis?.OutputVariables);
        Assert.Contains("result", result.Analysis.OutputVariables);
        Assert.Contains("int", result.Analysis?.ReturnType);
    }

    /// <summary>
    /// Tests that extracting code outside a method fails.
    /// </summary>
    [Fact]
    public async Task ExtractMethodAsync_OutsideMethod_ReturnsError()
    {
        // Arrange
        await File.WriteAllTextAsync(_sourceFilePath, """
            namespace Test;

            public class Calculator
            {
                private int _field = 5;

                public void Process() { }
            }
            """);

        // Act - try to extract the field declaration
        var result = await _service.ExtractMethodAsync(
            _solutionPath,
            _sourceFilePath,
            startLine: 5,
            endLine: 5,
            methodName: "ExtractedMethod");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("inside a method", result.Error);
    }

    /// <summary>
    /// Tests that invalid line range returns error.
    /// </summary>
    [Fact]
    public async Task ExtractMethodAsync_InvalidLineRange_ReturnsError()
    {
        // Arrange
        await File.WriteAllTextAsync(_sourceFilePath, """
            namespace Test;

            public class Calculator
            {
                public void Process()
                {
                    Console.WriteLine("Hello");
                }
            }
            """);

        // Act
        var result = await _service.ExtractMethodAsync(
            _solutionPath,
            _sourceFilePath,
            startLine: 10,
            endLine: 5,
            methodName: "ExtractedMethod");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Invalid line range", result.Error);
    }

    /// <summary>
    /// Tests that missing method name returns error.
    /// </summary>
    [Fact]
    public async Task ExtractMethodAsync_MissingMethodName_ReturnsError()
    {
        // Arrange
        await File.WriteAllTextAsync(_sourceFilePath, """
            namespace Test;

            public class Calculator
            {
                public void Process()
                {
                    Console.WriteLine("Hello");
                }
            }
            """);

        // Act
        var result = await _service.ExtractMethodAsync(
            _solutionPath,
            _sourceFilePath,
            startLine: 7,
            endLine: 7,
            methodName: "");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Method name is required", result.Error);
    }

    /// <summary>
    /// Tests extracting with explicit accessibility modifier.
    /// </summary>
    [Fact]
    public async Task ExtractMethodAsync_WithAccessibility_UsesSpecifiedModifier()
    {
        // Arrange
        await File.WriteAllTextAsync(_sourceFilePath, """
            namespace Test;

            public class Calculator
            {
                public void Process()
                {
                    Console.WriteLine("Hello");
                    Console.WriteLine("World");
                }
            }
            """);

        // Act
        var result = await _service.ExtractMethodAsync(
            _solutionPath,
            _sourceFilePath,
            startLine: 7,
            endLine: 8,
            methodName: "SayHello",
            accessibility: "internal");

        // Assert
        Assert.True(result.Success, result.Error);
        Assert.Equal("internal", result.ExtractedMethod?.Accessibility);

        var content = await File.ReadAllTextAsync(_sourceFilePath);
        Assert.Contains("internal", content);
    }

    /// <summary>
    /// Tests that file not found returns error.
    /// </summary>
    [Fact]
    public async Task ExtractMethodAsync_FileNotFound_ReturnsError()
    {
        // Act
        var result = await _service.ExtractMethodAsync(
            _solutionPath,
            Path.Combine(_tempDir, "NonExistent.cs"),
            startLine: 1,
            endLine: 1,
            methodName: "ExtractedMethod");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
    }
}
