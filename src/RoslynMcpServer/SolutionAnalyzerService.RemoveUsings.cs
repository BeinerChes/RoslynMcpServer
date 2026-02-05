using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Removes unnecessary using directives from files in the solution.
    /// Uses CS8019 diagnostics to identify which usings are unnecessary.
    /// </summary>
    public static async Task<RemoveUnnecessaryUsingsResult> RemoveUnnecessaryUsingsAsync(
        string solutionPath,
        string? projectFilter = null,
        string? fileFilter = null)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new RemoveUnnecessaryUsingsResult
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

            var filesModified = new List<string>();
            int totalUsingsRemoved = 0;

            foreach (var project in solution.Projects)
            {
                if (!string.IsNullOrEmpty(projectFilter) &&
                    !project.Name.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                // Get CS8019 diagnostics (unnecessary using directive)
                var cs8019Diagnostics = compilation.GetDiagnostics()
                    .Where(d => d.Id == "CS8019" && d.Location.IsInSource)
                    .ToList();

                // Group by file
                var diagnosticsByFile = cs8019Diagnostics
                    .GroupBy(d => d.Location.SourceTree?.FilePath)
                    .Where(g => g.Key != null);

                foreach (var fileGroup in diagnosticsByFile)
                {
                    var filePath = fileGroup.Key!;

                    // Apply file filter
                    if (!string.IsNullOrEmpty(fileFilter))
                    {
                        var fileName = Path.GetFileName(filePath);
                        if (!fileName.Contains(fileFilter, StringComparison.OrdinalIgnoreCase) &&
                            !filePath.Contains(fileFilter, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    // Skip generated files (obj folder)
                    if (filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                        filePath.Contains($"{Path.AltDirectorySeparatorChar}obj{Path.AltDirectorySeparatorChar}"))
                        continue;

                    var syntaxTree = compilation.SyntaxTrees
                        .FirstOrDefault(t => t.FilePath == filePath);
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync();
                    var diagnosticSpans = fileGroup
                        .Select(d => d.Location.SourceSpan)
                        .ToHashSet();

                    // Find all using directives that have CS8019 diagnostics
                    var usingsToRemove = root.DescendantNodes()
                        .OfType<UsingDirectiveSyntax>()
                        .Where(u => diagnosticSpans.Any(span =>
                            u.Span.Contains(span) || span.Contains(u.Span) ||
                            u.Span.IntersectsWith(span)))
                        .ToList();

                    if (usingsToRemove.Count == 0) continue;

                    totalUsingsRemoved += usingsToRemove.Count;

                    // Remove the using directives
                    var newRoot = root.RemoveNodes(usingsToRemove, SyntaxRemoveOptions.KeepLeadingTrivia);
                    if (newRoot != null)
                    {
                        // Clean up any double blank lines that may result
                        var newSource = newRoot.ToFullString();
                        newSource = CleanupExtraBlankLines(newSource);

                        await File.WriteAllTextAsync(filePath, newSource);
                        filesModified.Add(filePath);
                        Console.Error.WriteLine($"Removed {usingsToRemove.Count} usings from: {filePath}");
                    }
                }
            }

            return new RemoveUnnecessaryUsingsResult
            {
                Success = true,
                SolutionPath = solutionPath,
                TotalUsingsRemoved = totalUsingsRemoved,
                FilesModified = filesModified.Count,
                ModifiedFiles = filesModified
            };
        }
        catch (Exception ex)
        {
            return new RemoveUnnecessaryUsingsResult
            {
                Success = false,
                Error = $"Failed to remove unnecessary usings: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Removes consecutive blank lines, keeping at most one blank line.
    /// </summary>
    private static string CleanupExtraBlankLines(string source)
    {
        var lines = source.Split('\n');
        var result = new List<string>();
        bool previousWasBlank = false;

        foreach (var line in lines)
        {
            bool isBlank = string.IsNullOrWhiteSpace(line);

            if (isBlank && previousWasBlank)
            {
                // Skip consecutive blank lines
                continue;
            }

            result.Add(line);
            previousWasBlank = isBlank;
        }

        return string.Join('\n', result);
    }
}

/// <summary>
/// Result of removing unnecessary usings.
/// </summary>
public class RemoveUnnecessaryUsingsResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? SolutionPath { get; init; }
    public int TotalUsingsRemoved { get; init; }
    public int FilesModified { get; init; }
    public List<string> ModifiedFiles { get; init; } = [];
}
