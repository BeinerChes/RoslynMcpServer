namespace RoslynMcpServer;

/// <summary>
/// Result of applying a code fix.
/// </summary>
public class ApplyCodeFixResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? FilePath { get; init; }
    public string? DiagnosticId { get; init; }
    public string? DiagnosticMessage { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    /// <summary>
    /// The title/description of the applied fix.
    /// </summary>
    public string? AppliedFixTitle { get; init; }
    /// <summary>
    /// List of available fixes if multiple exist.
    /// </summary>
    public List<CodeFixInfo>? AvailableFixes { get; init; }
    /// <summary>
    /// Number of files changed by this fix.
    /// </summary>
    public int? FilesChanged { get; init; }
    /// <summary>
    /// Preview mode: shows what would change without applying.
    /// </summary>
    public bool IsPreview { get; init; }
}

/// <summary>
/// Information about an available code fix.
/// </summary>
public class CodeFixInfo
{
    public int Index { get; init; }
    public required string Title { get; init; }
    public string? EquivalenceKey { get; init; }
}

/// <summary>
/// Result of batch applying code fixes.
/// </summary>
public class BatchApplyCodeFixResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? SolutionPath { get; init; }
    public string? DiagnosticId { get; init; }
    /// <summary>
    /// Total diagnostics found matching the filter.
    /// </summary>
    public int TotalDiagnosticsFound { get; init; }
    /// <summary>
    /// Number of diagnostics that had fixes available.
    /// </summary>
    public int DiagnosticsWithFixes { get; init; }
    /// <summary>
    /// Number of fixes successfully applied.
    /// </summary>
    public int FixesApplied { get; init; }
    /// <summary>
    /// Number of fixes that failed to apply.
    /// </summary>
    public int FixesFailed { get; init; }
    /// <summary>
    /// Number of files modified.
    /// </summary>
    public int FilesModified { get; init; }
    /// <summary>
    /// List of files that were modified.
    /// </summary>
    public List<string> ModifiedFiles { get; init; } = [];
    /// <summary>
    /// Details about each fix applied.
    /// </summary>
    public List<BatchFixDetail>? Details { get; init; }
    /// <summary>
    /// Preview mode: shows what would change without applying.
    /// </summary>
    public bool IsPreview { get; init; }
}

/// <summary>
/// Detail about a single fix in a batch operation.
/// </summary>
public class BatchFixDetail
{
    public required string FilePath { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public required string DiagnosticMessage { get; init; }
    public required string FixTitle { get; init; }
    public bool Applied { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Result of renaming a symbol.
/// </summary>
public class RenameSymbolResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? OriginalName { get; init; }
    public string? NewName { get; init; }
    public string? SymbolKind { get; init; }
    public string? ContainingType { get; init; }
    public int TotalFilesAffected { get; init; }
    public int TotalChanges { get; init; }
    public List<string> AffectedFiles { get; init; } = [];
}
