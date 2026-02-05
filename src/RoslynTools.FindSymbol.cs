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
                Description = "Searches for symbols (types, methods, properties, fields) in a .NET solution by name. Returns fully qualified names, file locations, and signatures. Much faster and more accurate than text search.",
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

        var response = await BuildFindSymbolResponseAsync(solutionPath!, result);
        return CreateSuccessResponse(response, !result.Success);
    }

    /// <summary>
    /// Builds the response for find_symbol, including knowledge flags.
    /// </summary>
    private static async Task<object> BuildFindSymbolResponseAsync(string solutionPath, FindSymbolResult result)
    {
        if (!result.Success || result.Symbols == null || result.Symbols.Count == 0)
        {
            return new
            {
                count = 0,
                symbols = Array.Empty<string>()
            };
        }

        var knowledgeSymbols = await GetKnowledgeSymbolLinksAsync(solutionPath);
        var solutionDir = Path.GetDirectoryName(solutionPath) ?? "";

        // Format: "Namespace.Type.Member (Kind) path:line [K]"
        var compactSymbols = result.Symbols.Select(s =>
        {
            var relativePath = GetRelativePath(s.FilePath ?? "", solutionPath);
            var hasKnowledge = HasKnowledgeEntry(s.FullyQualifiedName, knowledgeSymbols);
            var knowledgeFlag = hasKnowledge ? " [K]" : "";
            return $"{s.FullyQualifiedName} ({s.Kind}) {relativePath}:{s.Line}{knowledgeFlag}";
        }).ToList();

        return new
        {
            count = result.TotalFound,
            symbols = compactSymbols
        };
    }
}
