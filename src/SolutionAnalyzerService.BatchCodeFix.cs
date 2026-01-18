using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using System.Collections.Immutable;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Batch applies code fixes for all diagnostics matching the specified criteria.
    /// Loads solution once, applies all fixes in memory, then writes all changes to disk.
    /// </summary>
    public async Task<BatchApplyCodeFixResult> BatchApplyCodeFixAsync(
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

        if (string.IsNullOrWhiteSpace(diagnosticId))
        {
            return new BatchApplyCodeFixResult
            {
                Success = false,
                Error = "diagnosticId is required for batch fix operations"
            };
        }

        using var workspace = CreateWorkspace();

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Get code fix providers that can fix this diagnostic
            var providers = GetCodeFixProviders();
            var relevantProviders = providers
                .Where(p => p.FixableDiagnosticIds.Contains(diagnosticId))
                .ToList();

            if (relevantProviders.Count == 0)
            {
                return new BatchApplyCodeFixResult
                {
                    Success = false,
                    Error = $"No code fix provider available for diagnostic '{diagnosticId}'",
                    SolutionPath = solutionPath,
                    DiagnosticId = diagnosticId
                };
            }

            Console.Error.WriteLine($"Found {relevantProviders.Count} code fix provider(s) for {diagnosticId}");

            // Collect all matching diagnostics across the solution
            var allDiagnostics = new List<(Document document, Diagnostic diagnostic)>();

            // Check if we need to run .NET analyzers (for CA* diagnostics)
            var needAnalyzers = diagnosticId.StartsWith("CA", StringComparison.OrdinalIgnoreCase);
            ImmutableArray<DiagnosticAnalyzer> netAnalyzers = [];
            if (needAnalyzers)
            {
                netAnalyzers = AnalyzerLoader.GetNetAnalyzers();
                Console.Error.WriteLine($"Running {netAnalyzers.Length} .NET analyzers for batch fix...");
            }

            foreach (var project in solution.Projects)
            {
                // Apply project filter
                if (!string.IsNullOrEmpty(projectFilter) &&
                    !project.Name.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                // Get diagnostics - either from compiler or from analyzers
                IEnumerable<Diagnostic> projectDiagnostics;
                if (needAnalyzers && netAnalyzers.Length > 0)
                {
                    projectDiagnostics = await GetAnalyzerDiagnosticsForBatchAsync(
                        compilation, netAnalyzers, diagnosticId);
                }
                else
                {
                    projectDiagnostics = compilation.GetDiagnostics();
                }

                foreach (var diagnostic in projectDiagnostics)
                {
                    if (!diagnostic.Id.Equals(diagnosticId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!diagnostic.Location.IsInSource)
                        continue;

                    var syntaxTree = diagnostic.Location.SourceTree;
                    if (syntaxTree == null) continue;

                    // Apply file filter
                    if (!string.IsNullOrEmpty(fileFilter))
                    {
                        var fileName = Path.GetFileName(syntaxTree.FilePath);
                        if (!fileName.Contains(fileFilter, StringComparison.OrdinalIgnoreCase) &&
                            !syntaxTree.FilePath.Contains(fileFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    var documentId = solution.GetDocumentIdsWithFilePath(syntaxTree.FilePath).FirstOrDefault();
                    if (documentId == null) continue;

                    var document = solution.GetDocument(documentId);
                    if (document == null) continue;

                    allDiagnostics.Add((document, diagnostic));
                }
            }

            Console.Error.WriteLine($"Found {allDiagnostics.Count} diagnostics matching '{diagnosticId}'");

            if (allDiagnostics.Count == 0)
            {
                return new BatchApplyCodeFixResult
                {
                    Success = true,
                    SolutionPath = solutionPath,
                    DiagnosticId = diagnosticId,
                    TotalDiagnosticsFound = 0,
                    DiagnosticsWithFixes = 0,
                    FixesApplied = 0,
                    FixesFailed = 0,
                    FilesModified = 0,
                    IsPreview = preview
                };
            }

            // Limit the number of fixes
            var diagnosticsToFix = allDiagnostics.Take(maxFixes).ToList();
            var details = new List<BatchFixDetail>();
            var modifiedDocumentIds = new HashSet<DocumentId>();
            int fixesApplied = 0;
            int fixesFailed = 0;
            int diagnosticsWithFixes = 0;

            // Apply fixes one by one, updating the solution in memory
            foreach (var (originalDocument, diagnostic) in diagnosticsToFix)
            {
                try
                {
                    // Get the current document from the (possibly modified) solution
                    var currentDocument = solution.GetDocument(originalDocument.Id);
                    if (currentDocument == null)
                    {
                        details.Add(new BatchFixDetail
                        {
                            FilePath = originalDocument.FilePath ?? "unknown",
                            Line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
                            Column = diagnostic.Location.GetLineSpan().StartLinePosition.Character + 1,
                            DiagnosticMessage = diagnostic.GetMessage(),
                            FixTitle = "N/A",
                            Applied = false,
                            Error = "Document no longer exists in solution"
                        });
                        fixesFailed++;
                        continue;
                    }

                    // We need to re-locate the diagnostic in the current document
                    // since the document may have changed from previous fixes
                    var currentSemanticModel = await currentDocument.GetSemanticModelAsync();
                    if (currentSemanticModel == null)
                    {
                        fixesFailed++;
                        continue;
                    }

                    // Get diagnostics from current compilation (use analyzers for CA*)
                    List<Diagnostic> currentDiagnostics;
                    if (needAnalyzers && netAnalyzers.Length > 0)
                    {
                        var analyzerDiags = await GetAnalyzerDiagnosticsForBatchAsync(
                            currentSemanticModel.Compilation, netAnalyzers, diagnosticId);
                        currentDiagnostics = analyzerDiags
                            .Where(d => d.Location.SourceTree?.FilePath == currentDocument.FilePath)
                            .ToList();
                    }
                    else
                    {
                        currentDiagnostics = currentSemanticModel.Compilation.GetDiagnostics()
                            .Where(d => d.Id.Equals(diagnosticId, StringComparison.OrdinalIgnoreCase) &&
                                       d.Location.IsInSource &&
                                       d.Location.SourceTree?.FilePath == currentDocument.FilePath)
                            .ToList();
                    }

                    if (currentDiagnostics.Count == 0)
                    {
                        // Diagnostic may have been fixed by a previous fix in the same file
                        continue;
                    }

                    // Try to find a matching diagnostic at or near the original location
                    var originalLine = diagnostic.Location.GetLineSpan().StartLinePosition.Line;
                    var targetDiagnostic = currentDiagnostics
                        .OrderBy(d => Math.Abs(d.Location.GetLineSpan().StartLinePosition.Line - originalLine))
                        .First();

                    // Get code fix for this diagnostic
                    CodeAction? selectedAction = null;
                    foreach (var provider in relevantProviders)
                    {
                        var actions = new List<CodeAction>();
                        var context = new CodeFixContext(
                            currentDocument,
                            targetDiagnostic,
                            (action, _) => actions.Add(action),
                            CancellationToken.None);

                        await provider.RegisterCodeFixesAsync(context);

                        if (actions.Count > 0)
                        {
                            selectedAction = actions[0]; // Take the first fix
                            break;
                        }
                    }

                    if (selectedAction == null)
                    {
                        details.Add(new BatchFixDetail
                        {
                            FilePath = currentDocument.FilePath ?? "unknown",
                            Line = targetDiagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
                            Column = targetDiagnostic.Location.GetLineSpan().StartLinePosition.Character + 1,
                            DiagnosticMessage = targetDiagnostic.GetMessage(),
                            FixTitle = "N/A",
                            Applied = false,
                            Error = "No code fix registered"
                        });
                        fixesFailed++;
                        continue;
                    }

                    diagnosticsWithFixes++;

                    // Get operations from the code action
                    var operations = await selectedAction.GetOperationsAsync(CancellationToken.None);
                    var applyChangesOp = operations.OfType<ApplyChangesOperation>().FirstOrDefault();

                    if (applyChangesOp == null)
                    {
                        details.Add(new BatchFixDetail
                        {
                            FilePath = currentDocument.FilePath ?? "unknown",
                            Line = targetDiagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
                            Column = targetDiagnostic.Location.GetLineSpan().StartLinePosition.Character + 1,
                            DiagnosticMessage = targetDiagnostic.GetMessage(),
                            FixTitle = selectedAction.Title,
                            Applied = false,
                            Error = "Code fix does not produce applicable changes"
                        });
                        fixesFailed++;
                        continue;
                    }

                    // Track which documents changed
                    var changedSolution = applyChangesOp.ChangedSolution;
                    var changes = changedSolution.GetChanges(solution);
                    foreach (var projectChanges in changes.GetProjectChanges())
                    {
                        foreach (var changedDocId in projectChanges.GetChangedDocuments())
                        {
                            modifiedDocumentIds.Add(changedDocId);
                        }
                    }

                    // Update the solution for the next iteration
                    solution = changedSolution;

                    details.Add(new BatchFixDetail
                    {
                        FilePath = currentDocument.FilePath ?? "unknown",
                        Line = targetDiagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
                        Column = targetDiagnostic.Location.GetLineSpan().StartLinePosition.Character + 1,
                        DiagnosticMessage = targetDiagnostic.GetMessage(),
                        FixTitle = selectedAction.Title,
                        Applied = true
                    });
                    fixesApplied++;
                }
                catch (Exception ex)
                {
                    details.Add(new BatchFixDetail
                    {
                        FilePath = originalDocument.FilePath ?? "unknown",
                        Line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
                        Column = diagnostic.Location.GetLineSpan().StartLinePosition.Character + 1,
                        DiagnosticMessage = diagnostic.GetMessage(),
                        FixTitle = "N/A",
                        Applied = false,
                        Error = ex.Message
                    });
                    fixesFailed++;
                }
            }

            // Write all changes to disk (unless preview mode)
            var modifiedFiles = new List<string>();
            if (!preview && modifiedDocumentIds.Count > 0)
            {
                foreach (var docId in modifiedDocumentIds)
                {
                    var doc = solution.GetDocument(docId);
                    if (doc?.FilePath != null)
                    {
                        var text = await doc.GetTextAsync();
                        await File.WriteAllTextAsync(doc.FilePath, text.ToString());
                        modifiedFiles.Add(doc.FilePath);
                        Console.Error.WriteLine($"Updated: {doc.FilePath}");
                    }
                }
            }
            else if (preview)
            {
                // In preview mode, just list what would be modified
                foreach (var docId in modifiedDocumentIds)
                {
                    var doc = solution.GetDocument(docId);
                    if (doc?.FilePath != null)
                    {
                        modifiedFiles.Add(doc.FilePath);
                    }
                }
            }

            return new BatchApplyCodeFixResult
            {
                Success = true,
                SolutionPath = solutionPath,
                DiagnosticId = diagnosticId,
                TotalDiagnosticsFound = allDiagnostics.Count,
                DiagnosticsWithFixes = diagnosticsWithFixes,
                FixesApplied = fixesApplied,
                FixesFailed = fixesFailed,
                FilesModified = modifiedFiles.Count,
                ModifiedFiles = modifiedFiles,
                Details = details.Count <= 50 ? details : null, // Only include details if not too many
                IsPreview = preview
            };
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

    /// <summary>
    /// Runs .NET analyzers and returns diagnostics for a specific diagnostic ID.
    /// </summary>
    private static async Task<IEnumerable<Diagnostic>> GetAnalyzerDiagnosticsForBatchAsync(
        Compilation compilation,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        string diagnosticId)
    {
        try
        {
            // Enable the specific diagnostic at Warning level
            var diagnosticOptions = new Dictionary<string, ReportDiagnostic>
            {
                [diagnosticId] = ReportDiagnostic.Warn
            };

            var modifiedCompilation = compilation.WithOptions(
                compilation.Options.WithSpecificDiagnosticOptions(diagnosticOptions));

            var options = new CompilationWithAnalyzersOptions(
                new AnalyzerOptions([]),
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false);

            var compilationWithAnalyzers = modifiedCompilation.WithAnalyzers(analyzers, options);
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

            return diagnostics.Where(d =>
                d.Id.Equals(diagnosticId, StringComparison.OrdinalIgnoreCase) &&
                d.Location.IsInSource);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error running analyzers for batch fix: {ex.Message}");
            return [];
        }
    }
}
