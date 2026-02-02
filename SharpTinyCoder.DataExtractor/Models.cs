namespace SharpTinyCoder.DataExtractor;

/// <summary>
/// Options for controlling the extraction process.
/// </summary>
public sealed record ExtractionOptions
{
    /// <summary>
    /// Minimum number of lines in method body (default: 3).
    /// </summary>
    public int MinBodyLines { get; init; } = 3;

    /// <summary>
    /// Maximum number of lines in method body (default: 50).
    /// </summary>
    public int MaxBodyLines { get; init; } = 50;

    /// <summary>
    /// Whether to include internal methods (default: true).
    /// </summary>
    public bool IncludeInternal { get; init; } = true;
}

/// <summary>
/// Represents a single training sample for the code generation model.
/// </summary>
public sealed class TrainingSample
{
    /// <summary>
    /// The input prompt containing XML doc, signature, class fields, and referenced types.
    /// </summary>
    public required string Input { get; init; }

    /// <summary>
    /// The expected output (method body without braces).
    /// </summary>
    public required string Output { get; init; }

    /// <summary>
    /// Source file path for debugging/tracing.
    /// </summary>
    public required string SourceFile { get; init; }

    /// <summary>
    /// Line number in source file.
    /// </summary>
    public required int Line { get; init; }

    /// <summary>
    /// Fully qualified method name.
    /// </summary>
    public required string MethodName { get; init; }
}

/// <summary>
/// Result of the extraction process.
/// </summary>
public sealed class ExtractionResult
{
    /// <summary>
    /// Total number of methods scanned.
    /// </summary>
    public int TotalMethods { get; set; }

    /// <summary>
    /// Number of methods that passed filtering.
    /// </summary>
    public int ExtractedMethods { get; set; }

    /// <summary>
    /// Number of methods skipped due to missing XML doc.
    /// </summary>
    public int SkippedNoDoc { get; set; }

    /// <summary>
    /// Number of methods skipped due to body length constraints.
    /// </summary>
    public int SkippedBodyLength { get; set; }

    /// <summary>
    /// Number of methods skipped due to being in generated files.
    /// </summary>
    public int SkippedGenerated { get; set; }

    /// <summary>
    /// Number of methods skipped due to access modifiers (private/protected).
    /// </summary>
    public int SkippedAccessibility { get; set; }

    /// <summary>
    /// Number of methods skipped due to being abstract/extern/partial.
    /// </summary>
    public int SkippedNoBody { get; set; }

    /// <summary>
    /// Path to the output JSONL file.
    /// </summary>
    public required string OutputFile { get; init; }
}
