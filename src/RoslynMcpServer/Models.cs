namespace RoslynMcpServer;

/// <summary>
/// Result of getting projects in build order.
/// </summary>
public class ProjectBuildOrderResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? SolutionPath { get; init; }
    public List<ProjectInfo> Projects { get; init; } = [];
}

/// <summary>
/// Information about a project in the solution.
/// </summary>
public class ProjectInfo
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required string Language { get; init; }
    public List<string> Dependencies { get; init; } = [];
}

/// <summary>
/// Filter for symbol kinds to search.
/// </summary>
public enum SymbolKindFilter
{
    All,
    Type,
    Member,
    Namespace,
    TypeAndMember
}

/// <summary>
/// How to match the search pattern.
/// </summary>
public enum MatchType
{
    Exact,
    ExactIgnoreCase,
    Contains,
    Prefix,
    Suffix
}

/// <summary>
/// Filter for member kinds to retrieve.
/// </summary>
public enum MemberKindFilter
{
    All,
    Methods,
    Properties,
    Fields,
    Events,
    Constructors
}

/// <summary>
/// Basic type information.
/// </summary>
public class TypeInfo
{
    public required string Name { get; init; }
    public required string FullyQualifiedName { get; init; }
    public required string Kind { get; init; }
}
