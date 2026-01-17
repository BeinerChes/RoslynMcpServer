using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Gets diagnostics from solution compilation.
    /// If diagnosticId is null, returns summary (counts by ID).
    /// If diagnosticId is specified, returns detailed entries for that ID.
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

        using var workspace = MSBuildWorkspace.Create();

        workspace.WorkspaceFailed += (sender, args) =>
        {
            Console.Error.WriteLine($"Workspace warning: {args.Diagnostic.Message}");
        };

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            var allDiagnostics = new List<(Diagnostic diagnostic, string projectName)>();

            // Compile each project and collect diagnostics
            foreach (var project in solution.Projects)
            {
                // Apply project filter if specified
                if (!string.IsNullOrEmpty(projectFilter))
                {
                    if (!project.Name.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                Console.Error.WriteLine($"Compiling project: {project.Name}");
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                var diagnostics = compilation.GetDiagnostics()
                    .Where(d => d.Location.IsInSource);

                // Apply severity filter
                if (!string.IsNullOrEmpty(severityFilter))
                {
                    diagnostics = severityFilter.ToLowerInvariant() switch
                    {
                        "error" => diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                        "warning" => diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning),
                        "info" => diagnostics.Where(d => d.Severity == DiagnosticSeverity.Info),
                        _ => diagnostics
                    };
                }

                foreach (var diag in diagnostics)
                {
                    allDiagnostics.Add((diag, project.Name));
                }
            }

            // Count totals
            int totalErrors = allDiagnostics.Count(d => d.diagnostic.Severity == DiagnosticSeverity.Error);
            int totalWarnings = allDiagnostics.Count(d => d.diagnostic.Severity == DiagnosticSeverity.Warning);
            int totalInfo = allDiagnostics.Count(d => d.diagnostic.Severity == DiagnosticSeverity.Info);

            Console.Error.WriteLine($"Found {totalErrors} errors, {totalWarnings} warnings, {totalInfo} info");

            // If no specific diagnostic ID, return summary
            if (string.IsNullOrEmpty(diagnosticId))
            {
                var summary = allDiagnostics
                    .GroupBy(d => d.diagnostic.Id)
                    .Select(g =>
                    {
                        var first = g.First().diagnostic;
                        var lineSpan = first.Location.GetLineSpan();
                        return new DiagnosticSummary
                        {
                            Id = g.Key,
                            Severity = first.Severity.ToString(),
                            Title = first.Descriptor.Title.ToString(),
                            Count = g.Count(),
                            ExampleFile = Path.GetFileName(lineSpan.Path),
                            ExampleLine = lineSpan.StartLinePosition.Line + 1
                        };
                    })
                    .OrderByDescending(s => s.Count)
                    .ThenBy(s => s.Id)
                    .ToList();

                return new GetDiagnosticsResult
                {
                    Success = true,
                    SolutionPath = solutionPath,
                    TotalErrors = totalErrors,
                    TotalWarnings = totalWarnings,
                    TotalInfo = totalInfo,
                    Summary = summary
                };
            }

            // Detail mode: return entries for specific diagnostic ID
            var matchingDiagnostics = allDiagnostics
                .Where(d => d.diagnostic.Id.Equals(diagnosticId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var entries = matchingDiagnostics
                .Skip(offset)
                .Take(maxResults)
                .Select(d => CreateDiagnosticEntry(d.diagnostic, d.projectName, solution))
                .ToList();

            return new GetDiagnosticsResult
            {
                Success = true,
                SolutionPath = solutionPath,
                TotalErrors = totalErrors,
                TotalWarnings = totalWarnings,
                TotalInfo = totalInfo,
                Entries = entries,
                ReturnedCount = entries.Count,
                TotalMatchingEntries = matchingDiagnostics.Count
            };
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

    private static DiagnosticEntry CreateDiagnosticEntry(
        Diagnostic diagnostic,
        string projectName,
        Solution solution)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();

        // Try to find containing type and method
        string? containingType = null;
        string? containingMethod = null;

        var syntaxTree = diagnostic.Location.SourceTree;
        if (syntaxTree != null)
        {
            var root = syntaxTree.GetRoot();
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            // Walk up to find containing type and method
            var current = node;
            while (current != null)
            {
                if (containingMethod == null && IsMethodDeclaration(current))
                {
                    containingMethod = GetMethodName(current);
                }
                if (containingType == null && IsTypeDeclaration(current))
                {
                    containingType = GetTypeName(current);
                    break; // Found both, stop
                }
                current = current.Parent;
            }
        }

        return new DiagnosticEntry
        {
            Id = diagnostic.Id,
            Severity = diagnostic.Severity.ToString(),
            Message = diagnostic.GetMessage(),
            FilePath = lineSpan.Path,
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1,
            ProjectName = projectName,
            ContainingType = containingType,
            ContainingMethod = containingMethod
        };
    }

    private static bool IsMethodDeclaration(SyntaxNode node)
    {
        var typeName = node.GetType().Name;
        return typeName is "MethodDeclarationSyntax" or "ConstructorDeclarationSyntax"
            or "PropertyDeclarationSyntax" or "LocalFunctionStatementSyntax";
    }

    private static bool IsTypeDeclaration(SyntaxNode node)
    {
        var typeName = node.GetType().Name;
        return typeName is "ClassDeclarationSyntax" or "StructDeclarationSyntax"
            or "InterfaceDeclarationSyntax" or "RecordDeclarationSyntax";
    }

    private static string GetMethodName(SyntaxNode node)
    {
        // Use reflection to get Identifier property
        var identifierProp = node.GetType().GetProperty("Identifier");
        if (identifierProp != null)
        {
            var identifier = identifierProp.GetValue(node);
            return identifier?.ToString() ?? "unknown";
        }
        return "unknown";
    }

    private static string GetTypeName(SyntaxNode node)
    {
        var identifierProp = node.GetType().GetProperty("Identifier");
        if (identifierProp != null)
        {
            var identifier = identifierProp.GetValue(node);
            return identifier?.ToString() ?? "unknown";
        }
        return "unknown";
    }
}
