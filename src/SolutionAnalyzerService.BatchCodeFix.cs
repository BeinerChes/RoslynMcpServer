namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Batch applies code fixes for all diagnostics matching the specified criteria.
    /// Delegates to CodeFixService.
    /// </summary>
    public static async Task<BatchApplyCodeFixResult> BatchApplyCodeFixAsync(
        string solutionPath,
        string diagnosticId,
        string? projectFilter = null,
        string? fileFilter = null,
        int maxFixes = 100,
        bool preview = false)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new BatchApplyCodeFixResult
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
            return await Services.CodeFixService.BatchApplyCodeFixAsync(solution, solutionPath, diagnosticId, projectFilter, fileFilter, maxFixes, preview);
        }
        catch (Exception ex)
        {
            return new BatchApplyCodeFixResult
            {
                Success = false,
                Error = $"Failed to batch apply code fixes: {ex.Message}",
                SolutionPath = solutionPath,
                DiagnosticId = diagnosticId
            };
        }
    }
}
