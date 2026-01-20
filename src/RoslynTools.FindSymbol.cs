using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynMcpServer.Graph;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Finds symbols in a solution by name pattern.
    /// </summary>
    private static void RegisterFindSymbolTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_find_symbol",
            new ToolDefinition
            {
                Description = "Searches for symbols (types, methods, properties, fields) in a .NET solution by name. Returns fully qualified names, file locations, and signatures. Much faster and more accurate than text search.",
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
                        },
                        compact = new
                        {
                            type = "boolean",
                            description = "Return minimal fields only (name, qualifiedName, kind, file, line). Default: true. Set to false for detailed info (column, containingType, accessibility, isStatic, signature)"
                        }
                    },
                    required = new[] { "solutionPath", "pattern" }
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
                var pattern = args?["pattern"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(solutionPath))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: solutionPath is required" }
                        },
                        isError = true
                    };
                }

                if (string.IsNullOrWhiteSpace(pattern))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: pattern is required" }
                        },
                        isError = true
                    };
                }

                // Parse optional parameters
                var symbolKindStr = args?["symbolKind"]?.GetValue<string>() ?? "all";
                var matchTypeStr = args?["matchType"]?.GetValue<string>() ?? "contains";
                var maxResults = args?["maxResults"]?.GetValue<int>() ?? 100;
                var compact = args?["compact"]?.GetValue<bool>() ?? true;

                var symbolKind = symbolKindStr.ToLowerInvariant() switch
                {
                    "type" => SymbolKindFilter.Type,
                    "member" => SymbolKindFilter.Member,
                    "namespace" => SymbolKindFilter.Namespace,
                    "typeandmember" => SymbolKindFilter.TypeAndMember,
                    _ => SymbolKindFilter.All
                };

                var matchType = matchTypeStr.ToLowerInvariant() switch
                {
                    "exact" => MatchType.Exact,
                    "exactignorecase" => MatchType.ExactIgnoreCase,
                    "prefix" => MatchType.Prefix,
                    "suffix" => MatchType.Suffix,
                    _ => MatchType.Contains
                };

                var result = await _analyzerService!.SearchSymbolsAsync(
                    solutionPath,
                    pattern,
                    symbolKind,
                    matchType,
                    maxResults,
                    compact);

                // Check which symbols have knowledge entries
                HashSet<string>? symbolsWithKnowledge = null;
                if (result.Success && result.Symbols?.Count > 0)
                {
                    try
                    {
                        var db = await GetKnowledgeDatabaseAsync(solutionPath);
                        symbolsWithKnowledge = new HashSet<string>();

                        // Get all symbol links from the knowledge database
                        var allEntries = await db.ListEntriesAsync(limit: 1000);
                        foreach (var entry in allEntries)
                        {
                            foreach (var link in entry.SymbolLinks)
                            {
                                symbolsWithKnowledge.Add(link);
                            }
                        }
                    }
                    catch
                    {
                        // Knowledge lookup failure shouldn't break the main functionality
                    }
                }

                // Build response with knowledge flags
                object response;
                if (symbolsWithKnowledge != null && symbolsWithKnowledge.Count > 0 && result.Symbols != null)
                {
                    var symbolsWithFlags = result.Symbols.Select(s => new
                    {
                        s.Name,
                        s.FullyQualifiedName,
                        s.Kind,
                        s.FilePath,
                        s.Line,
                        s.Column,
                        s.ContainingType,
                        s.Accessibility,
                        s.IsStatic,
                        s.Signature,
                        HasKnowledge = s.FullyQualifiedName != null && (
                            symbolsWithKnowledge.Contains(s.FullyQualifiedName) ||
                            symbolsWithKnowledge.Any(k => s.FullyQualifiedName.StartsWith(k + ".") || k.StartsWith(s.FullyQualifiedName + ".")))
                    }).ToList();

                    response = new
                    {
                        result.Success,
                        result.Error,
                        result.SolutionPath,
                        result.Pattern,
                        result.TotalFound,
                        Symbols = symbolsWithFlags
                    };
                }
                else
                {
                    response = result;
                }

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(response, JsonOptions) }
                    },
                    isError = !result.Success
                };
            });
    }
}
