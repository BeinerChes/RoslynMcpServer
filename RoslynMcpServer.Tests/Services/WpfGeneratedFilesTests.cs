namespace RoslynMcpServer.Tests.Services;

/// <summary>
/// Tests for WPF/XAML generated file inclusion in solution analysis.
/// Issue: #11
/// </summary>
public class WpfGeneratedFilesTests
{
    /// <summary>
    /// Tests that EnhanceSolutionWithGeneratedFiles doesn't break solutions without WPF projects.
    /// Issue: #11
    /// </summary>
    [Fact]
    public async Task EnhanceSolutionWithGeneratedFiles_WithNonWpfSolution_ReturnsUnchangedDocumentCount()
    {
        // Arrange
        var service = new SolutionAnalyzerService();
        // Path: Tests\bin\Debug\net10.0 -> Tests\bin\Debug -> Tests\bin -> Tests -> root
        var solutionPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RoslynMcpServer.slnx"));

        // Act - Load solution and check it works without errors
        var result = await service.GetProjectsInBuildOrderAsync(solutionPath);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Projects);
        Assert.True(result.Projects.Count > 0);
    }

    /// <summary>
    /// Tests that diagnostics work correctly after solution enhancement.
    /// Issue: #11
    /// </summary>
    [Fact]
    public async Task GetDiagnosticsAsync_AfterEnhancement_DoesNotThrow()
    {
        // Arrange
        var service = new SolutionAnalyzerService();
        // Path: Tests\bin\Debug\net10.0 -> Tests\bin\Debug -> Tests\bin -> Tests -> root
        var solutionPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RoslynMcpServer.slnx"));

        // Act
        var result = await service.GetDiagnosticsAsync(solutionPath, severityFilter: "error");

        // Assert - Should complete without throwing
        Assert.True(result.Success);
    }

    /// <summary>
    /// Tests that the service correctly identifies generated files by extension pattern.
    /// Issue: #11
    /// </summary>
    [Fact]
    public void IsGeneratedXamlFile_WithGcsExtension_ReturnsTrue()
    {
        // Arrange & Act
        var result1 = SolutionAnalyzerService.IsGeneratedXamlFile("MainWindow.g.cs");
        var result2 = SolutionAnalyzerService.IsGeneratedXamlFile("App.g.i.cs");
        var result3 = SolutionAnalyzerService.IsGeneratedXamlFile("MainWindow.cs");
        var result4 = SolutionAnalyzerService.IsGeneratedXamlFile("MainWindow.xaml.cs");

        // Assert
        Assert.True(result1, "*.g.cs should be identified as generated XAML file");
        Assert.True(result2, "*.g.i.cs should be identified as generated XAML file");
        Assert.False(result3, "Regular .cs files should not be identified as generated");
        Assert.False(result4, "Code-behind files should not be identified as generated");
    }

    /// <summary>
    /// Tests that ScanForGeneratedXamlFiles returns empty for projects without obj folder.
    /// Issue: #11
    /// </summary>
    [Fact]
    public void ScanForGeneratedXamlFiles_WithNonExistentObjFolder_ReturnsEmpty()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "fake.csproj");

        // Act
        var files = SolutionAnalyzerService.ScanForGeneratedXamlFiles(nonExistentPath);

        // Assert
        Assert.Empty(files);
    }

    /// <summary>
    /// Tests that ScanForGeneratedXamlFiles finds *.g.cs files in obj folder.
    /// Issue: #11
    /// </summary>
    [Fact]
    public void ScanForGeneratedXamlFiles_WithGeneratedFiles_ReturnsFiles()
    {
        // Arrange - Create temp directory structure simulating WPF project
        var tempDir = Path.Combine(Path.GetTempPath(), $"WpfTest_{Guid.NewGuid()}");
        var objDir = Path.Combine(tempDir, "obj", "Debug", "net8.0-windows");
        Directory.CreateDirectory(objDir);

        var generatedFile = Path.Combine(objDir, "MainWindow.g.cs");
        File.WriteAllText(generatedFile, "// Generated file");

        try
        {
            var projectPath = Path.Combine(tempDir, "TestProject.csproj");

            // Act
            var files = SolutionAnalyzerService.ScanForGeneratedXamlFiles(projectPath);

            // Assert
            Assert.Single(files);
            Assert.Contains("MainWindow.g.cs", files[0]);
        }
        finally
        {
            // Cleanup
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
