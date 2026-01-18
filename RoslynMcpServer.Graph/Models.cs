namespace RoslynMcpServer.Graph;

/// <summary>
/// Represents a .NET solution tracked in the graph database.
/// </summary>
public sealed class SolutionRecord
{
    public long Id { get; set; }
    public required string Path { get; set; }
    public required string Name { get; set; }
    public DateTime? LastAnalyzed { get; set; }
    public string? SolutionHash { get; set; }
}

/// <summary>
/// Represents a code symbol (method, property, field, etc.) in the graph.
/// </summary>
public sealed class SymbolRecord
{
    public long Id { get; set; }
    public long SolutionId { get; set; }
    public required SymbolKind Kind { get; set; }
    public required string Name { get; set; }
    public required string QualifiedName { get; set; }
    public required string FilePath { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string? BodyHash { get; set; }
    public long? ContainingTypeId { get; set; }
    public SymbolStatus Status { get; set; } = SymbolStatus.Pending;
    public DateTime? LastAnalyzed { get; set; }
}

/// <summary>
/// Represents a relationship between two symbols in the graph.
/// </summary>
public sealed class EdgeRecord
{
    public long FromSymbolId { get; set; }
    public long ToSymbolId { get; set; }
    public required EdgeType EdgeType { get; set; }
}

/// <summary>
/// Types of symbols tracked in the graph.
/// </summary>
public enum SymbolKind
{
    Method,
    Property,
    Field,
    Constructor,
    Event,
    Type
}

/// <summary>
/// Types of relationships between symbols.
/// </summary>
public enum EdgeType
{
    Calls,
    Reads,
    Writes,
    Implements,
    Overrides,
    Accesses
}

/// <summary>
/// Analysis status of a symbol.
/// </summary>
public enum SymbolStatus
{
    Pending,
    Analyzed,
    Dirty
}
