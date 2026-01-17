using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Rename;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Renames a symbol at a specific file position across the entire solution.
    /// </summary>
    public async Task<RenameSymbolResult> RenameSymbolAsync(
        string solutionPath,
        string filePath,
        int line,
        int column,
        string newName)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new RenameSymbolResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        if (!File.Exists(filePath))
        {
            return new RenameSymbolResult
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
            var normalizedPath = Path.GetFullPath(filePath);
            var document = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => string.Equals(
                    Path.GetFullPath(d.FilePath ?? ""),
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase));

            if (document == null)
            {
                return new RenameSymbolResult
                {
                    Success = false,
                    Error = $"File not found in solution: {filePath}"
                };
            }

            // Get the position in the document
            var text = await document.GetTextAsync();
            if (line < 1 || line > text.Lines.Count)
            {
                return new RenameSymbolResult
                {
                    Success = false,
                    Error = $"Line {line} is out of range (document has {text.Lines.Count} lines)"
                };
            }

            var lineInfo = text.Lines[line - 1];
            var position = lineInfo.Start + (column - 1);

            if (position < 0 || position > text.Length)
            {
                return new RenameSymbolResult
                {
                    Success = false,
                    Error = $"Column {column} is out of range for line {line}"
                };
            }

            // Find the symbol at this position
            var semanticModel = await document.GetSemanticModelAsync();
            if (semanticModel == null)
            {
                return new RenameSymbolResult
                {
                    Success = false,
                    Error = "Could not get semantic model for document"
                };
            }

            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, workspace);
            if (symbol == null)
            {
                return new RenameSymbolResult
                {
                    Success = false,
                    Error = $"No symbol found at line {line}, column {column}"
                };
            }

            // Get the original symbol for reporting
            var originalName = symbol.Name;
            var symbolKind = symbol.Kind.ToString();
            var containingType = symbol.ContainingType?.ToDisplayString() ?? "";

            Console.Error.WriteLine($"Renaming {symbolKind} '{originalName}' to '{newName}'");

            // Validate new name
            if (string.IsNullOrWhiteSpace(newName))
            {
                return new RenameSymbolResult
                {
                    Success = false,
                    Error = "New name cannot be empty"
                };
            }

            if (newName == originalName)
            {
                return new RenameSymbolResult
                {
                    Success = false,
                    Error = $"New name '{newName}' is the same as the original name"
                };
            }

            // Perform the rename
            var renameOptions = new SymbolRenameOptions(
                RenameOverloads: false,
                RenameInStrings: false,
                RenameInComments: false,
                RenameFile: false);

            var newSolution = await Renamer.RenameSymbolAsync(
                solution,
                symbol,
                renameOptions,
                newName);

            // Collect changed files and count changes
            var affectedFiles = new List<string>();
            var totalChanges = 0;
            var changedDocuments = newSolution.GetChanges(solution).GetProjectChanges()
                .SelectMany(pc => pc.GetChangedDocuments())
                .ToList();

            foreach (var changedDocId in changedDocuments)
            {
                var originalDoc = solution.GetDocument(changedDocId);
                var newDoc = newSolution.GetDocument(changedDocId);

                if (originalDoc == null || newDoc == null) continue;

                var originalText = await originalDoc.GetTextAsync();
                var newText = await newDoc.GetTextAsync();

                var textChanges = newText.GetTextChanges(originalText);
                if (textChanges.Count > 0)
                {
                    affectedFiles.Add(originalDoc.FilePath ?? "");
                    totalChanges += textChanges.Count;
                }
            }

            // Apply changes to disk
            foreach (var changedDocId in changedDocuments)
            {
                var newDoc = newSolution.GetDocument(changedDocId);
                if (newDoc?.FilePath == null) continue;

                var newText = await newDoc.GetTextAsync();
                await File.WriteAllTextAsync(newDoc.FilePath, newText.ToString());
            }

            Console.Error.WriteLine($"Renamed '{originalName}' to '{newName}' in {affectedFiles.Count} files");

            return new RenameSymbolResult
            {
                Success = true,
                OriginalName = originalName,
                NewName = newName,
                SymbolKind = symbolKind,
                ContainingType = containingType,
                TotalFilesAffected = affectedFiles.Count,
                TotalChanges = totalChanges,
                AffectedFiles = affectedFiles
            };
        }
        catch (Exception ex)
        {
            return new RenameSymbolResult
            {
                Success = false,
                Error = $"Rename failed: {ex.Message}"
            };
        }
    }
}
