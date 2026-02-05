using RoslynMcpServer.Graph;

namespace RoslynMcpServer.Web;

/// <summary>
/// API endpoints for graph visualization.
/// </summary>
public static class GraphApi
{
    /// <summary>
    /// System namespace prefixes to filter out from visualization.
    /// </summary>
    private static readonly string[] SystemPrefixes =
    [
        "System.",
        "Microsoft.",
        "Newtonsoft.",
        "NUnit.",
        "Xunit.",
        "Moq.",
        "Castle.",
        "AutoMapper.",
        "FluentAssertions.",
        "Dapper.",
        "Serilog.",
        "NLog.",
        "log4net.",
        "string.",
        "int.",
        "bool.",
        "double.",
        "float.",
        "decimal.",
        "object.",
        "byte.",
        "char.",
        "long.",
        "short.",
        "uint.",
        "ulong.",
        "ushort.",
        "sbyte."
    ];

    /// <summary>
    /// Checks if a symbol is from a system/framework library.
    /// </summary>
    private static bool IsSystemSymbol(string? qualifiedName)
    {
        if (string.IsNullOrEmpty(qualifiedName))
            return false;

        foreach (var prefix in SystemPrefixes)
        {
            if (qualifiedName.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        // Also filter symbols with "external" file path (framework refs)
        return false;
    }

    private static readonly string LastSymbolFilePath = Path.Combine(
        Path.GetTempPath(),
        "roslyn-mcp-last-symbol.json");


    private static readonly System.Text.Json.JsonSerializerOptions JsonDeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    public static void MapGraphApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/solutions", (Delegate)GetSolutions);
        api.MapGet("/projects", (Delegate)GetProjects);
        api.MapGet("/graph", (Delegate)GetGraph);
        api.MapGet("/graph/node/{id:long}", GetNode);
        api.MapGet("/last-symbol", GetLastSymbol);
    }

    /// <summary>
    /// Gets the last symbol processed by the MCP server (for meditation mode).
    /// </summary>
    private static IResult GetLastSymbol()
    {
        try
        {
            if (!File.Exists(LastSymbolFilePath))
            {
                return Results.Ok(new { exists = false });
            }

            var json = File.ReadAllText(LastSymbolFilePath);
            var state = System.Text.Json.JsonSerializer.Deserialize<LastSymbolState>(json, JsonDeserializeOptions);

            if (state == null)
            {
                return Results.Ok(new { exists = false });
            }

            return Results.Ok(new
            {
                exists = true,
                solutionPath = state.SolutionPath,
                qualifiedName = state.QualifiedName,
                kind = state.Kind,
                timestamp = state.Timestamp
            });
        }
        catch
        {
            return Results.Ok(new { exists = false });
        }
    }

    private class LastSymbolState
    {
        public string SolutionPath { get; set; } = "";
        public string QualifiedName { get; set; } = "";
        public string? Kind { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Lists all solutions with graph databases.
    /// </summary>
    private static async Task<IResult> GetSolutions(HttpContext context)
    {
        var solutionPath = context.Request.Query["solutionPath"].FirstOrDefault();

        if (!IsValidSolutionPath(solutionPath, out var validatedPath, out var errorMessage))
        {
            return errorMessage!.Contains("not found")
                ? Results.NotFound(new { error = errorMessage })
                : Results.BadRequest(new { error = errorMessage });
        }

        using var db = new GraphDatabase(validatedPath!);
        if (!db.Exists())
        {
            return Results.Ok(new { exists = false, message = "No graph database exists. Run GraphAnalyze first." });
        }

        await db.OpenAsync();
        var solution = await db.GetSolutionAsync(validatedPath!);

        if (solution == null)
        {
            return Results.Ok(new { exists = false, message = "Solution not found in graph database." });
        }

        var stats = await db.GetStatsAsync(solution.Id);

        return Results.Ok(new
        {
            exists = true,
            solution = new
            {
                solution.Id,
                solution.Path,
                solution.Name,
                solution.LastAnalyzed
            },
            stats
        });
    }

    /// <summary>
    /// Gets list of projects (extracted from file paths).
    /// </summary>
    private static async Task<IResult> GetProjects(HttpContext context)
    {
        var solutionPath = context.Request.Query["solutionPath"].FirstOrDefault();

        if (!IsValidSolutionPath(solutionPath, out var validatedPath, out var errorMessage))
        {
            return errorMessage!.Contains("not found")
                ? Results.NotFound(new { error = errorMessage })
                : Results.BadRequest(new { error = errorMessage });
        }

        using var db = new GraphDatabase(validatedPath!);
        if (!db.Exists())
        {
            return Results.NotFound(new { error = "No graph database exists." });
        }

        await db.OpenAsync();
        var solution = await db.GetSolutionAsync(validatedPath!);

        if (solution == null)
        {
            return Results.NotFound(new { error = "Solution not found." });
        }

        var projects = await db.GetProjectsAsync(solution.Id);
        return Results.Ok(new { projects });
    }

    /// <summary>
    /// Gets the full graph for visualization.
    /// </summary>
    private static async Task<IResult> GetGraph(HttpContext context)
    {
        var solutionPath = context.Request.Query["solutionPath"].FirstOrDefault();
        var projectFilter = context.Request.Query["project"].FirstOrDefault();

        if (!IsValidSolutionPath(solutionPath, out var validatedPath, out var errorMessage))
        {
            return errorMessage!.Contains("not found")
                ? Results.NotFound(new { error = errorMessage })
                : Results.BadRequest(new { error = errorMessage });
        }

        using var db = new GraphDatabase(validatedPath!);
        if (!db.Exists())
        {
            return Results.NotFound(new { error = "No graph database exists. Run GraphAnalyze first." });
        }

        await db.OpenAsync();
        var solution = await db.GetSolutionAsync(validatedPath!);

        if (solution == null)
        {
            return Results.NotFound(new { error = "Solution not found in graph database." });
        }

        var allSymbols = await db.GetAllSymbolsAsync(solution.Id, projectFilter);

        // Filter out system/framework symbols and auto-generated files - only show user code
        var symbols = allSymbols.Where(s =>
            !IsSystemSymbol(s.QualifiedName) &&
            !s.FilePath.Contains("external", StringComparison.OrdinalIgnoreCase) &&
            !s.FilePath.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) &&
            !s.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) &&
            !s.FilePath.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)).ToList();
        var symbolIds = symbols.Select(s => s.Id).ToHashSet();

        var edges = await db.GetAllEdgesAsync(solution.Id, projectFilter);

        // Filter edges to only include those where both endpoints are in the filtered symbol set
        var filteredEdges = edges.Where(e => symbolIds.Contains(e.FromSymbolId) && symbolIds.Contains(e.ToSymbolId)).ToList();

        // Create member nodes
        var memberNodes = symbols.Select(s => new
        {
            id = s.Id,
            name = s.Name,
            qualifiedName = s.QualifiedName,
            kind = s.Kind.ToString().ToLowerInvariant(),
            file = s.FilePath,
            line = s.Line,
            status = s.Status.ToString().ToLowerInvariant()
        }).ToList();

        // Extract unique types from qualified names and create synthetic type nodes
        var typeNodes = new List<object>();
        var seenTypes = new HashSet<string>();
        long syntheticId = -1;

        foreach (var symbol in symbols)
        {
            var qn = symbol.QualifiedName ?? symbol.Name;
            var lastDot = qn.LastIndexOf('.');
            if (lastDot > 0)
            {
                var typeQualifiedName = qn[..lastDot];
                if (seenTypes.Add(typeQualifiedName))
                {
                    var typeLastDot = typeQualifiedName.LastIndexOf('.');
                    var typeName = typeLastDot >= 0 ? typeQualifiedName[(typeLastDot + 1)..] : typeQualifiedName;

                    typeNodes.Add(new
                    {
                        id = syntheticId--,
                        name = typeName,
                        qualifiedName = typeQualifiedName,
                        kind = "type",
                        file = symbol.FilePath,
                        line = 1,
                        status = "active"
                    });
                }
            }
        }

        // Combine type nodes and member nodes
        var allNodes = typeNodes.Concat(memberNodes).ToList();

        var links = filteredEdges.Select(e => new
        {
            source = e.FromSymbolId,
            target = e.ToSymbolId,
            type = e.EdgeType.ToString().ToLowerInvariant()
        }).ToList();

        return Results.Ok(new
        {
            solution = new { solution.Name, solution.Path },
            nodes = allNodes,
            links
        });
    }

    /// <summary>
    /// Gets details for a specific node.
    /// </summary>
    private static async Task<IResult> GetNode(long id, HttpContext context)
    {
        var solutionPath = context.Request.Query["solutionPath"].FirstOrDefault();

        if (!IsValidSolutionPath(solutionPath, out var validatedPath, out var errorMessage))
        {
            return errorMessage!.Contains("not found")
                ? Results.NotFound(new { error = errorMessage })
                : Results.BadRequest(new { error = errorMessage });
        }

        using var db = new GraphDatabase(validatedPath!);
        if (!db.Exists())
        {
            return Results.NotFound(new { error = "No graph database exists." });
        }

        await db.OpenAsync();
        var symbol = await db.GetSymbolByIdAsync(id);

        if (symbol == null)
        {
            return Results.NotFound(new { error = "Symbol not found." });
        }

        var callers = await db.GetCallersAsync(id);
        var callees = await db.GetCalleesAsync(id);

        return Results.Ok(new
        {
            symbol = new
            {
                symbol.Id,
                symbol.Name,
                symbol.QualifiedName,
                kind = symbol.Kind.ToString().ToLowerInvariant(),
                symbol.FilePath,
                symbol.Line,
                symbol.Column,
                status = symbol.Status.ToString().ToLowerInvariant()
            },
            callers = callers.Select(c => new { c.Id, c.Name, c.QualifiedName }),
            callees = callees.Select(c => new { c.Id, c.Name, c.QualifiedName })
        });
    }


    internal static bool IsValidSolutionPath(string? userPath, out string? normalizedPath, out string? errorMessage)
    {
        normalizedPath = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(userPath))
        {
            errorMessage = "solutionPath query parameter is required";
            return false;
        }

        try
        {
            // Normalize the path to resolve any ../ sequences
            var fullPath = Path.GetFullPath(userPath);

            // Validate file extension - only allow solution files
            var extension = Path.GetExtension(fullPath);
            if (!extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Only .sln and .slnx files are allowed";
                return false;
            }

            // Check if file exists
            if (!File.Exists(fullPath))
            {
                errorMessage = "Solution file not found";
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            errorMessage = "Invalid path format";
            return false;
        }
    }
}
