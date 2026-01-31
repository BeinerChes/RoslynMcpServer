using System.Text.Json;
using RoslynMcpServer.Graph;

namespace RoslynMcpServer;

/// <summary>
/// Graph-related MCP tools for call graph analysis.
/// </summary>
public static partial class RoslynTools
{
    /// <summary>
    /// Registers the roslyn_graph_status tool.
    /// </summary>
    internal static void RegisterGraphStatusTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_graph_status",
            new ToolDefinition
            {
                Description = "Gets the status of the call graph database for a solution. Returns whether a graph exists, symbol counts, edge counts, and coverage statistics.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        solutionPath = new
                        {
                            type = "string",
                            description = "Absolute path to the .sln or .slnx solution file"
                        }
                    },
                    required = definitionArray22
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

                var result = await GetGraphStatusAsync(solutionPath!);

                if (!result.Success || !result.GraphExists)
                {
                    return CreateJsonResponse(new
                    {
                        exists = false,
                        message = result.Message ?? "No graph database found. Run roslyn_graph_analyze first."
                    });
                }

                // Compact format
                var compactResult = new
                {
                    exists = true,
                    lastAnalyzed = result.LastAnalyzed?.ToString("yyyy-MM-dd HH:mm:ss"),
                    symbols = result.SymbolStats != null ? $"{result.SymbolStats.Analyzed}/{result.SymbolStats.Total} ({result.SymbolStats.Dirty} dirty)" : null,
                    edges = result.EdgeStats?.Total
                };

                return CreateJsonResponse(compactResult);
            });
    }

    /// <summary>
    /// Registers the roslyn_graph_analyze tool.
    /// </summary>
    internal static void RegisterGraphAnalyzeTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_graph_analyze",
            new ToolDefinition
            {
                Description = "Analyzes a solution and builds/updates the call graph. Can perform full analysis or incremental update for changed files only.",
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
                        incremental = new
                        {
                            type = "boolean",
                            description = "If true, only analyze files that have changed. Default: true"
                        },
                        projectFilter = new
                        {
                            type = "string",
                            description = "Optional: filter by project name (partial match)"
                        }
                    },
                    required = definitionArray23
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    IdempotentHint = false
                }
            },
            async args =>
            {
                var (solutionPath, solutionError) = GetSolutionPathOrError();
                if (solutionError != null) return solutionError;

                var incremental = args?["incremental"]?.GetValue<bool>() ?? true;
                var projectFilter = args?["projectFilter"]?.GetValue<string>();

                var result = await AnalyzeGraphAsync(solutionPath!, incremental, projectFilter);

                if (!result.Success)
                {
                    return CreateJsonResponse(new { error = result.Error }, true);
                }

                // Compact format
                var compactResult = new
                {
                    documents = result.DocumentsAnalyzed,
                    symbols = result.SymbolsFound,
                    edges = result.EdgesFound
                };

                return CreateJsonResponse(compactResult);
            });
    }

    /// <summary>
    /// Registers the roslyn_query_graph tool.
    /// </summary>
    internal static void RegisterQueryGraphTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_query_graph",
            new ToolDefinition
            {
                Description = "Queries the call graph for a symbol. Returns callers and/or callees with optional recursive depth.",
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
                            description = "Qualified name of the symbol to query (e.g., 'MyNamespace.MyClass.MyMethod')"
                        },
                        direction = new
                        {
                            type = "string",
                            description = "Query direction: 'callers', 'callees', or 'both'. Default: 'both'",
                            @enum = definitionArray24
                        },
                        maxDepth = new
                        {
                            type = "integer",
                            description = "Maximum recursion depth. -1 for unlimited. Default: 3",
                            minimum = -1,
                            maximum = 100
                        }
                    },
                    required = definitionArray25
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

                var symbolName = args?["symbolName"]?.GetValue<string>();
                var direction = args?["direction"]?.GetValue<string>() ?? "both";
                var maxDepth = args?["maxDepth"]?.GetValue<int>() ?? 3;

                if (string.IsNullOrWhiteSpace(symbolName))
                {
                    return CreateErrorResponse("Error: symbolName is required");
                }

                var result = await QueryGraphAsync(solutionPath!, symbolName, direction, maxDepth);

                if (!result.Success)
                {
                    return CreateJsonResponse(new { error = result.Error }, true);
                }

                // Compact format: "QualifiedName file:line"
                var formatEntry = (GraphSymbolEntry e) => $"{e.QualifiedName} {e.FilePath}:{e.Line}";

                var compactResult = new Dictionary<string, object?>
                {
                    ["symbol"] = result.Symbol?.QualifiedName ?? symbolName
                };

                if (result.Callers != null && result.Callers.Count > 0)
                    compactResult["callers"] = result.Callers.Select(formatEntry).ToList();

                if (result.Callees != null && result.Callees.Count > 0)
                    compactResult["callees"] = result.Callees.Select(formatEntry).ToList();

                return CreateJsonResponse(compactResult);
            });
    }

    private static async Task<GraphStatusResult> GetGraphStatusAsync(string solutionPath)
    {
        using var db = new GraphDatabase(solutionPath);

        if (!db.Exists())
        {
            return new GraphStatusResult
            {
                Success = true,
                SolutionPath = solutionPath,
                GraphExists = false,
                Message = "No graph database exists for this solution. Use roslyn_graph_analyze to build one."
            };
        }

        await db.OpenAsync();
        var solution = await db.GetSolutionAsync(solutionPath);

        if (solution == null)
        {
            return new GraphStatusResult
            {
                Success = true,
                SolutionPath = solutionPath,
                GraphExists = true,
                Message = "Graph database exists but solution not yet analyzed."
            };
        }

        var symbolStats = await db.GetSymbolStatsAsync(solution.Id);
        var edgeStats = await db.GetEdgeStatsAsync(solution.Id);

        return new GraphStatusResult
        {
            Success = true,
            SolutionPath = solutionPath,
            GraphExists = true,
            LastAnalyzed = solution.LastAnalyzed,
            SymbolStats = symbolStats,
            EdgeStats = edgeStats
        };
    }

    private static async Task<GraphAnalyzeResult> AnalyzeGraphAsync(
        string solutionPath, bool incremental, string? projectFilter)
    {
        using var db = new GraphDatabase(solutionPath);
        await db.OpenAsync();

        var solution = await db.GetOrCreateSolutionAsync(solutionPath);
        var analyzer = new GraphAnalyzer(db);

        // Load the Roslyn solution
        var roslynSolution = await SolutionAnalyzerService.LoadSolutionAsync(solutionPath);
        if (roslynSolution == null)
        {
            return new GraphAnalyzeResult
            {
                Success = false,
                Error = "Failed to load solution"
            };
        }

        var documentsAnalyzed = 0;

        foreach (var project in roslynSolution.Projects)
        {
            if (!string.IsNullOrEmpty(projectFilter) &&
                !project.Name.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var document in project.Documents)
            {
                if (document.FilePath == null) continue;
                // Skip generated files
                if (document.FilePath.EndsWith(".g.cs") || document.FilePath.EndsWith(".designer.cs"))
                    continue;

                await analyzer.AnalyzeDocumentAsync(document, solution.Id);
                documentsAnalyzed++;
            }
        }

        await db.UpdateSolutionAnalyzedAsync(solution.Id);

        var stats = await db.GetSymbolStatsAsync(solution.Id);
        var edgeStats = await db.GetEdgeStatsAsync(solution.Id);

        return new GraphAnalyzeResult
        {
            Success = true,
            SolutionPath = solutionPath,
            DocumentsAnalyzed = documentsAnalyzed,
            SymbolsFound = stats.Total,
            EdgesFound = edgeStats.Total,
            SymbolStats = stats,
            EdgeStats = edgeStats
        };
    }

    private static readonly string[] definitionArray25 = new[] { "symbolName" };
    private static readonly string[] definitionArray24 = new[] { "callers", "callees", "both" };
    private static readonly string[] definitionArray23 = Array.Empty<string>();
    private static readonly string[] definitionArray22 = Array.Empty<string>();

    private static async Task<GraphQueryResult> QueryGraphAsync(
        string solutionPath, string symbolName, string direction, int maxDepth)
    {
        using var db = new GraphDatabase(solutionPath);

        if (!db.Exists())
        {
            return new GraphQueryResult
            {
                Success = false,
                Error = "No graph database exists. Use roslyn_graph_analyze first."
            };
        }

        await db.OpenAsync();
        var solution = await db.GetSolutionAsync(solutionPath);

        if (solution == null)
        {
            return new GraphQueryResult
            {
                Success = false,
                Error = "Solution not found in graph database."
            };
        }

        // Get solution directory for relative paths (Issue #115)
        var solutionDir = Path.GetDirectoryName(solutionPath) ?? "";

        // Use FindSymbolAsync for partial name matching (Issue #33)
        var searchResult = await db.FindSymbolAsync(solution.Id, symbolName);

        // Issue #35: If not found, try Roslyn search and analyze stale files
        if (searchResult.Symbol == null && _analyzerService != null)
        {
            var autoRefreshedFiles = await TryRefreshFilesForSymbolAsync(
                db, solution.Id, solutionPath, symbolName);

            if (autoRefreshedFiles.Count > 0)
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
            return new GraphQueryResult
            {
                Success = false,
                Error = error
            };
        }

        var symbol = searchResult.Symbol;

        // Collect all file paths to check for staleness
        var filesToCheck = new HashSet<string> { symbol.FilePath };

        // Get preliminary results to find all related files
        var preliminaryCallers = direction is "callers" or "both"
            ? await db.GetRecursiveCallersAsync(symbol.Id, maxDepth)
            : new List<SymbolRecord>();
        var preliminaryCallees = direction is "callees" or "both"
            ? await db.GetRecursiveCalleesAsync(symbol.Id, maxDepth)
            : new List<SymbolRecord>();

        foreach (var c in preliminaryCallers.Concat(preliminaryCallees))
        {
            if (!string.IsNullOrEmpty(c.FilePath) && c.FilePath != "external")
                filesToCheck.Add(c.FilePath);
        }

        // Check for stale files and refresh if needed
        var staleFiles = await db.GetStaleFilesAsync(solution.Id, filesToCheck);
        var refreshedFiles = await RefreshStaleFilesAsync(db, solution.Id, solutionPath, staleFiles);

        // Re-query to get fresh results if any files were refreshed
        if (refreshedFiles.Count > 0)
        {
            var refreshResult = await db.FindSymbolAsync(solution.Id, symbolName);
            if (refreshResult.Symbol == null)
            {
                return new GraphQueryResult
                {
                    Success = false,
                    Error = $"Symbol '{symbolName}' not found after refresh."
                };
            }
            symbol = refreshResult.Symbol;
        }

        var result = new GraphQueryResult
        {
            Success = true,
            Symbol = ToGraphSymbolEntry(symbol, solutionDir),
            StaleFilesRefreshed = refreshedFiles.Count,
            RefreshedFiles = refreshedFiles.Count > 0 ? refreshedFiles : null
        };

        if (direction is "callers" or "both")
        {
            var callers = refreshedFiles.Count > 0
                ? await db.GetRecursiveCallersAsync(symbol.Id, maxDepth)
                : preliminaryCallers;
            result.Callers = callers.Select(s => ToGraphSymbolEntry(s, solutionDir)).ToList();
        }

        if (direction is "callees" or "both")
        {
            var callees = refreshedFiles.Count > 0
                ? await db.GetRecursiveCalleesAsync(symbol.Id, maxDepth)
                : preliminaryCallees;
            result.Callees = callees.Select(s => ToGraphSymbolEntry(s, solutionDir)).ToList();
        }

        return result;
    }

    /// <summary>
    /// Converts a SymbolRecord to a GraphSymbolEntry.
    /// </summary>
    private static GraphSymbolEntry ToGraphSymbolEntry(SymbolRecord symbol, string solutionDir) => new()
    {
        Name = symbol.Name,
        QualifiedName = symbol.QualifiedName,
        Kind = symbol.Kind.ToString(),
        FilePath = GetRelativePath(symbol.FilePath, solutionDir),
        Line = symbol.Line
    };

    /// <summary>
    /// Refreshes stale files in the graph database.
    /// </summary>
    private static async Task<List<string>> RefreshStaleFilesAsync(
        GraphDatabase db, long solutionId, string solutionPath, IReadOnlyList<string> staleFiles)
    {
        var refreshedFiles = new List<string>();
        if (staleFiles.Count == 0 || _analyzerService == null)
            return refreshedFiles;

        var roslynSolution = await SolutionAnalyzerService.LoadSolutionAsync(solutionPath);
        if (roslynSolution == null)
            return refreshedFiles;

        var analyzer = new GraphAnalyzer(db);
        foreach (var staleFilePath in staleFiles)
        {
            var document = roslynSolution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == staleFilePath);

            if (document != null)
            {
                await analyzer.AnalyzeDocumentAsync(document, solutionId);
                refreshedFiles.Add(Path.GetFileName(staleFilePath));
            }
        }

        return refreshedFiles;
    }

    private static object CreateJsonResponse(object result, bool isError = false)
    {
        return new
        {
            content = new[]
            {
                new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) }
            },
            isError
        };
    }

    private static object CreateErrorResponse(string message)
    {
        return new
        {
            content = new[] { new { type = "text", text = message } },
            isError = true
        };
    }

    /// <summary>
    /// Tries to find a symbol via Roslyn and refresh stale files containing it.
    /// Issue: #35
    /// </summary>
    private static async Task<List<string>> TryRefreshFilesForSymbolAsync(
        GraphDatabase db, long solutionId, string solutionPath, string symbolName)
    {
        var refreshedFiles = new List<string>();

        if (_analyzerService == null) return refreshedFiles;

        try
        {
            // Search for symbol using Roslyn
            var roslynResult = await _analyzerService.SearchSymbolsAsync(
                solutionPath,
                symbolName,
                SymbolKindFilter.TypeAndMember,
                MatchType.Contains,
                maxResults: 10,
                compact: true);

            if (!roslynResult.Success || roslynResult.Symbols == null || roslynResult.Symbols.Count == 0)
            {
                return refreshedFiles;
            }

            // Get unique file paths from Roslyn results
            var filePaths = roslynResult.Symbols
                .Where(s => !string.IsNullOrEmpty(s.FilePath))
                .Select(s => s.FilePath!)
                .Distinct()
                .ToList();

            if (filePaths.Count == 0) return refreshedFiles;

            // Compute current hashes for found files
            var currentHashes = new Dictionary<string, string>();
            foreach (var filePath in filePaths)
            {
                if (File.Exists(filePath))
                {
                    currentHashes[filePath] = GraphDatabase.ComputeFileHash(filePath);
                }
            }

            // Check which files need analysis
            var needsAnalysis = await db.GetFilesNeedingAnalysisAsync(solutionId, currentHashes);

            if (needsAnalysis.Count == 0) return refreshedFiles;

            // Load solution and analyze needed files
            var roslynSolution = await SolutionAnalyzerService.LoadSolutionAsync(solutionPath);
            if (roslynSolution == null) return refreshedFiles;

            var analyzer = new GraphAnalyzer(db);
            foreach (var filePath in needsAnalysis)
            {
                var document = roslynSolution.Projects
                    .SelectMany(p => p.Documents)
                    .FirstOrDefault(d => d.FilePath == filePath);

                if (document != null)
                {
                    await analyzer.AnalyzeDocumentAsync(document, solutionId);
                    refreshedFiles.Add(Path.GetFileName(filePath));
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error refreshing files for symbol '{symbolName}': {ex.Message}");
        }

        return refreshedFiles;
    }
}

// DTOs for graph tools
public class GraphStatusResult
{
    public bool Success { get; set; }
    public string? SolutionPath { get; set; }
    public bool GraphExists { get; set; }
    public DateTime? LastAnalyzed { get; set; }
    public string? Message { get; set; }
    public SymbolStats? SymbolStats { get; set; }
    public EdgeStats? EdgeStats { get; set; }
}

public class GraphAnalyzeResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? SolutionPath { get; set; }
    public int DocumentsAnalyzed { get; set; }
    public int SymbolsFound { get; set; }
    public int EdgesFound { get; set; }
    public SymbolStats? SymbolStats { get; set; }
    public EdgeStats? EdgeStats { get; set; }
}

public class GraphQueryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public GraphSymbolEntry? Symbol { get; set; }
    public List<GraphSymbolEntry>? Callers { get; set; }
    public List<GraphSymbolEntry>? Callees { get; set; }
    public int StaleFilesRefreshed { get; set; }
    public List<string>? RefreshedFiles { get; set; }
}

public class GraphSymbolEntry
{
    public string? Name { get; set; }
    public string? QualifiedName { get; set; }
    public string? Kind { get; set; }
    public string? FilePath { get; set; }
    public int Line { get; set; }
}
