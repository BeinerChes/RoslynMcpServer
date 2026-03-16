using RoslynMcpServer.Graph;
using RoslynMcpServer.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definitionArray19 = ["all", "type", "member", "namespace", "typeAndMember"];
    private static readonly string[] definitionArray20 = ["exact", "exactIgnoreCase", "contains", "prefix", "suffix"];
    private static readonly string[] definitionArray21 = ["pattern"];

    /// <summary>
    /// Finds symbols in a solution by name pattern.
    /// </summary>
    private static void RegisterFindSymbolTool(McpServer server)
    {
        server.RegisterTool(
            "FindSymbol",
            new ToolDefinition
            {
                Description = "Searches for symbols (types, methods, properties, fields) in a .NET solution by name. Returns fully qualified names, file locations, and signatures. Much faster and more accurate than text search. When no results are found, falls back to fuzzy matching via the graph database to suggest similar symbol names (requires prior GraphAnalyze).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pattern = new
                        {
                            type = "string",
                            description = "The symbol name pattern to search for"
                        },
                        symbolKind = new
                        {
                            type = "string",
                            description = "Kind of symbols to find: 'all', 'type', 'member', 'namespace', 'typeAndMember'. Default: 'all'",
                            @enum = new[] { "all", "type", "member", "namespace", "typeAndMember" }
                        },
                        matchType = new
                        {
                            type = "string",
                            description = "How to match the pattern: 'exact', 'exactIgnoreCase', 'contains', 'prefix', 'suffix'. Default: 'contains'",
                            @enum = new[] { "exact", "exactIgnoreCase", "contains", "prefix", "suffix" }
                        },
                        maxResults = new
                        {
                            type = "integer",
                            description = "Maximum number of results to return. Default: 100",
                            minimum = 1,
                            maximum = 1000
                        }
                    },
                    required = new[] { "pattern" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            HandleFindSymbolAsync);
    }

    /// <summary>
    /// Handler for the find_symbol tool.
    /// </summary>
    private static async Task<object> HandleFindSymbolAsync(JsonObject? args)
    {
        var (solutionPath, error) = GetSolutionPathOrError();
        if (error != null) return error;

        if (!TryGetRequiredString(args, "pattern", out var pattern, out error))
            return error!;

        var symbolKind = ParseSymbolKindFilter(GetOptionalString(args, "symbolKind", "all"));
        var matchType = ParseMatchType(GetOptionalString(args, "matchType", "contains"));
        var maxResults = GetOptionalInt(args, "maxResults", 100);

        var result = await _analyzerService!.SearchSymbolsAsync(
            solutionPath!, pattern, symbolKind, matchType, maxResults);

        // Fuzzy fallback: when no results, suggest similar names from graph DB
        List<string>? suggestions = null;
        if (result.TotalFound == 0)
        {
            suggestions = await GetFuzzySuggestionsAsync(solutionPath!, pattern);
        }

        var response = await BuildFindSymbolResponseAsync(solutionPath!, result, suggestions);
        return CreateSuccessResponse(response, !result.Success);
    }

    /// <summary>
    /// Builds the response for find_symbol, including knowledge flags.
    /// </summary>
    private static async Task<object> BuildFindSymbolResponseAsync(string solutionPath, FindSymbolResult result, List<string>? suggestions = null)
    {
        if (!result.Success || result.Symbols == null || result.Symbols.Count == 0)
        {
            if (suggestions != null && suggestions.Count > 0)
            {
                return new
                {
                    count = 0,
                    symbols = Array.Empty<string>(),
                    suggestions
                };
            }

            return new
            {
                count = 0,
                symbols = Array.Empty<string>()
            };
        }

        var solutionDir = Path.GetDirectoryName(solutionPath) ?? "";

        // Format: "Namespace.Type.Member (Kind) path:line"
        var compactSymbols = result.Symbols.Select(s =>
        {
            var relativePath = GetRelativePath(s.FilePath ?? "", solutionPath);
            return $"{s.FullyQualifiedName} ({s.Kind}) {relativePath}:{s.Line}";
        }).ToList();

        return new
        {
            count = result.TotalFound,
            symbols = compactSymbols
        };
    }

    /// <summary>
    /// Queries the graph database for fuzzy symbol name suggestions when FindSymbol returns no results.
    /// </summary>
    private static async Task<List<string>?> GetFuzzySuggestionsAsync(string solutionPath, string pattern)
    {
        using var db = new GraphDatabase(solutionPath);
        if (!db.Exists()) return null;

        await db.OpenAsync();
        var solution = await db.GetSolutionAsync(solutionPath);
        if (solution == null) return null;

        var allSymbols = await db.GetAllSymbolsAsync(solution.Id);
        if (allSymbols.Count == 0) return null;

        var matches = FuzzyMatcher.FindSimilar(pattern, allSymbols);
        if (matches.Count == 0) return null;

        return matches.Select(m =>
        {
            var distance = FuzzyMatcher.LevenshteinDistance(pattern, m.Name);
            var isSubseq = FuzzyMatcher.IsSubsequence(pattern, m.Name);
            var matchType = isSubseq && distance > 3 ? "subsequence" : $"distance: {distance}";
            return $"{m.Name} ({matchType})";
        }).ToList();
    }
}
