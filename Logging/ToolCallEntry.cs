namespace RoslynMcpServer.Logging;

public class ToolCallEntry
{


    public DateTime Timestamp { get; set; } = DateTime.UtcNow;


    public string Tool { get; set; } = "";


    public Dictionary<string, object?> Parameters { get; set; } = [];


    public bool Success { get; set; }


    public string? ResultSummary { get; set; }


    public string? Error { get; set; }


    public long DurationMs { get; set; }


    public int InputChars { get; set; }


    public int OutputChars { get; set; }
}