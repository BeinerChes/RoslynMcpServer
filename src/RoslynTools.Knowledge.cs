using System.Text.Json;
using RoslynMcpServer.Graph;

namespace RoslynMcpServer;

/// <summary>
/// Knowledge base MCP tools for storing and retrieving code insights.
/// </summary>
public static partial class RoslynTools
{
    private static readonly Dictionary<string, (KnowledgeDatabase Db, SmartComponentsEmbeddingProvider Embedder)> _knowledgeDatabases = new();

    /// <summary>
    /// Gets or creates a KnowledgeDatabase for the specified solution.
    /// </summary>
    private static async Task<KnowledgeDatabase> GetKnowledgeDatabaseAsync(string solutionPath)
    {
        if (_knowledgeDatabases.TryGetValue(solutionPath, out var existing))
        {
            return existing.Db;
        }

        var db = new KnowledgeDatabase(solutionPath);
        var embedder = new SmartComponentsEmbeddingProvider();
        db.SetEmbeddingProvider(embedder);
        await db.OpenAsync();

        _knowledgeDatabases[solutionPath] = (db, embedder);
        return db;
    }

    /// <summary>
    /// Registers the roslyn_knowledge_add tool.
    /// </summary>
    internal static void RegisterKnowledgeAddTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_knowledge_add",
            new ToolDefinition
            {
                Description = "Adds a new knowledge entry about code patterns, gotchas, or insights. Knowledge entries can be linked to specific symbols for automatic retrieval.",
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
                        category = new
                        {
                            type = "string",
                            description = "Category of knowledge: 'gotcha', 'pattern', 'architecture', 'debugging', 'performance', 'security', 'testing', 'workaround'",
                            @enum = KnowledgeCategories.All
                        },
                        title = new
                        {
                            type = "string",
                            description = "Short title summarizing the knowledge (1-2 sentences)"
                        },
                        content = new
                        {
                            type = "string",
                            description = "Detailed explanation of the knowledge, including context and recommendations"
                        },
                        symbolLinks = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Optional: qualified symbol names to link this knowledge to (e.g., 'MyNamespace.MyClass.MyMethod')"
                        },
                        tags = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Optional: tags for categorization (e.g., 'caching', 'threading', 'ui')"
                        },
                        confidence = new
                        {
                            type = "number",
                            description = "Confidence level 0.0-1.0. Default: 1.0. Lower confidence for uncertain learnings.",
                            minimum = 0.0,
                            maximum = 1.0
                        }
                    },
                    required = new[] { "solutionPath", "category", "title", "content" }
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
                var category = args?["category"]?.GetValue<string>();
                var title = args?["title"]?.GetValue<string>();
                var content = args?["content"]?.GetValue<string>();
                var symbolLinks = args?["symbolLinks"]?.AsArray()?.Select(x => x?.GetValue<string>() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                var tags = args?["tags"]?.AsArray()?.Select(x => x?.GetValue<string>() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                var confidence = args?["confidence"]?.GetValue<double>() ?? 1.0;

                if (string.IsNullOrWhiteSpace(solutionPath))
                    return CreateErrorResponse("Error: solutionPath is required");

                if (!File.Exists(solutionPath))
                    return CreateErrorResponse($"Error: Solution file not found: {solutionPath}");

                if (string.IsNullOrWhiteSpace(category))
                    return CreateErrorResponse("Error: category is required");

                if (string.IsNullOrWhiteSpace(title))
                    return CreateErrorResponse("Error: title is required");

                if (string.IsNullOrWhiteSpace(content))
                    return CreateErrorResponse("Error: content is required");

                try
                {
                    var db = await GetKnowledgeDatabaseAsync(solutionPath);

                    var input = new AddKnowledgeInput
                    {
                        Category = category,
                        Title = title,
                        Content = content,
                        SymbolLinks = symbolLinks,
                        Tags = tags,
                        Confidence = confidence
                    };

                    var entry = await db.AddEntryAsync(input);

                    return CreateJsonResponse(new
                    {
                        success = true,
                        id = entry.Id,
                        message = $"Knowledge entry created with ID {entry.Id}",
                        entry = new
                        {
                            entry.Id,
                            entry.Category,
                            entry.Title,
                            entry.Content,
                            entry.SymbolLinks,
                            entry.Tags,
                            entry.Confidence,
                            hasEmbedding = entry.Embedding != null
                        }
                    });
                }
                catch (ArgumentException ex)
                {
                    return CreateErrorResponse($"Error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse($"Error adding knowledge: {ex.Message}");
                }
            });
    }

    /// <summary>
    /// Registers the roslyn_knowledge_search tool.
    /// </summary>
    internal static void RegisterKnowledgeSearchTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_knowledge_search",
            new ToolDefinition
            {
                Description = "Searches the knowledge base using semantic search. Combines symbol links (exact match), full-text search (keywords), and vector similarity (semantic meaning).",
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
                        query = new
                        {
                            type = "string",
                            description = "Search query (can be natural language, keywords, or description of the problem)"
                        },
                        symbols = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Optional: symbol names to search for exact matches"
                        },
                        limit = new
                        {
                            type = "integer",
                            description = "Maximum number of results to return. Default: 10",
                            minimum = 1,
                            maximum = 50
                        }
                    },
                    required = new[] { "solutionPath", "query" }
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
                var query = args?["query"]?.GetValue<string>();
                var symbols = args?["symbols"]?.AsArray()?.Select(x => x?.GetValue<string>() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToArray();
                var limit = args?["limit"]?.GetValue<int>() ?? 10;

                if (string.IsNullOrWhiteSpace(solutionPath))
                    return CreateErrorResponse("Error: solutionPath is required");

                if (!File.Exists(solutionPath))
                    return CreateErrorResponse($"Error: Solution file not found: {solutionPath}");

                if (string.IsNullOrWhiteSpace(query))
                    return CreateErrorResponse("Error: query is required");

                try
                {
                    var db = await GetKnowledgeDatabaseAsync(solutionPath);
                    var results = await db.SearchAsync(query, symbols, limit);

                    return CreateJsonResponse(new
                    {
                        success = true,
                        query,
                        resultCount = results.Count,
                        results = results.Select(r => new
                        {
                            r.Entry.Id,
                            r.Entry.Category,
                            r.Entry.Title,
                            r.Entry.Content,
                            r.Entry.SymbolLinks,
                            r.Entry.Tags,
                            r.Entry.Confidence,
                            r.Score,
                            r.MatchSource
                        })
                    });
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse($"Error searching knowledge: {ex.Message}");
                }
            });
    }

    /// <summary>
    /// Registers the roslyn_knowledge_list tool.
    /// </summary>
    internal static void RegisterKnowledgeListTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_knowledge_list",
            new ToolDefinition
            {
                Description = "Lists all knowledge entries with optional filtering by category or tag.",
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
                        category = new
                        {
                            type = "string",
                            description = "Optional: filter by category",
                            @enum = KnowledgeCategories.All
                        },
                        tag = new
                        {
                            type = "string",
                            description = "Optional: filter by tag"
                        },
                        limit = new
                        {
                            type = "integer",
                            description = "Maximum number of results to return. Default: 100",
                            minimum = 1,
                            maximum = 500
                        },
                        offset = new
                        {
                            type = "integer",
                            description = "Skip first N entries for pagination. Default: 0",
                            minimum = 0
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
                var category = args?["category"]?.GetValue<string>();
                var tag = args?["tag"]?.GetValue<string>();
                var limit = args?["limit"]?.GetValue<int>() ?? 100;
                var offset = args?["offset"]?.GetValue<int>() ?? 0;

                if (string.IsNullOrWhiteSpace(solutionPath))
                    return CreateErrorResponse("Error: solutionPath is required");

                if (!File.Exists(solutionPath))
                    return CreateErrorResponse($"Error: Solution file not found: {solutionPath}");

                try
                {
                    var db = await GetKnowledgeDatabaseAsync(solutionPath);
                    var entries = await db.ListEntriesAsync(category, tag, limit, offset);
                    var totalCount = await db.GetEntryCountAsync();

                    return CreateJsonResponse(new
                    {
                        success = true,
                        totalCount,
                        returnedCount = entries.Count,
                        offset,
                        entries = entries.Select(e => new
                        {
                            e.Id,
                            e.Category,
                            e.Title,
                            e.Content,
                            e.SymbolLinks,
                            e.Tags,
                            e.Confidence,
                            e.CreatedAt,
                            e.UpdatedAt
                        })
                    });
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse($"Error listing knowledge: {ex.Message}");
                }
            });
    }

    /// <summary>
    /// Registers the roslyn_knowledge_delete tool.
    /// </summary>
    internal static void RegisterKnowledgeDeleteTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_knowledge_delete",
            new ToolDefinition
            {
                Description = "Deletes a knowledge entry by ID.",
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
                        id = new
                        {
                            type = "integer",
                            description = "ID of the knowledge entry to delete"
                        }
                    },
                    required = new[] { "solutionPath", "id" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var solutionPath = args?["solutionPath"]?.GetValue<string>();
                var id = args?["id"]?.GetValue<long>() ?? 0;

                if (string.IsNullOrWhiteSpace(solutionPath))
                    return CreateErrorResponse("Error: solutionPath is required");

                if (!File.Exists(solutionPath))
                    return CreateErrorResponse($"Error: Solution file not found: {solutionPath}");

                if (id <= 0)
                    return CreateErrorResponse("Error: valid id is required");

                try
                {
                    var db = await GetKnowledgeDatabaseAsync(solutionPath);
                    var deleted = await db.DeleteEntryAsync(id);

                    if (deleted)
                    {
                        return CreateJsonResponse(new
                        {
                            success = true,
                            message = $"Knowledge entry {id} deleted"
                        });
                    }
                    else
                    {
                        return CreateErrorResponse($"Error: Knowledge entry {id} not found");
                    }
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse($"Error deleting knowledge: {ex.Message}");
                }
            });
    }

    /// <summary>
    /// Registers the roslyn_knowledge_for_symbol tool.
    /// </summary>
    internal static void RegisterKnowledgeForSymbolTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_knowledge_for_symbol",
            new ToolDefinition
            {
                Description = "Gets all knowledge entries linked to a specific symbol. Use this when working on a method/class to get relevant gotchas and patterns.",
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
                            description = "Qualified symbol name (e.g., 'MyNamespace.MyClass.MyMethod')"
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

                if (string.IsNullOrWhiteSpace(solutionPath))
                    return CreateErrorResponse("Error: solutionPath is required");

                if (!File.Exists(solutionPath))
                    return CreateErrorResponse($"Error: Solution file not found: {solutionPath}");

                if (string.IsNullOrWhiteSpace(symbolName))
                    return CreateErrorResponse("Error: symbolName is required");

                try
                {
                    var db = await GetKnowledgeDatabaseAsync(solutionPath);
                    var entries = await db.GetEntriesForSymbolAsync(symbolName);

                    return CreateJsonResponse(new
                    {
                        success = true,
                        symbolName,
                        entryCount = entries.Count,
                        entries = entries.Select(e => new
                        {
                            e.Id,
                            e.Category,
                            e.Title,
                            e.Content,
                            e.SymbolLinks,
                            e.Tags,
                            e.Confidence
                        })
                    });
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse($"Error getting knowledge for symbol: {ex.Message}");
                }
            });
    }
}
