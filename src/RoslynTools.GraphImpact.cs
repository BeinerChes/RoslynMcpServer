using RoslynMcpServer.Graph;

namespace RoslynMcpServer;

/// <summary>
/// Graph impact analysis and dead code detection tools.
/// Issue: #13
/// </summary>
public static partial class RoslynTools
{
    /// <summary>
    /// Registers the roslyn_graph_impact tool.
    /// </summary>
    internal static void RegisterGraphImpactTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_graph_impact",
            new ToolDefinition
            {
                Description = "Analyzes what code would be affected if a symbol changes. Returns all direct and transitive callers of the symbol. Essential for understanding the blast radius of a change before refactoring.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        solutionPath = new
                        {
                            type = "string",
                            description = "Absolute path to the .sln or .slnx solution file"
                        },
                        symbolName = new
                        {
                            type = "string",
                            description = "Qualified name of the symbol to analyze (e.g., 'MyNamespace.MyClass.MyMethod')"
                        },
                        maxDepth = new
                        {
                            type = "integer",
                            description = "Maximum recursion depth for transitive callers. -1 for unlimited. Default: 10",
                            minimum = -1,
                            maximum = 100
                        },
                        includeTests = new
                        {
                            type = "boolean",
                            description = "Include test files in impact analysis. Default: true"
                        }
                    },
                    required = definitionArray0
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var solutionPath = args?["solutionPath"]?.GetValue<string>();
                var symbolName = args?["symbolName"]?.GetValue<string>();
                var maxDepth = args?["maxDepth"]?.GetValue<int>() ?? 10;
                var includeTests = args?["includeTests"]?.GetValue<bool>() ?? true;

                if (string.IsNullOrWhiteSpace(solutionPath))
                    return CreateToolError("Error: solutionPath is required");

                if (string.IsNullOrWhiteSpace(symbolName))
                    return CreateToolError("Error: symbolName is required");

                if (!File.Exists(solutionPath))
                    return CreateToolError($"Error: Solution file not found: {solutionPath}");

                var result = await GetGraphImpactAsync(solutionPath, symbolName, maxDepth, includeTests);
                return CreateToolResponse(result, !result.Success);
            });
    }

    // Empty default patterns - structural detection is preferred over name patterns
    private static readonly string[] DefaultExcludeTypePatterns = [];
    private static readonly string[] DefaultExcludeFilePatterns = [];

    /// <summary>
    /// Registers the roslyn_find_dead_code tool.
    /// </summary>
    internal static void RegisterFindDeadCodeTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_find_dead_code",
            new ToolDefinition
            {
                Description = "Finds potentially dead code - methods and properties with no callers. Excludes common entry points, properties with attributes, and (by default) properties on pure model/DTO classes (detected by structure: only auto-properties, no methods). Use roslyn_graph_analyze first to build the graph.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        solutionPath = new
                        {
                            type = "string",
                            description = "Absolute path to the .sln or .slnx solution file"
                        },
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
                        excludePureModelClasses = new
                        {
                            type = "boolean",
                            description = "Exclude properties on pure model/DTO classes (classes with only auto-properties and no methods). Default: true. Set to false to include DTO properties in results."
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
                    required = definitionArray100
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var solutionPath = args?["solutionPath"]?.GetValue<string>();
                var includePrivate = args?["includePrivate"]?.GetValue<bool>() ?? false;
                var includeTests = args?["includeTests"]?.GetValue<bool>() ?? false;
                var maxResults = args?["maxResults"]?.GetValue<int>() ?? 100;
                var excludePureModelClasses = args?["excludePureModelClasses"]?.GetValue<bool>() ?? true;

                // Parse array parameters with defaults
                var excludeTypePatterns = args?["excludeTypePatterns"]?.AsArray()?
                    .Select(x => x?.GetValue<string>() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray() ?? DefaultExcludeTypePatterns;

                var excludeFilePatterns = args?["excludeFilePatterns"]?.AsArray()?
                    .Select(x => x?.GetValue<string>() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray() ?? DefaultExcludeFilePatterns;

                if (string.IsNullOrWhiteSpace(solutionPath))
                    return CreateToolError("Error: solutionPath is required");

                if (!File.Exists(solutionPath))
                    return CreateToolError($"Error: Solution file not found: {solutionPath}");

                var result = await FindDeadCodeAsync(
                    solutionPath, includePrivate, includeTests, maxResults,
                    excludePureModelClasses, excludeTypePatterns, excludeFilePatterns);
                return CreateToolResponse(result, !result.Success);
            });
    }

    private static async Task<GraphImpactResult> GetGraphImpactAsync(
        string solutionPath, string symbolName, int maxDepth, bool includeTests)
    {
        using var db = new GraphDatabase(solutionPath);

        if (!db.Exists())
        {
            return new GraphImpactResult
            {
                Success = false,
                Error = "No graph database exists. Use roslyn_graph_analyze first."
            };
        }

        await db.OpenAsync();
        var solution = await db.GetSolutionAsync(solutionPath);

        if (solution == null)
        {
            return new GraphImpactResult
            {
                Success = false,
                Error = "Solution not found in graph database."
            };
        }

        // Use FindSymbolAsync for partial name matching (Issue #33)
        var searchResult = await db.FindSymbolAsync(solution.Id, symbolName);

        // Issue #35: If not found, try Roslyn search and analyze stale files
        if (searchResult.Symbol == null && _analyzerService != null)
        {
            var refreshedFiles = await TryRefreshFilesForSymbolAsync(
                db, solution.Id, solutionPath, symbolName);

            if (refreshedFiles.Count > 0)
            {
                // Retry graph search after refresh
                searchResult = await db.FindSymbolAsync(solution.Id, symbolName);
            }
        }

        if (searchResult.Symbol == null)
        {
            var error = searchResult.Error ?? $"Symbol '{symbolName}' not found in graph.";
            if (searchResult.Candidates != null && searchResult.Candidates.Count > 0)
            {
                error += " Did you mean: " + string.Join(", ", searchResult.Candidates.Take(5).Select(c => c.QualifiedName));
            }
            return new GraphImpactResult
            {
                Success = false,
                Error = error
            };
        }

        var symbol = searchResult.Symbol;

        // Get all transitive callers
        var allCallers = await db.GetRecursiveCallersAsync(symbol.Id, maxDepth);

        // Filter out test files if requested
        var filteredCallers = includeTests
            ? allCallers
            : allCallers.Where(c => !IsTestFile(c.FilePath)).ToList();

        // Group by file for better readability
        var affectedFiles = filteredCallers
            .Where(c => !string.IsNullOrEmpty(c.FilePath) && c.FilePath != "external")
            .GroupBy(c => c.FilePath)
            .Select(g => new AffectedFile
            {
                FilePath = g.Key,
                FileName = Path.GetFileName(g.Key),
                AffectedSymbols = g.Select(s => new AffectedSymbol
                {
                    Name = s.Name,
                    QualifiedName = s.QualifiedName,
                    Kind = s.Kind.ToString(),
                    Line = s.Line
                }).ToList()
            })
            .OrderBy(f => f.FileName)
            .ToList();

        return new GraphImpactResult
        {
            Success = true,
            Symbol = new GraphSymbolEntry
            {
                Name = symbol.Name,
                QualifiedName = symbol.QualifiedName,
                Kind = symbol.Kind.ToString(),
                FilePath = symbol.FilePath,
                Line = symbol.Line
            },
            TotalAffectedSymbols = filteredCallers.Count,
            TotalAffectedFiles = affectedFiles.Count,
            AffectedFiles = affectedFiles,
            MaxDepthReached = maxDepth
        };
    }

    private static async Task<DeadCodeResult> FindDeadCodeAsync(
        string solutionPath, bool includePrivate, bool includeTests, int maxResults,
        bool excludePureModelClasses, string[] excludeTypePatterns, string[] excludeFilePatterns)
    {
        using var db = new GraphDatabase(solutionPath);

        if (!db.Exists())
        {
            return new DeadCodeResult
            {
                Success = false,
                Error = "No graph database exists. Use roslyn_graph_analyze first."
            };
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

        // Get all symbols that are methods or properties
        // Issue #36: Filter out external symbols (BCL, framework types)
        var allSymbols = await db.GetSymbolsAsync(solution.Id, null, null);
        var candidateSymbols = allSymbols
            .Where(s => s.Kind is SymbolKind.Method or SymbolKind.Property)
            .Where(s => !string.IsNullOrEmpty(s.FilePath) && s.FilePath != "external")
            .Where(s => !IsEntryPoint(s))
            .Where(s => includePrivate || !IsPrivateSymbol(s))
            .Where(s => includeTests || !IsTestFile(s.FilePath))
            .ToList();

        // Apply file pattern exclusions (Issue #106)
        if (excludeFilePatterns.Length > 0)
        {
            candidateSymbols = candidateSymbols
                .Where(s => !excludeFilePatterns.Any(pattern =>
                    s.FilePath.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var deadCode = new List<DeadCodeEntry>();

        // Issue #36: Load Roslyn solution to check for attributes and pure model detection
        Microsoft.CodeAnalysis.Solution? roslynSolution = null;
        if (_analyzerService != null)
        {
            roslynSolution = await SolutionAnalyzerService.LoadSolutionAsync(solutionPath);
        }

        // Cache for pure model class detection (Issue #106)
        var pureModelCache = new Dictionary<string, bool>();

        foreach (var symbol in candidateSymbols)
        {
            if (deadCode.Count >= maxResults) break;

            // Issue #106: Apply type pattern exclusions
            if (excludeTypePatterns.Length > 0)
            {
                var typeName = GetContainingTypeName(symbol.QualifiedName);
                if (excludeTypePatterns.Any(pattern =>
                    typeName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            // Issue #36: For properties, check Reads/Accesses edges too, not just Calls
            // Use null edgeType to check ALL edge types
            var callers = await db.GetCallersAsync(symbol.Id, edgeType: null);
            if (callers.Count == 0)
            {
                // Issue #36: Skip properties with any attributes (likely used for serialization)
                if (symbol.Kind == SymbolKind.Property && roslynSolution != null)
                {
                    if (await HasAttributesAsync(roslynSolution, symbol.FilePath, symbol.Line))
                    {
                        continue;
                    }

                    // Issue #106: Skip properties on pure model/DTO classes (structural detection)
                    if (excludePureModelClasses)
                    {
                        var containingType = GetContainingTypeName(symbol.QualifiedName);
                        if (!pureModelCache.TryGetValue(containingType, out var isPureModel))
                        {
                            isPureModel = await IsPureModelClassAsync(roslynSolution, symbol.FilePath, containingType);
                            pureModelCache[containingType] = isPureModel;
                        }
                        if (isPureModel)
                        {
                            continue;
                        }
                    }
                }

                deadCode.Add(new DeadCodeEntry
                {
                    Name = symbol.Name,
                    QualifiedName = symbol.QualifiedName,
                    Kind = symbol.Kind.ToString(),
                    FilePath = symbol.FilePath,
                    FileName = Path.GetFileName(symbol.FilePath),
                    Line = symbol.Line
                });
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
            Note = deadCode.Count >= maxResults
                ? $"Results limited to {maxResults}. Use maxResults parameter to see more."
                : null
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

    private static readonly string[] definitionArray100 = new[] { "solutionPath" };
    private static readonly string[] definitionArray0 = new[] { "solutionPath", "symbolName" };

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
            isError = true
        };
    }
}

// DTOs for impact analysis
public class GraphImpactResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public GraphSymbolEntry? Symbol { get; set; }
    public int TotalAffectedSymbols { get; set; }
    public int TotalAffectedFiles { get; set; }
    public List<AffectedFile>? AffectedFiles { get; set; }
    public int MaxDepthReached { get; set; }
}

public class AffectedFile
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public List<AffectedSymbol> AffectedSymbols { get; set; } = [];
}

public class AffectedSymbol
{
    public string Name { get; set; } = "";
    public string QualifiedName { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Line { get; set; }
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
