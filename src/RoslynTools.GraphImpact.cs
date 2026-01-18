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
                    required = new[] { "solutionPath", "symbolName" }
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

    /// <summary>
    /// Registers the roslyn_find_dead_code tool.
    /// </summary>
    internal static void RegisterFindDeadCodeTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_find_dead_code",
            new ToolDefinition
            {
                Description = "Finds potentially dead code - methods and properties with no callers. Excludes common entry points (Main, event handlers, interface implementations). Use roslyn_graph_analyze first to build the graph.",
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
                        }
                    },
                    required = new[] { "solutionPath" }
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

                if (string.IsNullOrWhiteSpace(solutionPath))
                    return CreateToolError("Error: solutionPath is required");

                if (!File.Exists(solutionPath))
                    return CreateToolError($"Error: Solution file not found: {solutionPath}");

                var result = await FindDeadCodeAsync(solutionPath, includePrivate, includeTests, maxResults);
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
        string solutionPath, bool includePrivate, bool includeTests, int maxResults)
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
        var allSymbols = await db.GetSymbolsAsync(solution.Id, null, null);
        var candidateSymbols = allSymbols
            .Where(s => s.Kind is SymbolKind.Method or SymbolKind.Property)
            .Where(s => !IsEntryPoint(s))
            .Where(s => includePrivate || !IsPrivateSymbol(s))
            .Where(s => includeTests || !IsTestFile(s.FilePath))
            .ToList();

        var deadCode = new List<DeadCodeEntry>();

        foreach (var symbol in candidateSymbols)
        {
            if (deadCode.Count >= maxResults) break;

            var callers = await db.GetCallersAsync(symbol.Id, EdgeType.Calls);
            if (callers.Count == 0)
            {
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
