namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Applies a code fix for a diagnostic at a specific location.
    /// Delegates to CodeFixService.
    /// </summary>
    public async Task<ApplyCodeFixResult> ApplyCodeFixAsync(
        string solutionPath,
        string filePath,
        int line,
        int column,
        string? diagnosticId = null,
        int? fixIndex = null,
        bool preview = false)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new ApplyCodeFixResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        // Run design-time builds for WPF projects to generate *.g.cs files
        RunDesignTimeBuildsForSolution(solutionPath);

        using var workspace = CreateWorkspace();

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Enhance solution with WPF/XAML generated files (fallback for any missed files)
            solution = EnhanceSolutionWithGeneratedFiles(solution);

            // Delegate to CodeFixService
            var codeFixService = new Services.CodeFixService();
            return await codeFixService.ApplyCodeFixAsync(solution, filePath, line, column, diagnosticId, fixIndex, preview);
        }
        catch (Exception ex)
        {
            return new ApplyCodeFixResult
            {
                Success = false,
                Error = $"Failed to apply code fix: {ex.Message}"
            };
        }
    }
}
