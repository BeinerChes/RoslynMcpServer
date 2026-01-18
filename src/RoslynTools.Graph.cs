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

                if (string.IsNullOrWhiteSpace(solutionPath))
                {
                    return CreateErrorResponse("Error: solutionPath is required");
                }

                if (!File.Exists(solutionPath))
                {
                    return CreateErrorResponse($"Error: Solution file not found: {solutionPath}");
                }

                var result = await GetGraphStatusAsync(solutionPath);
                return CreateJsonResponse(result);
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
                    required = new[] { "solutionPath" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    IdempotentHint = false
                }
            },
            async args =>
            {
                var solutionPath = args?["solutionPath"]?.GetValue<string>();
                var incremental = args?["incremental"]?.GetValue<bool>() ?? true;
                var projectFilter = args?["projectFilter"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(solutionPath))
                {
                    return CreateErrorResponse("Error: solutionPath is required");
                }

                if (!File.Exists(solutionPath))
                {
                    return CreateErrorResponse($"Error: Solution file not found: {solutionPath}");
                }

                var result = await AnalyzeGraphAsync(solutionPath, incremental, projectFilter);
                return CreateJsonResponse(result, !result.Success);
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
                            @enum = new[] { "callers", "callees", "both" }
                        },
                        maxDepth = new
                        {
                            type = "integer",
                            description = "Maximum recursion depth. -1 for unlimited. Default: 3",
                            minimum = -1,
                            maximum = 100
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
                var direction = args?["direction"]?.GetValue<string>() ?? "both";
                var maxDepth = args?["maxDepth"]?.GetValue<int>() ?? 3;

                if (string.IsNullOrWhiteSpace(solutionPath))
                {
                    return CreateErrorResponse("Error: solutionPath is required");
                }

                if (string.IsNullOrWhiteSpace(symbolName))
                {
                    return CreateErrorResponse("Error: symbolName is required");
                }

                var result = await QueryGraphAsync(solutionPath, symbolName, direction, maxDepth);
                return CreateJsonResponse(result, !result.Success);
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
        var roslynSolution = await _analyzerService!.LoadSolutionAsync(solutionPath);
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

        var symbol = await db.GetSymbolByQualifiedNameAsync(solution.Id, symbolName);
        if (symbol == null)
        {
            return new GraphQueryResult
            {
                Success = false,
                Error = $"Symbol '{symbolName}' not found in graph."
            };
        }

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
        var refreshedFiles = new List<string>();

        if (staleFiles.Count > 0 && _analyzerService != null)
        {
            var roslynSolution = await _analyzerService.LoadSolutionAsync(solutionPath);
            if (roslynSolution != null)
            {
                var analyzer = new GraphAnalyzer(db);
                foreach (var staleFilePath in staleFiles)
                {
                    var document = roslynSolution.Projects
                        .SelectMany(p => p.Documents)
                        .FirstOrDefault(d => d.FilePath == staleFilePath);

                    if (document != null)
                    {
                        await analyzer.AnalyzeDocumentAsync(document, solution.Id);
                        refreshedFiles.Add(Path.GetFileName(staleFilePath));
                    }
                }
            }
        }

        // Re-query to get fresh results if any files were refreshed
        if (refreshedFiles.Count > 0)
        {
            symbol = await db.GetSymbolByQualifiedNameAsync(solution.Id, symbolName);
            if (symbol == null)
            {
                return new GraphQueryResult
                {
                    Success = false,
                    Error = $"Symbol '{symbolName}' not found after refresh."
                };
            }
        }

        var result = new GraphQueryResult
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
            StaleFilesRefreshed = refreshedFiles.Count,
            RefreshedFiles = refreshedFiles.Count > 0 ? refreshedFiles : null
        };

        if (direction is "callers" or "both")
        {
            var callers = refreshedFiles.Count > 0
                ? await db.GetRecursiveCallersAsync(symbol.Id, maxDepth)
                : preliminaryCallers;
            result.Callers = callers.Select(c => new GraphSymbolEntry
            {
                Name = c.Name,
                QualifiedName = c.QualifiedName,
                Kind = c.Kind.ToString(),
                FilePath = c.FilePath,
                Line = c.Line
            }).ToList();
        }

        if (direction is "callees" or "both")
        {
            var callees = refreshedFiles.Count > 0
                ? await db.GetRecursiveCalleesAsync(symbol.Id, maxDepth)
                : preliminaryCallees;
            result.Callees = callees.Select(c => new GraphSymbolEntry
            {
                Name = c.Name,
                QualifiedName = c.QualifiedName,
                Kind = c.Kind.ToString(),
                FilePath = c.FilePath,
                Line = c.Line
            }).ToList();
        }

        return result;
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
