namespace RoslynMcpServer.Logging;

public class ToolStats
{


    public string Tool { get; set; } = "";


    public int Calls { get; set; }


    public double SuccessRate { get; set; }


    public long AvgDurationMs { get; set; }


    public long TotalDurationMs { get; set; }
}