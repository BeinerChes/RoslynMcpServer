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

/// <summary>
/// Result of getting a method's source code.
/// </summary>
public class GetMethodBodyResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? SolutionPath { get; init; }
    public string? TypeName { get; init; }
    public string? MethodName { get; init; }
    public string? FilePath { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public string? Signature { get; init; }
    public string? SourceCode { get; init; }
    /// <summary>
    /// If multiple overloads exist, lists them so user can specify which one.
    /// </summary>
    public List<string>? AvailableOverloads { get; init; }
}

/// <summary>
/// Result of updating a method's source code.
/// </summary>
public class UpdateMethodResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? FilePath { get; init; }
    public string? TypeName { get; init; }
    public string? MethodName { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public string? OldSignature { get; init; }
    public string? NewSignature { get; init; }
    /// <summary>
    /// If multiple overloads exist, lists them so user can specify which one.
    /// </summary>
    public List<string>? AvailableOverloads { get; init; }
}

/// <summary>
/// Result of getting diagnostics from a solution.
/// </summary>
public class GetDiagnosticsResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? SolutionPath { get; init; }
    public int TotalErrors { get; init; }
    public int TotalWarnings { get; init; }
    public int TotalInfo { get; init; }
    /// <summary>
    /// Summary mode: grouped counts by diagnostic ID.
    /// </summary>
    public List<DiagnosticSummary>? Summary { get; init; }
    /// <summary>
    /// Detail mode: specific entries for a diagnostic ID.
    /// </summary>
    public List<DiagnosticEntry>? Entries { get; init; }
    public int? ReturnedCount { get; init; }
    public int? TotalMatchingEntries { get; init; }
}

/// <summary>
/// Summary of diagnostics grouped by ID.
/// </summary>
public class DiagnosticSummary
{
    public required string Id { get; init; }
    public required string Severity { get; init; }
    public required string Title { get; init; }
    public int Count { get; init; }
    public string? ExampleFile { get; init; }
    public int? ExampleLine { get; init; }
}

/// <summary>
/// A specific diagnostic entry with location info.
/// </summary>
public class DiagnosticEntry
{
    public required string Id { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public string? ProjectName { get; init; }
    /// <summary>
    /// The containing type name if the diagnostic is inside a type.
    /// </summary>
    public string? ContainingType { get; init; }
    /// <summary>
    /// The containing method name if the diagnostic is inside a method.
    /// </summary>
    public string? ContainingMethod { get; init; }
}

/// <summary>
/// Result of adding a member to a type.
/// </summary>
public class AddMemberResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? FilePath { get; init; }
    public string? TypeName { get; init; }
    public string? MemberName { get; init; }
    public string? MemberKind { get; init; }
    public int? InsertedAtLine { get; init; }
    public string? Signature { get; init; }
}
