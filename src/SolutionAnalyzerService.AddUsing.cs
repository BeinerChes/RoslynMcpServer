using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Adds a using directive to the file containing the specified type.
    /// </summary>
    public static async Task<AddUsingResult> AddUsingAsync(
        string solutionPath,
        string typeName,
        string usingDirective,
        string? filePath = null)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new AddUsingResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        using var workspace = CreateWorkspace();

        try
        {
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Normalize the using directive - strip "using " prefix and ";" suffix if provided
            var ns = usingDirective.Trim();
            if (ns.StartsWith("using "))
                ns = ns["using ".Length..];
            if (ns.EndsWith(";"))
                ns = ns[..^1];
            ns = ns.Trim();

            // Find the target file
            SyntaxTree? targetTree = null;
            string? targetFilePath = null;

            if (!string.IsNullOrEmpty(filePath))
            {
                // Find by file path
                foreach (var project in solution.Projects)
                {
                    foreach (var doc in project.Documents)
                    {
                        if (doc.FilePath != null &&
                            (doc.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase) ||
                             doc.FilePath.EndsWith(filePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            targetTree = await doc.GetSyntaxTreeAsync();
                            targetFilePath = doc.FilePath;
                            break;
                        }
                    }
                    if (targetTree != null) break;
                }
            }
            else
            {
                // Find by type name
                foreach (var project in solution.Projects)
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null) continue;

                    var typeSymbols = await SymbolFinder.FindSourceDeclarationsAsync(
                        project,
                        name => name.Equals(typeName, StringComparison.Ordinal) ||
                                name.Equals(typeName, StringComparison.OrdinalIgnoreCase),
                        SymbolFilter.Type);

                    var targetType = typeSymbols.OfType<INamedTypeSymbol>().FirstOrDefault();
                    if (targetType != null)
                    {
                        var location = targetType.Locations.FirstOrDefault(l => l.IsInSource);
                        if (location?.SourceTree != null)
                        {
                            targetTree = location.SourceTree;
                            targetFilePath = targetTree.FilePath;
                        }
                        break;
                    }
                }
            }

            if (targetTree == null || targetFilePath == null)
            {
                var target = !string.IsNullOrEmpty(filePath) ? $"file '{filePath}'" : $"type '{typeName}'";
                return new AddUsingResult
                {
                    Success = false,
                    Error = $"Could not find {target} in solution"
                };
            }

            var root = (CompilationUnitSyntax)await targetTree.GetRootAsync();

            // Check if using already exists
            var existingUsings = root.Usings;
            if (existingUsings.Any(u => u.Name?.ToString() == ns))
            {
                return new AddUsingResult
                {
                    Success = true,
                    FilePath = targetFilePath,
                    UsingDirective = ns,
                    AlreadyExists = true
                };
            }

            // Create the new using directive
            var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns))
                .NormalizeWhitespace()
                .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

            // Insert in sorted order among existing usings
            var newUsings = existingUsings.Add(newUsing);
            newUsings = SyntaxFactory.List(
                newUsings.OrderBy(u => u.Name?.ToString() ?? ""));

            var newRoot = root.WithUsings(newUsings);
            var newSource = newRoot.ToFullString();

            await File.WriteAllTextAsync(targetFilePath, newSource);

            return new AddUsingResult
            {
                Success = true,
                FilePath = targetFilePath,
                UsingDirective = ns,
                AlreadyExists = false
            };
        }
        catch (Exception ex)
        {
            return new AddUsingResult
            {
                Success = false,
                Error = $"Failed to add using: {ex.Message}"
            };
        }
    }
}
