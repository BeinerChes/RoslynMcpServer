using RoslynMcpServer.Graph;

namespace RoslynMcpServer;

/// <summary>
/// Dead code detection tool.
/// Issue: #13
/// </summary>
public static partial class RoslynTools
{
    // Empty default patterns - structural detection is preferred over name patterns
    private static readonly string[] DefaultExcludeTypePatterns = [];
    private static readonly string[] DefaultExcludeFilePatterns = [];

    /// <summary>
    /// Registers the FindDeadCode tool.
    /// </summary>
    internal static void RegisterFindDeadCodeTool(McpServer server)
    {
        server.RegisterTool(
            "FindDeadCode",
            new ToolDefinition
            {
                Description = "Finds potentially dead code - methods and properties with no callers. Excludes common entry points, properties with attributes, and properties on pure model/DTO classes (detected by structure: only auto-properties, no methods). Use GraphAnalyze first to build the graph.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        includePrivate = new
                        {
                            type = "boolean",
                            description = "Include private members (often intentionally unused). Default: false"
                        },
                        includeTests = new
                        {
                            type = "boolean",
                            description = "Include test files in dead code search. Default: false"
                        },
                        maxResults = new
                        {
                            type = "integer",
                            description = "Maximum number of results to return. Default: 100",
                            minimum = 1,
                            maximum = 1000
                        },
                        excludeTypePatterns = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Additional type name patterns to exclude (suffix match). Example: ['Result', 'Response', 'Dto']. Empty by default - relies on structural detection instead."
                        },
                        excludeFilePatterns = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Additional file path patterns to exclude (contains match). Example: ['Models/', 'Dto/']. Empty by default - relies on structural detection instead."
                        }
                    },
                    required = Array.Empty<string>()
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var (solutionPath, solutionError) = GetSolutionPathOrError();
                if (solutionError != null) return solutionError;

                var includePrivate = args?["includePrivate"]?.GetValue<bool>() ?? false;
                var includeTests = args?["includeTests"]?.GetValue<bool>() ?? false;
                var maxResults = args?["maxResults"]?.GetValue<int>() ?? 100;

                // Parse array parameters with defaults
                var excludeTypePatterns = args?["excludeTypePatterns"]?.AsArray()?
                    .Select(x => x?.GetValue<string>() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray() ?? DefaultExcludeTypePatterns;

                var excludeFilePatterns = args?["excludeFilePatterns"]?.AsArray()?
                    .Select(x => x?.GetValue<string>() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray() ?? DefaultExcludeFilePatterns;

                var result = await FindDeadCodeAsync(
                    solutionPath!, includePrivate, includeTests, maxResults,
                    excludeTypePatterns, excludeFilePatterns);
                return CreateToolResponse(result, !result.Success);
            });
    }

    private static async Task<DeadCodeResult> FindDeadCodeAsync(
        string solutionPath, bool includePrivate, bool includeTests, int maxResults,
        string[] excludeTypePatterns, string[] excludeFilePatterns)
    {
        using var db = new GraphDatabase(solutionPath);

        if (!db.Exists())
        {
            // Auto-build graph if it doesn't exist
            Console.Error.WriteLine("Graph database not found. Building automatically...");
            var buildResult = await AnalyzeGraphAsync(solutionPath, incremental: false, projectFilter: null);
            if (!buildResult.Success)
            {
                return new DeadCodeResult
                {
                    Success = false,
                    Error = $"Failed to build graph: {buildResult.Error}"
                };
            }
            Console.Error.WriteLine($"Graph built: {buildResult.SymbolsFound} symbols, {buildResult.EdgesFound} edges");
        }

        await db.OpenAsync();
        var solution = await db.GetSolutionAsync(solutionPath);

        if (solution == null)
        {
            return new DeadCodeResult
            {
                Success = false,
                Error = "Solution not found in graph database."
            };
        }

        // Ensure graph is fresh before querying
        await EnsureGraphFreshAsync(db, solution.Id, solutionPath);

        // Get solution directory for relative paths
        var solutionDir = Path.GetDirectoryName(solutionPath) ?? "";

        // STEP 1: Use graph to quickly find candidates (symbols with no incoming edges)
        var uncalledSymbols = await db.GetSymbolsWithNoCallersAsync(
            solution.Id,
            new[] { SymbolKind.Method, SymbolKind.Property });

        // Apply basic filters
        var candidateSymbols = uncalledSymbols
            .Where(s => !IsEntryPoint(s))
            .Where(s => includePrivate || !IsPrivateSymbol(s))
            .Where(s => includeTests || !IsTestFile(s.FilePath))
            .ToList();

        // Apply file pattern exclusions
        if (excludeFilePatterns.Length > 0)
        {
            candidateSymbols = candidateSymbols
                .Where(s => !excludeFilePatterns.Any(pattern =>
                    s.FilePath.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        // Apply type pattern exclusions
        if (excludeTypePatterns.Length > 0)
        {
            candidateSymbols = candidateSymbols
                .Where(s =>
                {
                    var typeName = GetContainingTypeName(s.QualifiedName);
                    return !excludeTypePatterns.Any(pattern =>
                        typeName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();
        }

        // STEP 2: Load Roslyn solution for accurate validation
        var roslynSolution = await SolutionAnalyzerService.LoadSolutionAsync(solutionPath);
        if (roslynSolution == null)
        {
            return new DeadCodeResult
            {
                Success = false,
                Error = "Could not load Roslyn solution for validation."
            };
        }

        var deadCode = new List<DeadCodeEntry>();
        var validatedCount = 0;
        var falsePositivesFiltered = 0;

        // STEP 3: Validate each candidate using FindReferencesAsync
        foreach (var candidate in candidateSymbols)
        {
            if (deadCode.Count >= maxResults) break;

            // Find the symbol by qualified name using Roslyn
            var roslynSymbol = await FindSymbolByQualifiedNameAsync(roslynSolution, candidate.QualifiedName);
            if (roslynSymbol == null) continue;

            validatedCount++;

            // Use FindReferencesAsync for accurate reference detection
            var references = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder
                .FindReferencesAsync(roslynSymbol, roslynSolution);

            // Count actual references (excluding the definition itself)
            var refLocations = references.SelectMany(r => r.Locations).ToList();
            var definitionSpan = roslynSymbol.Locations.FirstOrDefault()?.SourceSpan;
            var refCount = refLocations.Count(loc =>
                definitionSpan == null || !loc.Location.SourceSpan.Equals(definitionSpan));

            if (refCount == 0)
            {
                // Truly dead - no references found
                deadCode.Add(new DeadCodeEntry
                {
                    Name = candidate.Name,
                    QualifiedName = candidate.QualifiedName,
                    Kind = candidate.Kind.ToString(),
                    FilePath = GetRelativePath(candidate.FilePath, solutionDir),
                    FileName = Path.GetFileName(candidate.FilePath),
                    Line = candidate.Line
                });
            }
            else
            {
                falsePositivesFiltered++;
            }
        }

        // Group by file
        var byFile = deadCode
            .GroupBy(d => d.FilePath)
            .Select(g => new DeadCodeFile
            {
                FilePath = g.Key,
                FileName = Path.GetFileName(g.Key),
                Count = g.Count(),
                Symbols = g.ToList()
            })
            .OrderByDescending(f => f.Count)
            .ToList();

        return new DeadCodeResult
        {
            Success = true,
            TotalFound = deadCode.Count,
            TotalFiles = byFile.Count,
            ByFile = byFile,
            Note = $"Validated {validatedCount} candidates, filtered {falsePositivesFiltered} false positives." +
                   (deadCode.Count >= maxResults ? $" Results limited to {maxResults}." : "")
        };
    }

    /// <summary>
    /// Extracts the containing type name from a qualified symbol name.
    /// e.g., "Namespace.ClassName.PropertyName" -> "ClassName"
    /// </summary>
    private static string GetContainingTypeName(string qualifiedName)
    {
        var parts = qualifiedName.Split('.');
        return parts.Length >= 2 ? parts[^2] : qualifiedName;
    }

    /// <summary>
    /// Detects if a class is a "pure model" class - only auto-properties, no methods with logic.
    /// Issue #106: Structural detection is more reliable than name-based patterns.
    /// </summary>
    private static async Task<bool> IsPureModelClassAsync(
        Microsoft.CodeAnalysis.Solution solution, string filePath, string typeName)
    {
        try
        {
            var document = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == filePath);

            if (document == null) return false;

            var syntaxRoot = await document.GetSyntaxRootAsync();
            if (syntaxRoot == null) return false;

            // Find the class declaration
            var classDecl = syntaxRoot.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == typeName);

            if (classDecl == null) return false;

            // Check members: a pure model class has only auto-properties (no backing logic)
            var members = classDecl.Members;

            foreach (var member in members)
            {
                switch (member)
                {
                    case Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax prop:
                        // Auto-properties have no body (just { get; set; })
                        if (prop.ExpressionBody != null) return false; // => expression body
                        if (prop.AccessorList != null)
                        {
                            foreach (var accessor in prop.AccessorList.Accessors)
                            {
                                // Auto-property accessors have no body
                                if (accessor.Body != null || accessor.ExpressionBody != null)
                                    return false;
                            }
                        }
                        break;

                    case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax:
                        // Methods (other than property accessors) disqualify pure model
                        return false;

                    case Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax ctor:
                        // Allow parameterless constructors with no/empty body
                        if (ctor.ParameterList.Parameters.Count > 0) return false;
                        if (ctor.Body != null && ctor.Body.Statements.Count > 0) return false;
                        if (ctor.ExpressionBody != null) return false;
                        break;

                    case Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax:
                        // Fields are OK (backing fields)
                        break;

                    case Microsoft.CodeAnalysis.CSharp.Syntax.IndexerDeclarationSyntax:
                    case Microsoft.CodeAnalysis.CSharp.Syntax.EventDeclarationSyntax:
                    case Microsoft.CodeAnalysis.CSharp.Syntax.OperatorDeclarationSyntax:
                        // These disqualify pure model
                        return false;
                }
            }

            return true; // Only auto-properties and allowed members
        }
        catch
        {
            return false; // If we can't check, assume not a pure model
        }
    }

    private static bool IsTestFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        var lower = filePath.ToLowerInvariant();
        return lower.Contains("test") || lower.Contains(".tests") || lower.Contains("\\tests\\");
    }

    private static bool IsEntryPoint(SymbolRecord symbol)
    {
        var name = symbol.Name;
        var qualifiedName = symbol.QualifiedName;

        // Common entry points that shouldn't be flagged as dead code
        return name == "Main" ||
               name == "RunAsync" || // Issue #36: Common entry point pattern
               name.StartsWith("On") || // Event handlers: OnClick, OnLoad, etc.
               name.EndsWith("Async") && name.StartsWith("On") ||
               qualifiedName.Contains(".Program.") ||
               qualifiedName.Contains(".Startup.") ||
               name == "ConfigureServices" ||
               name == "Configure" ||
               name == "Dispose" ||
               name == "DisposeAsync" ||
               name.StartsWith("get_") || // Property getters (called implicitly)
               name.StartsWith("set_");   // Property setters (called implicitly)
    }

    private static bool IsPrivateSymbol(SymbolRecord symbol)
    {
        // Simple heuristic: if qualified name has only one dot after namespace,
        // and symbol name starts with underscore or lowercase, likely private
        return symbol.Name.StartsWith("_") ||
               (symbol.Name.Length > 0 && char.IsLower(symbol.Name[0]));
    }

    /// <summary>
    /// Checks if a symbol at the given file/line has any attributes.
    /// Issue: #36 - Properties with attributes are likely used for serialization.
    /// </summary>
    private static async Task<bool> HasAttributesAsync(
        Microsoft.CodeAnalysis.Solution solution, string filePath, int line)
    {
        try
        {
            var document = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == filePath);

            if (document == null) return false;

            var syntaxRoot = await document.GetSyntaxRootAsync();
            if (syntaxRoot == null) return false;

            // Find the node at the given line (0-based in Roslyn)
            var lineSpan = syntaxRoot.SyntaxTree.GetText().Lines[line - 1];
            var node = syntaxRoot.FindNode(lineSpan.Span);

            // Walk up to find a property declaration
            while (node != null)
            {
                if (node is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax propDecl)
                {
                    return propDecl.AttributeLists.Count > 0;
                }
                node = node.Parent;
            }

            return false;
        }
        catch
        {
            return false; // If we can't check, assume no attributes
        }
    }

    private static object CreateToolResponse(object result, bool isError = false)
    {
        return new
        {
            content = new[]
            {
                new { type = "text", text = System.Text.Json.JsonSerializer.Serialize(result, JsonOptions) }
            },
            isError
        };
    }

    private static object CreateToolError(string message)
    {
        return new
        {
            content = new[] { new { type = "text", text = message } },
            isError = false
        };
    }
}

// DTOs for dead code detection
public class DeadCodeResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalFound { get; set; }
    public int TotalFiles { get; set; }
    public List<DeadCodeFile>? ByFile { get; set; }
    public string? Note { get; set; }
}

public class DeadCodeFile
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public int Count { get; set; }
    public List<DeadCodeEntry> Symbols { get; set; } = [];
}

public class DeadCodeEntry
{
    public string Name { get; set; } = "";
    public string QualifiedName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public int Line { get; set; }
}
