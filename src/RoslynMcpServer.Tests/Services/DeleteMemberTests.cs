namespace RoslynMcpServer.Tests.Services;

/// <summary>
/// Tests for the DeleteMember tool.
/// Issue: #39
/// </summary>
public class DeleteMemberTests : IAsyncLifetime
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
        _tempDir = Path.Combine(Path.GetTempPath(), $"DeleteMemberTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _solutionPath = Path.Combine(_tempDir, "Test.sln");
        _projectPath = Path.Combine(_tempDir, "Test.csproj");
        _sourceFilePath = Path.Combine(_tempDir, "TestClass.cs");

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

        // Create source file with various members
        await File.WriteAllTextAsync(_sourceFilePath, """
            namespace Test;

            /// <summary>
            /// Test class with various members.
            /// </summary>
            public class TestClass
            {
                private readonly string _field1;
                private int _field2;

                /// <summary>
                /// A property to delete.
                /// </summary>
                [Obsolete]
                public string PropertyToDelete { get; set; } = "";

                public string KeepProperty { get; set; } = "";

                /// <summary>
                /// A method to delete.
                /// </summary>
                public void MethodToDelete()
                {
                    Console.WriteLine("Delete me");
                }

                public void KeepMethod()
                {
                    Console.WriteLine("Keep me");
                }

                /// <summary>
                /// Overloaded method - first overload.
                /// </summary>
                public void OverloadedMethod(string value)
                {
                    Console.WriteLine(value);
                }

                /// <summary>
                /// Overloaded method - second overload.
                /// </summary>
                public void OverloadedMethod(int value)
                {
                    Console.WriteLine(value);
                }
            }
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
    /// Tests deleting a method by name.
    /// </summary>
    [Fact]
    public async Task DeleteMemberAsync_DeleteMethod_RemovesMethod()
    {
        // Act
        var result = await SolutionAnalyzerService.DeleteMemberAsync(
            _solutionPath,
            "TestClass",
            "MethodToDelete");

        // Assert
        Assert.True(result.Success, result.Error);
        Assert.Equal("Method", result.MemberKind);
        Assert.Equal("MethodToDelete", result.MemberName);

        // Verify the method is gone
        var content = await File.ReadAllTextAsync(_sourceFilePath);
        Assert.DoesNotContain("MethodToDelete", content);
        Assert.Contains("KeepMethod", content);
    }

    /// <summary>
    /// Tests deleting a property by name.
    /// </summary>
    [Fact]
    public async Task DeleteMemberAsync_DeleteProperty_RemovesPropertyAndAttributes()
    {
        // Act
        var result = await SolutionAnalyzerService.DeleteMemberAsync(
            _solutionPath,
            "TestClass",
            "PropertyToDelete");

        // Assert
        Assert.True(result.Success, result.Error);
        Assert.Equal("Property", result.MemberKind);

        // Verify the property and its attribute are gone
        var content = await File.ReadAllTextAsync(_sourceFilePath);
        Assert.DoesNotContain("PropertyToDelete", content);
        Assert.DoesNotContain("[Obsolete]", content);
        Assert.Contains("KeepProperty", content);
    }

    /// <summary>
    /// Tests deleting a field by name.
    /// </summary>
    [Fact]
    public async Task DeleteMemberAsync_DeleteField_RemovesField()
    {
        // Act
        var result = await SolutionAnalyzerService.DeleteMemberAsync(
            _solutionPath,
            "TestClass",
            "_field2");

        // Assert
        Assert.True(result.Success, result.Error);
        Assert.Equal("Field", result.MemberKind);

        // Verify the field is gone
        var content = await File.ReadAllTextAsync(_sourceFilePath);
        Assert.DoesNotContain("_field2", content);
        Assert.Contains("_field1", content);
    }

    /// <summary>
    /// Tests deleting a method overload using parameterTypes.
    /// </summary>
    [Fact]
    public async Task DeleteMemberAsync_DeleteMethodOverload_RemovesCorrectOverload()
    {
        // Act - delete the int overload
        var result = await SolutionAnalyzerService.DeleteMemberAsync(
            _solutionPath,
            "TestClass",
            "OverloadedMethod",
            parameterTypes: "int");

        // Assert
        Assert.True(result.Success, result.Error);

        // Verify only the int overload is gone
        var content = await File.ReadAllTextAsync(_sourceFilePath);
        Assert.Contains("OverloadedMethod(string value)", content);
        Assert.DoesNotContain("OverloadedMethod(int value)", content);
    }

    /// <summary>
    /// Tests that deleting non-existent member returns error.
    /// </summary>
    [Fact]
    public async Task DeleteMemberAsync_MemberNotFound_ReturnsError()
    {
        // Act
        var result = await SolutionAnalyzerService.DeleteMemberAsync(
            _solutionPath,
            "TestClass",
            "NonExistentMethod");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
    }

    /// <summary>
    /// Tests that deleting from non-existent type returns error.
    /// </summary>
    [Fact]
    public async Task DeleteMemberAsync_TypeNotFound_ReturnsError()
    {
        // Act
        var result = await SolutionAnalyzerService.DeleteMemberAsync(
            _solutionPath,
            "NonExistentClass",
            "SomeMethod");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Type not found", result.Error);
    }
}
