using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Reflection;

namespace RoslynMcpServer.Services;

public class CodeFixService
{


    private static ImmutableArray<CodeFixProvider>? _codeFixProviders;


    public static async Task<ApplyCodeFixResult> ApplyCodeFixAsync(
        Solution solution,
        string filePath,
        int line,
        int column,
        string? diagnosticId = null,
        int? fixIndex = null,
        bool preview = false)
    {
        if (!File.Exists(filePath))
        {
            return new ApplyCodeFixResult { Success = false, Error = $"Source file not found: {filePath}" };
        }

        try
        {
            var documentId = solution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
            if (documentId == null)
                return new ApplyCodeFixResult { Success = false, Error = $"File not found in solution: {filePath}" };

            var document = solution.GetDocument(documentId);
            if (document == null)
                return new ApplyCodeFixResult { Success = false, Error = $"Could not load document: {filePath}" };

            var semanticModel = await document.GetSemanticModelAsync();
            if (semanticModel == null)
                return new ApplyCodeFixResult { Success = false, Error = "Could not get semantic model for document" };

            var sourceText = await document.GetTextAsync();
            var position = sourceText.Lines.GetPosition(new LinePosition(line - 1, column - 1));

            var compilation = semanticModel.Compilation;
            var tree = semanticModel.SyntaxTree;
            var allDiagnostics = compilation.GetDiagnostics()
                .Concat(tree.GetDiagnostics())
                .Where(d => d.Location.IsInSource && d.Location.SourceTree?.FilePath == filePath)
                .ToList();

            if (string.IsNullOrEmpty(diagnosticId) || diagnosticId.StartsWith("CA", StringComparison.OrdinalIgnoreCase))
            {
                var netAnalyzers = AnalyzerLoader.GetNetAnalyzers();
                if (netAnalyzers.Length > 0)
                {
                    Console.Error.WriteLine($"Running {netAnalyzers.Length} .NET analyzers for code fix...");
                    var analyzerDiagnostics = await RunAnalyzersForCodeFixAsync(compilation, netAnalyzers, filePath);
                    allDiagnostics.AddRange(analyzerDiagnostics);
                }
            }

            var diagnosticsAtLocation = allDiagnostics
                .Where(d =>
                {
                    var diagLine = d.Location.GetLineSpan().StartLinePosition.Line + 1;
                    var diagCol = d.Location.GetLineSpan().StartLinePosition.Character + 1;
                    return diagLine == line && Math.Abs(diagCol - column) <= 5;
                })
                .ToList();

            if (!string.IsNullOrEmpty(diagnosticId))
                diagnosticsAtLocation = diagnosticsAtLocation.Where(d => d.Id.Equals(diagnosticId, StringComparison.OrdinalIgnoreCase)).ToList();

            if (diagnosticsAtLocation.Count == 0)
            {
                return new ApplyCodeFixResult
                {
                    Success = false,
                    Error = diagnosticId != null ? $"No diagnostic '{diagnosticId}' found at line {line}, column {column}" : $"No diagnostics found at line {line}, column {column}",
                    FilePath = filePath,
                    Line = line,
                    Column = column
                };
            }

            var targetDiagnostic = diagnosticsAtLocation.First();
            var providers = GetCodeFixProviders();
            var relevantProviders = providers.Where(p => p.FixableDiagnosticIds.Contains(targetDiagnostic.Id)).ToList();

            if (relevantProviders.Count == 0)
            {
                return new ApplyCodeFixResult
                {
                    Success = false,
                    Error = $"No code fix available for diagnostic '{targetDiagnostic.Id}': {targetDiagnostic.GetMessage()}",
                    FilePath = filePath,
                    DiagnosticId = targetDiagnostic.Id,
                    DiagnosticMessage = targetDiagnostic.GetMessage(),
                    Line = line,
                    Column = column
                };
            }

            var allFixes = new List<(CodeAction action, CodeFixProvider provider)>();
            foreach (var provider in relevantProviders)
            {
                var context = new CodeFixContext(document, targetDiagnostic, (action, diagnostics) => allFixes.Add((action, provider)), CancellationToken.None);
                await provider.RegisterCodeFixesAsync(context);
            }

            if (allFixes.Count == 0)
            {
                return new ApplyCodeFixResult
                {
                    Success = false,
                    Error = $"Code fix providers found but no fixes registered for '{targetDiagnostic.Id}'",
                    FilePath = filePath,
                    DiagnosticId = targetDiagnostic.Id,
                    DiagnosticMessage = targetDiagnostic.GetMessage(),
                    Line = line,
                    Column = column
                };
            }

            var availableFixes = allFixes.Select((f, i) => new CodeFixInfo { Index = i, Title = f.action.Title, EquivalenceKey = f.action.EquivalenceKey }).ToList();

            if (fixIndex == null && allFixes.Count > 1)
            {
                return new ApplyCodeFixResult
                {
                    Success = false,
                    Error = "Multiple fixes available. Specify fixIndex to select one.",
                    FilePath = filePath,
                    DiagnosticId = targetDiagnostic.Id,
                    DiagnosticMessage = targetDiagnostic.GetMessage(),
                    Line = line,
                    Column = column,
                    AvailableFixes = availableFixes
                };
            }

            var selectedIndex = fixIndex ?? 0;
            if (selectedIndex < 0 || selectedIndex >= allFixes.Count)
            {
                return new ApplyCodeFixResult
                {
                    Success = false,
                    Error = $"Invalid fixIndex {selectedIndex}. Valid range: 0-{allFixes.Count - 1}",
                    FilePath = filePath,
                    DiagnosticId = targetDiagnostic.Id,
                    DiagnosticMessage = targetDiagnostic.GetMessage(),
                    Line = line,
                    Column = column,
                    AvailableFixes = availableFixes
                };
            }

            var selectedFix = allFixes[selectedIndex];
            Console.Error.WriteLine($"Applying fix: {selectedFix.action.Title}");

            var operations = await selectedFix.action.GetOperationsAsync(CancellationToken.None);
            var applyChangesOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();

            if (applyChangesOperation == null)
            {
                return new ApplyCodeFixResult
                {
                    Success = false,
                    Error = "Code fix does not produce applicable changes",
                    FilePath = filePath,
                    DiagnosticId = targetDiagnostic.Id,
                    DiagnosticMessage = targetDiagnostic.GetMessage(),
                    Line = line,
                    Column = column,
                    AppliedFixTitle = selectedFix.action.Title
                };
            }

            var changedSolution = applyChangesOperation.ChangedSolution;
            var changedDocuments = changedSolution.GetChanges(solution).GetProjectChanges().SelectMany(p => p.GetChangedDocuments()).ToList();

            if (preview)
            {
                return new ApplyCodeFixResult
                {
                    Success = true,
                    FilePath = filePath,
                    DiagnosticId = targetDiagnostic.Id,
                    DiagnosticMessage = targetDiagnostic.GetMessage(),
                    Line = line,
                    Column = column,
                    AppliedFixTitle = selectedFix.action.Title,
                    AvailableFixes = availableFixes,
                    FilesChanged = changedDocuments.Count,
                    IsPreview = true
                };
            }

            int filesWritten = 0;
            foreach (var changedDocId in changedDocuments)
            {
                var changedDoc = changedSolution.GetDocument(changedDocId);
                if (changedDoc != null)
                {
                    var newText = await changedDoc.GetTextAsync();
                    var docPath = changedDoc.FilePath;
                    if (!string.IsNullOrEmpty(docPath))
                    {
                        await File.WriteAllTextAsync(docPath, newText.ToString());
                        filesWritten++;
                        Console.Error.WriteLine($"Updated: {docPath}");
                    }
                }
            }

            return new ApplyCodeFixResult
            {
                Success = true,
                FilePath = filePath,
                DiagnosticId = targetDiagnostic.Id,
                DiagnosticMessage = targetDiagnostic.GetMessage(),
                Line = line,
                Column = column,
                AppliedFixTitle = selectedFix.action.Title,
                AvailableFixes = availableFixes,
                FilesChanged = filesWritten,
                IsPreview = false
            };
        }
        catch (Exception ex)
        {
            return new ApplyCodeFixResult { Success = false, Error = $"Failed to apply code fix: {ex.Message}" };
        }
    }

    private static void LoadCodeFixProvidersFromAssembly(Assembly assembly, List<CodeFixProvider> providers)
    {
        try
        {
            var providerTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract &&
                           typeof(CodeFixProvider).IsAssignableFrom(t) &&
                           t.GetConstructor(Type.EmptyTypes) != null);

            foreach (var type in providerTypes)
            {
                try
                {
                    if (Activator.CreateInstance(type) is CodeFixProvider instance)
                    {
                        providers.Add(instance);
                    }
                }
                catch
                {
                    // Skip providers that can't be instantiated
                }
            }
        }
        catch
        {
            // Skip assemblies that fail to enumerate types
        }
    }


    public static ImmutableArray<CodeFixProvider> GetCodeFixProviders()
    {
        if (_codeFixProviders.HasValue)
            return _codeFixProviders.Value;

        var providers = new List<CodeFixProvider>();

        // Load code fix providers from CSharp.Features assembly (for CS* diagnostics)
        try
        {
            var csharpFeaturesAssembly = Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features");
            LoadCodeFixProvidersFromAssembly(csharpFeaturesAssembly, providers);
            Console.Error.WriteLine($"Loaded {providers.Count} code fix providers from CSharp.Features");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not load CSharp.Features code fix providers: {ex.Message}");
        }

        // Load code fix providers from bundled NetAnalyzers (for CA* diagnostics)
        var netAnalyzerAssemblies = AnalyzerLoader.GetLoadedAssemblies();
        foreach (var assembly in netAnalyzerAssemblies)
        {
            var countBefore = providers.Count;
            LoadCodeFixProvidersFromAssembly(assembly, providers);
            var loaded = providers.Count - countBefore;
            if (loaded > 0)
            {
                Console.Error.WriteLine($"Loaded {loaded} code fix providers from {assembly.GetName().Name}");
            }
        }

        Console.Error.WriteLine($"Total code fix providers: {providers.Count}");
        _codeFixProviders = providers.ToImmutableArray();
        return _codeFixProviders.Value;
    }


    public static HashSet<string> GetFixableDiagnosticIds()
    {
        var providers = GetCodeFixProviders();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            foreach (var id in provider.FixableDiagnosticIds)
            {
                ids.Add(id);
            }
        }
        return ids;
    }


    private static async Task<IEnumerable<Diagnostic>> RunAnalyzersForCodeFixAsync(
        Compilation compilation,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        string filePath)
    {
        try
        {
            // Enable all CA* rules at Warning level
            var diagnosticOptions = new Dictionary<string, ReportDiagnostic>();
            foreach (var analyzer in analyzers)
            {
                foreach (var descriptor in analyzer.SupportedDiagnostics)
                {
                    if (descriptor.Id.StartsWith("CA", StringComparison.Ordinal))
                    {
                        diagnosticOptions[descriptor.Id] = ReportDiagnostic.Warn;
                    }
                }
            }

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

            // Filter to the specific file
            return diagnostics.Where(d =>
                d.Location.IsInSource &&
                d.Location.SourceTree?.FilePath == filePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error running analyzers for code fix: {ex.Message}");
            return [];
        }
    }


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


    public async Task<BatchApplyCodeFixResult> BatchApplyCodeFixAsync(
        Solution solution,
        string solutionPath,
        string diagnosticId,
        string? projectFilter = null,
        string? fileFilter = null,
        int maxFixes = 100,
        bool preview = false)
    {
        if (string.IsNullOrWhiteSpace(diagnosticId))
            return new BatchApplyCodeFixResult { Success = false, Error = "diagnosticId is required for batch fix operations" };

        try
        {
            // Find relevant code fix providers
            var providers = GetCodeFixProviders();
            var relevantProviders = providers.Where(p => p.FixableDiagnosticIds.Contains(diagnosticId)).ToList();

            if (relevantProviders.Count == 0)
                return new BatchApplyCodeFixResult { Success = false, Error = $"No code fix provider available for diagnostic '{diagnosticId}'", SolutionPath = solutionPath, DiagnosticId = diagnosticId };

            Console.Error.WriteLine($"Found {relevantProviders.Count} code fix provider(s) for {diagnosticId}");

            // Load analyzers if needed for CA* diagnostics
            var needAnalyzers = diagnosticId.StartsWith("CA", StringComparison.OrdinalIgnoreCase);
            ImmutableArray<DiagnosticAnalyzer> netAnalyzers = [];
            if (needAnalyzers)
            {
                netAnalyzers = AnalyzerLoader.GetNetAnalyzers();
                Console.Error.WriteLine($"Running {netAnalyzers.Length} .NET analyzers for batch fix...");
            }

            // Collect all matching diagnostics
            var allDiagnostics = await CollectDiagnosticsAsync(solution, diagnosticId, projectFilter, fileFilter, netAnalyzers);
            Console.Error.WriteLine($"Found {allDiagnostics.Count} diagnostics matching '{diagnosticId}'");

            if (allDiagnostics.Count == 0)
                return new BatchApplyCodeFixResult { Success = true, SolutionPath = solutionPath, DiagnosticId = diagnosticId, TotalDiagnosticsFound = 0, DiagnosticsWithFixes = 0, FixesApplied = 0, FixesFailed = 0, FilesModified = 0, IsPreview = preview };

            // Apply fixes
            var diagnosticsToFix = allDiagnostics.Take(maxFixes).ToList();
            var details = new List<BatchFixDetail>();
            var allModifiedDocumentIds = new HashSet<DocumentId>();
            int fixesApplied = 0, fixesFailed = 0, diagnosticsWithFixes = 0;

            foreach (var (originalDocument, diagnostic) in diagnosticsToFix)
            {
                try
                {
                    var (success, newSolution, detail, modifiedDocs) = await ApplySingleFixAsync(
                        solution, originalDocument, diagnostic, relevantProviders, netAnalyzers, diagnosticId);

                    if (success && detail != null)
                    {
                        solution = newSolution;
                        details.Add(detail);
                        foreach (var docId in modifiedDocs)
                            allModifiedDocumentIds.Add(docId);
                        fixesApplied++;
                        diagnosticsWithFixes++;
                    }
                    else
                    {
                        fixesFailed++;
                    }
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

            // Write modified files to disk
            var modifiedFiles = await WriteModifiedFilesAsync(solution, allModifiedDocumentIds, preview);

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
                Details = details.Count <= 50 ? details : null,
                IsPreview = preview
            };
        }
        catch (Exception ex)
        {
            return new BatchApplyCodeFixResult { Success = false, Error = $"Failed to batch apply code fixes: {ex.Message}", SolutionPath = solutionPath, DiagnosticId = diagnosticId };
        }
    }


    private static async Task<List<(Document document, Diagnostic diagnostic)>> CollectDiagnosticsAsync(
            Solution solution,
            string diagnosticId,
            string? projectFilter,
            string? fileFilter,
            ImmutableArray<DiagnosticAnalyzer> netAnalyzers)
    {
        var allDiagnostics = new List<(Document document, Diagnostic diagnostic)>();
        var needAnalyzers = diagnosticId.StartsWith("CA", StringComparison.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            if (!string.IsNullOrEmpty(projectFilter) && !project.Name.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            IEnumerable<Diagnostic> projectDiagnostics = needAnalyzers && netAnalyzers.Length > 0
                ? await GetAnalyzerDiagnosticsForBatchAsync(compilation, netAnalyzers, diagnosticId)
                : compilation.GetDiagnostics();

            foreach (var diagnostic in projectDiagnostics)
            {
                if (!diagnostic.Id.Equals(diagnosticId, StringComparison.OrdinalIgnoreCase) || !diagnostic.Location.IsInSource)
                    continue;

                var syntaxTree = diagnostic.Location.SourceTree;
                if (syntaxTree == null) continue;

                if (!string.IsNullOrEmpty(fileFilter))
                {
                    var fileName = Path.GetFileName(syntaxTree.FilePath);
                    if (!fileName.Contains(fileFilter, StringComparison.OrdinalIgnoreCase) && !syntaxTree.FilePath.Contains(fileFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var documentId = solution.GetDocumentIdsWithFilePath(syntaxTree.FilePath).FirstOrDefault();
                var document = documentId != null ? solution.GetDocument(documentId) : null;
                if (document != null)
                    allDiagnostics.Add((document, diagnostic));
            }
        }

        return allDiagnostics;
    }


    private static async Task<(bool success, Solution newSolution, BatchFixDetail? detail, HashSet<DocumentId> modifiedDocs)> ApplySingleFixAsync(
            Solution solution,
            Document originalDocument,
            Diagnostic diagnostic,
            List<CodeFixProvider> providers,
            ImmutableArray<DiagnosticAnalyzer> netAnalyzers,
            string diagnosticId)
    {
        var needAnalyzers = diagnosticId.StartsWith("CA", StringComparison.OrdinalIgnoreCase);
        var modifiedDocs = new HashSet<DocumentId>();

        var currentDocument = solution.GetDocument(originalDocument.Id);
        if (currentDocument == null)
            return (false, solution, null, modifiedDocs);

        var currentSemanticModel = await currentDocument.GetSemanticModelAsync();
        if (currentSemanticModel == null)
            return (false, solution, null, modifiedDocs);

        List<Diagnostic> currentDiagnostics = needAnalyzers && netAnalyzers.Length > 0
            ? (await GetAnalyzerDiagnosticsForBatchAsync(currentSemanticModel.Compilation, netAnalyzers, diagnosticId))
                .Where(d => d.Location.SourceTree?.FilePath == currentDocument.FilePath).ToList()
            : currentSemanticModel.Compilation.GetDiagnostics()
                .Where(d => d.Id.Equals(diagnosticId, StringComparison.OrdinalIgnoreCase) && d.Location.IsInSource && d.Location.SourceTree?.FilePath == currentDocument.FilePath).ToList();

        if (currentDiagnostics.Count == 0)
            return (false, solution, null, modifiedDocs);

        var originalLine = diagnostic.Location.GetLineSpan().StartLinePosition.Line;
        var targetDiagnostic = currentDiagnostics.OrderBy(d => Math.Abs(d.Location.GetLineSpan().StartLinePosition.Line - originalLine)).First();

        CodeAction? selectedAction = null;
        foreach (var provider in providers)
        {
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(currentDocument, targetDiagnostic, (action, _) => actions.Add(action), CancellationToken.None);
            await provider.RegisterCodeFixesAsync(context);
            if (actions.Count > 0) { selectedAction = actions[0]; break; }
        }

        if (selectedAction == null)
            return (false, solution, null, modifiedDocs);

        var operations = await selectedAction.GetOperationsAsync(CancellationToken.None);
        var applyChangesOp = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyChangesOp == null)
            return (false, solution, null, modifiedDocs);

        var changedSolution = applyChangesOp.ChangedSolution;
        foreach (var projectChanges in changedSolution.GetChanges(solution).GetProjectChanges())
            foreach (var changedDocId in projectChanges.GetChangedDocuments())
                modifiedDocs.Add(changedDocId);

        var detail = new BatchFixDetail
        {
            FilePath = currentDocument.FilePath ?? "unknown",
            Line = targetDiagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
            Column = targetDiagnostic.Location.GetLineSpan().StartLinePosition.Character + 1,
            DiagnosticMessage = targetDiagnostic.GetMessage(),
            FixTitle = selectedAction.Title,
            Applied = true
        };

        return (true, changedSolution, detail, modifiedDocs);
    }


    private static async Task<List<string>> WriteModifiedFilesAsync(
            Solution solution,
            HashSet<DocumentId> modifiedDocumentIds,
            bool preview)
    {
        var modifiedFiles = new List<string>();

        foreach (var docId in modifiedDocumentIds)
        {
            var doc = solution.GetDocument(docId);
            if (doc?.FilePath == null) continue;

            if (!preview)
            {
                var text = await doc.GetTextAsync();
                await File.WriteAllTextAsync(doc.FilePath, text.ToString());
                Console.Error.WriteLine($"Updated: {doc.FilePath}");
            }

            modifiedFiles.Add(doc.FilePath);
        }

        return modifiedFiles;
    }
}