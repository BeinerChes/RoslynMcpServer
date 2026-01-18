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

    /// <summary>
    /// Gets the last tracked symbol.
    /// </summary>
    public static LastSymbolState? GetLast()
    {
        try
        {
            if (!File.Exists(StateFilePath))
                return null;

            var json = File.ReadAllText(StateFilePath);
            return JsonSerializer.Deserialize<LastSymbolState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Clears the last tracked symbol.
    /// </summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(StateFilePath))
                File.Delete(StateFilePath);
        }
        catch
        {
            // Silently ignore
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
