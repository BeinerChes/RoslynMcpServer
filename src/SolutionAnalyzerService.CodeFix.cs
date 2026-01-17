using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Composition.Hosting;
using System.Reflection;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    private static ImmutableArray<CodeFixProvider>? _codeFixProviders;

    /// <summary>
    /// Applies a code fix for a diagnostic at a specific location.
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

        if (!File.Exists(filePath))
        {
            return new ApplyCodeFixResult
            {
                Success = false,
                Error = $"Source file not found: {filePath}"
            };
        }

        using var workspace = MSBuildWorkspace.Create();
        RegisterFailureHandler(workspace);

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Find the document
            var documentId = solution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
            if (documentId == null)
            {
                return new ApplyCodeFixResult
                {
                    Success = false,
                    Error = $"File not found in solution: {filePath}"
                };
            }

            var document = solution.GetDocument(documentId);
            if (document == null)
            {
                return new ApplyCodeFixResult
                {
                    Success = false,
                    Error = $"Could not load document: {filePath}"
                };
            }

            // Get the semantic model and find diagnostics at the location
            var semanticModel = await document.GetSemanticModelAsync();
            if (semanticModel == null)
            {
                return new ApplyCodeFixResult
                {
                    Success = false,
                    Error = "Could not get semantic model for document"
                };
            }

            var sourceText = await document.GetTextAsync();
            var position = sourceText.Lines.GetPosition(new LinePosition(line - 1, column - 1));
            var span = new TextSpan(position, 1);

            // Get diagnostics from compilation
            var compilation = semanticModel.Compilation;
            var tree = semanticModel.SyntaxTree;
            var allDiagnostics = compilation.GetDiagnostics()
                .Concat(tree.GetDiagnostics())
                .Where(d => d.Location.IsInSource &&
                           d.Location.SourceTree?.FilePath == filePath)
                .ToList();

            // Find diagnostics at or near the specified location
            var diagnosticsAtLocation = allDiagnostics
                .Where(d =>
                {
                    var diagLine = d.Location.GetLineSpan().StartLinePosition.Line + 1;
                    var diagCol = d.Location.GetLineSpan().StartLinePosition.Character + 1;
                    // Allow some tolerance for column position
                    return diagLine == line && Math.Abs(diagCol - column) <= 5;
                })
                .ToList();

            // Filter by diagnostic ID if specified
            if (!string.IsNullOrEmpty(diagnosticId))
            {
                diagnosticsAtLocation = diagnosticsAtLocation
                    .Where(d => d.Id.Equals(diagnosticId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (diagnosticsAtLocation.Count == 0)
            {
                return new ApplyCodeFixResult
                {
                    Success = false,
                    Error = diagnosticId != null
                        ? $"No diagnostic '{diagnosticId}' found at line {line}, column {column}"
                        : $"No diagnostics found at line {line}, column {column}",
                    FilePath = filePath,
                    Line = line,
                    Column = column
                };
            }

            var targetDiagnostic = diagnosticsAtLocation.First();

            // Get code fix providers
            var providers = GetCodeFixProviders();
            var relevantProviders = providers
                .Where(p => p.FixableDiagnosticIds.Contains(targetDiagnostic.Id))
                .ToList();

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

            // Collect all available fixes
            var allFixes = new List<(CodeAction action, CodeFixProvider provider)>();

            foreach (var provider in relevantProviders)
            {
                var context = new CodeFixContext(
                    document,
                    targetDiagnostic,
                    (action, diagnostics) => allFixes.Add((action, provider)),
                    CancellationToken.None);

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

            // Build list of available fixes
            var availableFixes = allFixes.Select((f, i) => new CodeFixInfo
            {
                Index = i,
                Title = f.action.Title,
                EquivalenceKey = f.action.EquivalenceKey
            }).ToList();

            // If no fix index specified and there are multiple fixes, return the list
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

            // Select the fix to apply
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

            // Get the operations from the code action
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
            var changedDocuments = changedSolution.GetChanges(solution)
                .GetProjectChanges()
                .SelectMany(p => p.GetChangedDocuments())
                .ToList();

            if (preview)
            {
                // Preview mode - just return info about what would change
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

            // Apply the changes to disk
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
            return new ApplyCodeFixResult
            {
                Success = false,
                Error = $"Failed to apply code fix: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Gets all available code fix providers from Roslyn.
    /// </summary>
    private static ImmutableArray<CodeFixProvider> GetCodeFixProviders()
    {
        if (_codeFixProviders.HasValue)
            return _codeFixProviders.Value;

        var providers = new List<CodeFixProvider>();

        // Load code fix providers from the Features assembly
        var featuresAssembly = typeof(Microsoft.CodeAnalysis.CSharp.CSharpExtensions).Assembly;

        // Try to find the CSharp.Features assembly
        try
        {
            var csharpFeaturesAssembly = Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features");
            var providerTypes = csharpFeaturesAssembly.GetTypes()
                .Where(t => !t.IsAbstract &&
                           typeof(CodeFixProvider).IsAssignableFrom(t) &&
                           t.GetConstructor(Type.EmptyTypes) != null);

            foreach (var type in providerTypes)
            {
                try
                {
                    var instance = Activator.CreateInstance(type) as CodeFixProvider;
                    if (instance != null)
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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not load code fix providers: {ex.Message}");
        }

        Console.Error.WriteLine($"Loaded {providers.Count} code fix providers");
        _codeFixProviders = providers.ToImmutableArray();
        return _codeFixProviders.Value;
    }
}
