namespace RoslynMcpServer;

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
