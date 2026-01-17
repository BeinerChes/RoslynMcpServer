using System.Text.Json.Serialization;

namespace RoslynMcpServer;

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

/// <summary>
/// Result of getting type members.
/// </summary>
public class GetTypeMembersResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? SolutionPath { get; init; }
    public TypeInfo? Type { get; init; }
    public int TotalMembers { get; init; }
    public List<MemberInfo> Members { get; init; } = [];
}

/// <summary>
/// Information about a type member.
/// Core fields (always included): Name, Kind, Signature
/// Optional fields (only in detailed mode): FilePath, Line, Column, Accessibility, IsStatic, IsAbstract, IsVirtual, IsOverride
/// </summary>
public class MemberInfo
{
    // Core fields - always included
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Signature { get; init; }

    // Optional fields - only included when not null (detailed mode)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FilePath { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Line { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Column { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Accessibility { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsStatic { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsAbstract { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsVirtual { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsOverride { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InheritedFrom { get; init; }
}
