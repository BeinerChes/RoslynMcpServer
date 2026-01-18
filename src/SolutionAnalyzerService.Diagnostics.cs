using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
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

        // Run design-time builds for WPF projects to generate *.g.cs files
        RunDesignTimeBuildsForSolution(solutionPath);

        using var workspace = CreateWorkspace();

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Enhance solution with WPF/XAML generated files (fallback for any missed files)
            solution = EnhanceSolutionWithGeneratedFiles(solution);

            // Get the set of diagnostic IDs that have code fixes available
            var fixableIds = GetFixableDiagnosticIds();
            Console.Error.WriteLine($"Loaded {fixableIds.Count} fixable diagnostic IDs");

            // Load .NET analyzers
            var netAnalyzers = AnalyzerLoader.GetNetAnalyzers();
            Console.Error.WriteLine($"Using {netAnalyzers.Length} .NET analyzers");

            // Track: diagnostic, projectName, isSuppressedByProject, suppressionReason
            var allDiagnostics = new List<(Diagnostic diagnostic, string projectName, bool isSuppressed, string? suppressionReason)>();

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

                // Get project's original diagnostic options (before we override)
                var projectDiagnosticOptions = compilation.Options.SpecificDiagnosticOptions;

                // Get compiler diagnostics
                var compilerDiagnostics = compilation.GetDiagnostics()
                    .Where(d => d.Location.IsInSource);

                foreach (var diag in FilterBySeverity(compilerDiagnostics, severityFilter))
                {
                    var (isSuppressed, reason) = GetSuppressionInfo(diag, projectDiagnosticOptions);
                    allDiagnostics.Add((diag, project.Name, isSuppressed, reason));
                }

                // Run .NET analyzers if available
                if (netAnalyzers.Length > 0)
                {
                    var analyzerDiagnostics = await RunAnalyzersAsync(
                        compilation, netAnalyzers, project.AnalyzerOptions, projectDiagnosticOptions);

                    foreach (var diag in FilterBySeverity(analyzerDiagnostics, severityFilter))
                    {
                        var (isSuppressed, reason) = GetSuppressionInfo(diag, projectDiagnosticOptions);
                        allDiagnostics.Add((diag, project.Name, isSuppressed, reason));
                    }
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
                        var first = g.First();
                        return new DiagnosticSummary
                        {
                            Id = g.Key,
                            Severity = first.diagnostic.Severity.ToString(),
                            Title = first.diagnostic.Descriptor.Title.ToString(),
                            Count = g.Count(),
                            SuppressedCount = g.Count(x => x.isSuppressed),
                            FixAvailable = fixableIds.Contains(g.Key)
                        };
                    })
                    .OrderByDescending(s => s.Count)
                    .ThenBy(s => s.Id)
                    .ToList();

                return new GetDiagnosticsResult
                {
                    Success = true,
                    TotalErrors = totalErrors,
                    TotalWarnings = totalWarnings,
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
                .Select(d => CreateDiagnosticEntry(d.diagnostic, d.projectName, d.suppressionReason))
                .ToList();

            return new GetDiagnosticsResult
            {
                Success = true,
                Entries = entries,
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
        string? suppressionReason)
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
            Message = diagnostic.GetMessage(),
            FilePath = lineSpan.Path,
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1,
            ProjectName = projectName,
            ContainingType = containingType,
            ContainingMethod = containingMethod,
            Suppressed = suppressionReason
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

    private static IEnumerable<Diagnostic> FilterBySeverity(
        IEnumerable<Diagnostic> diagnostics, string? severityFilter)
    {
        if (string.IsNullOrEmpty(severityFilter))
            return diagnostics;

        return severityFilter.ToLowerInvariant() switch
        {
            "error" => diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
            "warning" => diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning),
            "info" => diagnostics.Where(d => d.Severity == DiagnosticSeverity.Info),
            _ => diagnostics
        };
    }

    private static async Task<IEnumerable<Diagnostic>> RunAnalyzersAsync(
        Compilation compilation,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        AnalyzerOptions? analyzerOptions,
        ImmutableDictionary<string, ReportDiagnostic> projectDiagnosticOptions)
    {
        try
        {
            // Get all supported diagnostic IDs from analyzers and enable them at Warning level
            var diagnosticOptions = new Dictionary<string, ReportDiagnostic>();
            foreach (var analyzer in analyzers)
            {
                foreach (var descriptor in analyzer.SupportedDiagnostics)
                {
                    // Enable all CA* rules at Warning level (they're often disabled by default)
                    if (descriptor.Id.StartsWith("CA", StringComparison.Ordinal))
                    {
                        diagnosticOptions[descriptor.Id] = ReportDiagnostic.Warn;
                    }
                }
            }

            Console.Error.WriteLine($"Enabled {diagnosticOptions.Count} CA* diagnostic rules");

            // Apply the diagnostic options to the compilation
            var modifiedCompilation = compilation.WithOptions(
                compilation.Options.WithSpecificDiagnosticOptions(diagnosticOptions));

            // Create compilation with analyzers - enable suppressed diagnostics reporting
            var options = new CompilationWithAnalyzersOptions(
                analyzerOptions ?? new AnalyzerOptions([]),
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: true);  // Report suppressed diagnostics too

            var compilationWithAnalyzers = modifiedCompilation.WithAnalyzers(analyzers, options);

            // Get analyzer diagnostics (excludes compiler diagnostics)
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

            // Only return diagnostics that are in source files
            return diagnostics.Where(d => d.Location.IsInSource);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error running analyzers: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Determines if a diagnostic is suppressed by project settings.
    /// </summary>
    private static (bool isSuppressed, string? reason) GetSuppressionInfo(
        Diagnostic diagnostic,
        ImmutableDictionary<string, ReportDiagnostic> projectDiagnosticOptions)
    {
        // Check if suppressed by pragma or attribute
        if (diagnostic.IsSuppressed)
        {
            return (true, "pragma/attribute");
        }

        // Check if the project has this diagnostic set to None or Suppress
        if (projectDiagnosticOptions.TryGetValue(diagnostic.Id, out var projectSeverity))
        {
            if (projectSeverity == ReportDiagnostic.Suppress ||
                projectSeverity == ReportDiagnostic.Hidden)
            {
                return (true, "project settings");
            }
        }

        // For CA* rules, check if the project didn't explicitly enable them
        // (they're disabled by default in many projects)
        if (diagnostic.Id.StartsWith("CA", StringComparison.Ordinal))
        {
            // If not in project options, it means project uses default (often None for CA rules)
            if (!projectDiagnosticOptions.ContainsKey(diagnostic.Id))
            {
                // Check the descriptor's default severity
                if (diagnostic.Descriptor.DefaultSeverity == DiagnosticSeverity.Hidden ||
                    !diagnostic.Descriptor.IsEnabledByDefault)
                {
                    return (true, "disabled by default");
                }
            }
        }

        return (false, null);
    }
}
