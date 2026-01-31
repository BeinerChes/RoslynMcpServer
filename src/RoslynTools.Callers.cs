using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynMcpServer.Graph;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definitionArray1 = ["filePath", "line", "column"];

    /// <summary>
    /// Finds all callers of a method at a given position.
    /// Uses graph cache when available, with automatic staleness detection and refresh.
    /// </summary>
    private static void RegisterGetCallersTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_get_callers",
            new ToolDefinition
            {
                Description = "Finds all callers of a method/property at a given file position. Unlike get_references, this returns only actual call sites - not declarations, docs, or type references. Essential for understanding execution flow and impact analysis before refactoring.",
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
                        filePath = new
                        {
                            type = "string",
                            description = "Absolute path to the source file containing the method"
                        },
                        line = new
                        {
                            type = "integer",
                            description = "Line number (1-based) where the method is located",
                            minimum = 1
                        },
                        column = new
                        {
                            type = "integer",
                            description = "Column number (1-based) where the method is located",
                            minimum = 1
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
                    required = definitionArray1
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

        if (!TryGetRequiredString(args, "filePath", out var filePath, out var error))
            return error!;

        if (!TryGetRequiredInt(args, "line", 1, out var line, out error))
            return error!;

        if (!TryGetRequiredInt(args, "column", 1, out var column, out error))
            return error!;

        var maxResults = GetOptionalInt(args, "maxResults", 100);
        var offset = GetOptionalInt(args, "offset", 0);
        var projectFilter = args?["projectFilter"]?.GetValue<string>();
        var fileFilter = args?["fileFilter"]?.GetValue<string>();

        GetCallersResult? result = null;

        // Try graph cache first
        var graphResult = await TryGetCallersFromGraphAsync(
            solutionPath!, filePath, line, column, maxResults, offset, projectFilter, fileFilter);

        if (graphResult != null)
        {
            result = graphResult;
        }
        else
        {
            // Fall back to live analysis
            var liveResult = await SolutionAnalyzerService.GetCallersAsync(
                solutionPath!, filePath, line, column, maxResults, offset, projectFilter, fileFilter);

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
                isError = true
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
        string filePath,
        int line,
        int column,
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

        // Get the symbol's qualified name from Roslyn
        var roslynSolution = await SolutionAnalyzerService.LoadSolutionAsync(solutionPath);
        if (roslynSolution == null) return null;

        var qualifiedName = await SolutionAnalyzerService.GetSymbolQualifiedNameAsync(solutionPath, filePath, line, column);
        if (string.IsNullOrEmpty(qualifiedName)) return null;

        // Look up symbol in graph
        var symbol = await db.GetSymbolByQualifiedNameAsync(solution.Id, qualifiedName);
        if (symbol == null) return null;

        // Check for stale files and refresh
        var callers = await db.GetCallersAsync(symbol.Id);
        var filesToCheck = new HashSet<string> { symbol.FilePath };
        foreach (var caller in callers)
        {
            if (!string.IsNullOrEmpty(caller.FilePath) && caller.FilePath != "external")
                filesToCheck.Add(caller.FilePath);
        }

        var staleFiles = await db.GetStaleFilesAsync(solution.Id, filesToCheck);
        var refreshedCount = 0;

        if (staleFiles.Count > 0)
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
                    refreshedCount++;
                }
            }

            // Re-query callers after refresh
            callers = await db.GetCallersAsync(symbol.Id);
        }

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
        LastSymbolTracker.Track(solutionPath, qualifiedName, "member");

        return new GetCallersResult
        {
            Success = true,
            Symbol = qualifiedName,
            TotalCallers = totalCallers,
            ReturnedCount = paginatedCallers.Count,
            Source = refreshedCount > 0 ? "graph+refresh" : "graph",
            StaleFilesRefreshed = refreshedCount,
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
