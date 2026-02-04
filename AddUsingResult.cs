namespace RoslynMcpServer;

public class AddUsingResult
{

    /// <summary>
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// </summary>
    public string? UsingDirective { get; set; }

    /// <summary>
    /// </summary>
    public bool AlreadyExists { get; set; }
}