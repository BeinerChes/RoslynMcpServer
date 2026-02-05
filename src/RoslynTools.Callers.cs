using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynMcpServer.Graph;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Finds all callers of a method/property by name.
    /// Uses graph cache when available, with automatic staleness detection and refresh.
    /// </summary>
    private static void RegisterGetCallersTool(McpServer server)
    {
        server.RegisterTool(
            "GetCallers",
            new ToolDefinition
            {
                Description = "Finds all callers of a method/property by name. Unlike get_references, this returns only actual call sites - not declarations, docs, or type references. Essential for understanding execution flow and impact analysis before refactoring.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        symbolName = new
                        {
                            type = "string",
                            description = "Name of the method/property to find callers of. Supports: 'MethodName', 'Type.Method', or 'Namespace.Type.Method'"
                        },
                        maxResults = new
                        {
                            type = "integer",
                            description = "Maximum number of callers to return. Default: 100",
                            minimum = 1,
                            maximum = 1000
                        },
                        offset = new
                        {
                            type = "integer",
                            description = "Skip first N results for pagination. Default: 0",
                            minimum = 0
                        },
                        projectFilter = new
                        {
                            type = "string",
                            description = "Filter by project name. Supports wildcards (*). Example: 'MyApp.*'"
                        },
                        fileFilter = new
                        {
                            type = "string",
                            description = "Filter by file path. Supports wildcards (*). Example: '*Service.cs'"
                        }
                    },
                    required = new[] { "symbolName" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            HandleGetCallersAsync);
    }

    /// <summary>
    /// Handler for the get_callers tool.
    /// </summary>
    private static async Task<object> HandleGetCallersAsync(JsonObject? args)
    {
        var (solutionPath, solutionError) = GetSolutionPathOrError();
        if (solutionError != null) return solutionError;

        if (!TryGetRequiredString(args, "symbolName", out var symbolName, out var error))
            return error!;

        var maxResults = GetOptionalInt(args, "maxResults", 100);
        var offset = GetOptionalInt(args, "offset", 0);
        var projectFilter = args?["projectFilter"]?.GetValue<string>();
        var fileFilter = args?["fileFilter"]?.GetValue<string>();

        GetCallersResult? result = null;

        // Try graph cache first
        try
        {
            var graphResult = await TryGetCallersFromGraphAsync(
                solutionPath!, symbolName, maxResults, offset, projectFilter, fileFilter);

            if (graphResult != null && graphResult.TotalCallers > 0)
            {
                result = graphResult;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Graph cache failed, falling back to live analysis: {ex.Message}");
        }

        if (result == null)
        {
            // Fall back to live analysis when graph is unavailable OR returns 0 callers.
            // Graph may miss callers (e.g., stale graph), so live Roslyn is the source of truth.
            var liveResult = await SolutionAnalyzerService.GetCallersAsync(
                solutionPath!, symbolName, maxResults, offset, projectFilter, fileFilter);

            result = new GetCallersResult
            {
                Success = liveResult.Success,
                Error = liveResult.Error,
                Symbol = liveResult.Symbol,
                TotalCallers = liveResult.TotalCallers,
                ReturnedCount = liveResult.ReturnedCount,
                Source = "live",
                Callers = liveResult.Callers
            };
        }

        if (!result.Success)
        {
            return new
            {
                content = new[]
                {
                    new { type = "text", text = result.Error ?? "Failed to find callers" }
                },
                isError = false
            };
        }

        // Build compact response: "Type.Method file:line"
        var compactCallers = result.Callers?.Select(c =>
        {
            var caller = string.IsNullOrEmpty(c.Type) ? c.Method : $"{c.Type}.{c.Method}";
            return $"{caller} {c.File}:{c.Line}";
        }).ToList() ?? new List<string>();

        var compactResult = new
        {
            symbol = result.Symbol ?? "unknown",
            count = result.TotalCallers,
            callers = compactCallers
        };

        return new
        {
            content = new[]
            {
                new { type = "text", text = JsonSerializer.Serialize(compactResult, JsonOptions) }
            },
            isError = false
        };
    }

    /// <summary>
    /// Tries to get callers from the graph cache.
    /// Returns null if graph doesn't exist or symbol not found.
    /// </summary>
    private static async Task<GetCallersResult?> TryGetCallersFromGraphAsync(
        string solutionPath,
        string symbolName,
        int maxResults,
        int offset,
        string? projectFilter,
        string? fileFilter)
    {
        using var db = new GraphDatabase(solutionPath);
        if (!db.Exists()) return null;

        await db.OpenAsync();
        var solution = await db.GetSolutionAsync(solutionPath);
        if (solution == null) return null;

        // Ensure graph is fresh before querying (blocking)
        var freshnessResult = await EnsureGraphFreshAsync(db, solution.Id, solutionPath);

        // Look up symbol in graph by name
        var searchResult = await db.FindSymbolAsync(solution.Id, symbolName);
        if (searchResult.Symbol == null)
        {
            // Return error with candidates if ambiguous
            if (searchResult.Candidates?.Count > 0)
            {
                return new GetCallersResult
                {
                    Success = false,
                    Error = searchResult.Error
                };
            }
            return null; // Not found, fall back to live
        }

        var symbol = searchResult.Symbol;

        // Get callers from fresh graph
        var callers = await db.GetCallersAsync(symbol.Id);

        // Apply filters
        var filteredCallers = callers.AsEnumerable();

        if (!string.IsNullOrEmpty(projectFilter))
        {
            var pattern = projectFilter.Replace("*", "");
            filteredCallers = filteredCallers.Where(c =>
                c.FilePath.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(fileFilter))
        {
            var pattern = fileFilter.Replace("*", "");
            filteredCallers = filteredCallers.Where(c =>
                Path.GetFileName(c.FilePath).Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        var callerList = filteredCallers.ToList();
        var totalCallers = callerList.Count;

        // Apply pagination
        var paginatedCallers = callerList
            .Skip(offset)
            .Take(maxResults)
            .Select(c => new CallerInfo
            {
                File = GetRelativePath(c.FilePath, solutionPath),
                Line = c.Line,
                Method = c.Name,
                Type = ExtractTypeName(c.QualifiedName)
            })
            .ToList();

        // Track for visualization sync
        LastSymbolTracker.Track(solutionPath, symbol.QualifiedName, "member");

        return new GetCallersResult
        {
            Success = true,
            Symbol = symbol.QualifiedName,
            TotalCallers = totalCallers,
            ReturnedCount = paginatedCallers.Count,
            Source = freshnessResult.FilesReanalyzed > 0 ? "graph+refresh" : "graph",
            StaleFilesRefreshed = freshnessResult.FilesReanalyzed,
            Callers = paginatedCallers
        };
    }

    private static string GetRelativePath(string filePath, string solutionPath)
    {
        var solutionDir = Path.GetDirectoryName(solutionPath);
        if (string.IsNullOrEmpty(solutionDir)) return filePath;

        if (filePath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase))
        {
            return filePath.Substring(solutionDir.Length).TrimStart(Path.DirectorySeparatorChar);
        }
        return filePath;
    }

    private static string? ExtractTypeName(string qualifiedName)
    {
        // Extract type name from qualified name like "Namespace.Type.Method(params)"
        var parenIndex = qualifiedName.IndexOf('(');
        var nameWithoutParams = parenIndex >= 0 ? qualifiedName.Substring(0, parenIndex) : qualifiedName;

        var lastDot = nameWithoutParams.LastIndexOf('.');
        if (lastDot <= 0) return null;

        var beforeLastDot = nameWithoutParams.Substring(0, lastDot);
        var secondLastDot = beforeLastDot.LastIndexOf('.');
        if (secondLastDot < 0) return beforeLastDot;

        return beforeLastDot.Substring(secondLastDot + 1);
    }
}
