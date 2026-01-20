namespace RoslynMcpServer.Graph;

/// <summary>
/// Represents a knowledge entry about code patterns, gotchas, or insights.
/// </summary>
public class KnowledgeEntry
{
    public long Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public byte[]? Embedding { get; set; }
    public string? EmbeddingModel { get; set; }
    public double Confidence { get; set; } = 1.0;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties (populated by queries)
    public List<string> SymbolLinks { get; set; } = [];
    public List<string> Tags { get; set; } = [];
}

/// <summary>
/// Represents a link between a knowledge entry and a code symbol.
/// </summary>
public class KnowledgeSymbolLink
{
    public long KnowledgeId { get; set; }
    public string SymbolName { get; set; } = string.Empty;
    public string LinkType { get; set; } = "related";
}

/// <summary>
/// Valid categories for knowledge entries.
/// </summary>
public static class KnowledgeCategories
{
    public const string Gotcha = "gotcha";
    public const string Pattern = "pattern";
    public const string Architecture = "architecture";
    public const string Debugging = "debugging";
    public const string Performance = "performance";
    public const string Security = "security";
    public const string Testing = "testing";
    public const string Workaround = "workaround";

    public static readonly string[] All =
    [
        Gotcha, Pattern, Architecture, Debugging,
        Performance, Security, Testing, Workaround
    ];

    public static bool IsValid(string category) =>
        All.Contains(category, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Valid link types for knowledge-symbol relationships.
/// </summary>
public static class KnowledgeLinkTypes
{
    public const string About = "about";       // Entry is specifically about this symbol
    public const string Related = "related";   // Entry is related to this symbol
    public const string Affects = "affects";   // Entry describes something that affects this symbol

    public static readonly string[] All = [About, Related, Affects];

    public static bool IsValid(string linkType) =>
        All.Contains(linkType, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Search result with relevance score.
/// </summary>
public class KnowledgeSearchResult
{
    public KnowledgeEntry Entry { get; set; } = null!;
    public double Score { get; set; }
    public string MatchSource { get; set; } = string.Empty; // "symbol", "fts", "vector"
}

/// <summary>
/// Input for adding a new knowledge entry.
/// </summary>
public class AddKnowledgeInput
{
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string>? SymbolLinks { get; set; }
    public List<string>? Tags { get; set; }
    public double Confidence { get; set; } = 1.0;
}
