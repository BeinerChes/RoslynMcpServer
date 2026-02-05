namespace RoslynMcpServer.Logging;

public class UsageReport
{


    public DateTime GeneratedAt { get; set; }


    public DateTime? Since { get; set; }


    public int TotalCalls { get; set; }


    public int SuccessfulCalls { get; set; }


    public int FailedCalls { get; set; }


    public long TotalDurationMs { get; set; }


    public int TotalInputChars { get; set; }


    public int TotalOutputChars { get; set; }


    public List<ToolStats> ToolStats { get; set; } = [];
}