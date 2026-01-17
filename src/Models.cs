using System.Text.Json.Serialization;

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
/// Result of finding symbols.
/// </summary>
public class FindSymbolResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? SolutionPath { get; init; }
    public string? Pattern { get; init; }
    public int TotalFound { get; init; }
    public List<SymbolInfo> Symbols { get; init; } = [];
}

/// <summary>
/// Information about a found symbol.
/// Core fields (always included): Name, FullyQualifiedName, Kind, FilePath, Line
/// Optional fields (only in detailed mode): Column, ContainingType, Accessibility, IsStatic, Signature
/// </summary>
public class SymbolInfo
{
    // Core fields - always included
    public required string Name { get; init; }
    public required string FullyQualifiedName { get; init; }
    public required string Kind { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }

    // Optional fields - only included when not null (detailed mode)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Column { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContainingType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Accessibility { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsStatic { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Signature { get; init; }
}

/// <summary>
/// Result of finding references.
/// </summary>
public class FindReferencesResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? SolutionPath { get; init; }
    public SymbolInfo? Symbol { get; init; }
    public int TotalFound { get; init; }
    public int ReturnedCount { get; init; }
    public List<ReferenceInfo> References { get; init; } = [];
}

/// <summary>
/// Information about a single reference location.
/// </summary>
public class ReferenceInfo
{
    public required string FilePath { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public string? ProjectName { get; init; }
    public string? Preview { get; init; }
}

/// <summary>
/// Result of finding implementations.
/// </summary>
public class FindImplementationsResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? SolutionPath { get; init; }
    public TypeInfo? BaseType { get; init; }
    public int TotalFound { get; init; }
    public List<ImplementationInfo> Implementations { get; init; } = [];
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

/// <summary>
/// Information about an implementing type.
/// </summary>
public class ImplementationInfo
{
    public required string Name { get; init; }
    public required string FullyQualifiedName { get; init; }
    public required string Kind { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public bool IsBaseType { get; init; }
    public bool IsAbstract { get; init; }
    public List<string> BaseTypes { get; init; } = [];
    public List<string> Interfaces { get; init; } = [];
}
