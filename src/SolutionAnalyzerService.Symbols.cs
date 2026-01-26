namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Finds symbols in a solution by name pattern.
    /// Delegates to SymbolSearchService.
    /// </summary>
    public async Task<FindSymbolResult> SearchSymbolsAsync(
        string solutionPath,
        string pattern,
        SymbolKindFilter kindFilter = SymbolKindFilter.All,
        MatchType matchType = MatchType.Contains,
        int maxResults = 100,
        bool compact = true)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new FindSymbolResult
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

            // Delegate to SymbolSearchService
            var symbolSearchService = new Services.SymbolSearchService();
            var result = await symbolSearchService.SearchSymbolsAsync(solution, solutionPath, pattern, kindFilter, matchType, maxResults, compact);

            return result;
        }
        catch (Exception ex)
        {
            return new FindSymbolResult
            {
                Success = false,
                Error = $"Failed to find symbols: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Gets the qualified name of a symbol at the given file position.
    /// Delegates to SymbolSearchService.
    /// </summary>
    public static async Task<string?> GetSymbolQualifiedNameAsync(
        string solutionPath,
        string filePath,
        int line,
        int column)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath) || !File.Exists(filePath))
            return null;

        using var workspace = CreateWorkspace();

        try
        {
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Delegate to SymbolSearchService
            var symbolSearchService = new Services.SymbolSearchService();
            return await Services.SymbolSearchService.GetSymbolQualifiedNameAsync(solution, workspace, filePath, line, column);
        }
        catch
        {
            return null;
        }
    }
}
