namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Gets diagnostics from solution compilation.
    /// Delegates to DiagnosticsService.
    /// </summary>
    public async Task<GetDiagnosticsResult> GetDiagnosticsAsync(
        string solutionPath,
        string? diagnosticId = null,
        string? severityFilter = null,
        string? projectFilter = null,
        int maxResults = 100,
        int offset = 0)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new GetDiagnosticsResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        using var workspace = CreateWorkspace();

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Delegate to DiagnosticsService
            var diagnosticsService = new Services.DiagnosticsService();
            return await diagnosticsService.GetDiagnosticsAsync(solution, diagnosticId, severityFilter, projectFilter, maxResults, offset);
        }
        catch (Exception ex)
        {
            return new GetDiagnosticsResult
            {
                Success = false,
                Error = $"Failed to get diagnostics: {ex.Message}"
            };
        }
    }
}
