using System.Text.Json;

namespace RoslynMcpServer;

/// <summary>
/// Tracks the last symbol processed by MCP tools for visualization sync.
/// Uses a file-based approach so the Web server can read it.
/// </summary>
public static class LastSymbolTracker
{
    private static readonly string StateFilePath = Path.Combine(
        Path.GetTempPath(),
        "roslyn-mcp-last-symbol.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Records a symbol that was just processed.
    /// </summary>
    public static void Track(string solutionPath, string qualifiedName, string? kind = null)
    {
        try
        {
            var state = new LastSymbolState
            {
                SolutionPath = solutionPath,
                QualifiedName = qualifiedName,
                Kind = kind,
                Timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(StateFilePath, json);
        }
        catch
        {
            // Silently ignore errors - this is non-critical
        }
    }
}

/// <summary>
/// State object for last processed symbol.
/// </summary>
public class LastSymbolState
{
    public string SolutionPath { get; set; } = "";
    public string QualifiedName { get; set; } = "";
    public string? Kind { get; set; }
    public DateTime Timestamp { get; set; }
}
