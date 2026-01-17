using System.Text.Json.Serialization;

namespace RoslynMcpServer;

/// <summary>
/// Result of getting diagnostics from a solution.
/// </summary>
public class GetDiagnosticsResult
{
    public bool Success { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TotalErrors { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TotalWarnings { get; init; }

    /// <summary>
    /// Summary mode: grouped counts by diagnostic ID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DiagnosticSummary>? Summary { get; init; }

    /// <summary>
    /// Detail mode: specific entries for a diagnostic ID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DiagnosticEntry>? Entries { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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

    /// <summary>
    /// Number suppressed by project settings. Omitted when 0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SuppressedCount { get; init; }

    /// <summary>
    /// True if Roslyn has an auto-fix for this diagnostic.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FixAvailable { get; init; }
}

/// <summary>
/// A specific diagnostic entry with location info.
/// Compact: Id, Severity, FixAvailable omitted (same for all entries when filtering by diagnosticId).
/// </summary>
public class DiagnosticEntry
{
    public required string Message { get; init; }
    public required string FilePath { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContainingType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContainingMethod { get; init; }

    /// <summary>
    /// Suppression reason if suppressed, otherwise omitted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Suppressed { get; init; }
}
