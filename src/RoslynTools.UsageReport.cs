using System.Text;
using System.Text.Json.Nodes;
using RoslynMcpServer.Logging;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    public static void RegisterUsageReportTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_get_usage_report",
            new ToolDefinition
            {
                Description = "Gets usage statistics for Roslyn MCP tools. Shows call counts, success rates, and performance metrics per tool.",
                InputSchema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["hours"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Only include calls from the last N hours. Default: all time"
                        },
                        ["toolFilter"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Filter by tool name (partial match)"
                        }
                    }
                }
            },
            HandleGetUsageReportAsync);
    }

    private static Task<object> HandleGetUsageReportAsync(JsonObject? args)
    {
        var hours = args?["hours"]?.GetValue<int?>();
        var toolFilter = args?["toolFilter"]?.GetValue<string?>();

        DateTime? since = hours.HasValue ? DateTime.UtcNow.AddHours(-hours.Value) : null;

        var report = ToolCallLogger.GenerateReport(since);

        // Filter by tool if specified
        if (!string.IsNullOrEmpty(toolFilter))
        {
            report.ToolStats = report.ToolStats
                .Where(s => s.Tool.Contains(toolFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Generate markdown report
        var md = new StringBuilder();
        md.AppendLine("# Roslyn MCP Usage Report");
        md.AppendLine();
        md.AppendLine($"**Generated:** {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        if (report.Since.HasValue)
            md.AppendLine($"**Period:** Since {report.Since:yyyy-MM-dd HH:mm:ss} UTC");
        else
            md.AppendLine("**Period:** All time");
        md.AppendLine();
        
        md.AppendLine("## Summary");
        md.AppendLine();
        md.AppendLine($"| Metric | Value |");
        md.AppendLine($"|--------|-------|");
        md.AppendLine($"| Total Calls | {report.TotalCalls:N0} |");
        md.AppendLine($"| Successful | {report.SuccessfulCalls:N0} |");
        md.AppendLine($"| Failed | {report.FailedCalls:N0} |");
        var successRate = report.TotalCalls > 0 ? (double)report.SuccessfulCalls / report.TotalCalls * 100 : 0;
        md.AppendLine($"| Success Rate | {successRate:F1}% |");
        md.AppendLine($"| Total Duration | {report.TotalDurationMs:N0} ms |");
        md.AppendLine($"| Total Input | {report.TotalInputChars:N0} chars |");
        md.AppendLine($"| Total Output | {report.TotalOutputChars:N0} chars |");
        md.AppendLine();

        md.AppendLine("## Tool Statistics");
        md.AppendLine();
        md.AppendLine("| Tool | Calls | Success % | Avg (ms) | Total (ms) |");
        md.AppendLine("|------|------:|----------:|---------:|-----------:|");
        
        foreach (var stat in report.ToolStats)
        {
            var toolName = stat.Tool.Replace("roslyn_", "");
            md.AppendLine($"| {toolName} | {stat.Calls} | {stat.SuccessRate:F1}% | {stat.AvgDurationMs} | {stat.TotalDurationMs} |");
        }
        md.AppendLine();

        // Write to file
        var reportsDir = Path.Combine(AppContext.BaseDirectory, "reports");
        Directory.CreateDirectory(reportsDir);
        var reportPath = Path.Combine(reportsDir, "usage-report.md");
        File.WriteAllText(reportPath, md.ToString());

        return Task.FromResult<object>(new JsonObject
        {
            ["reportPath"] = reportPath,
            ["totalCalls"] = report.TotalCalls,
            ["successRate"] = Math.Round(successRate, 1),
            ["toolCount"] = report.ToolStats.Count
        });
    }
}
