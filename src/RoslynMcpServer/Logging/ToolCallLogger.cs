namespace RoslynMcpServer.Logging;

public class ToolCallLogger
{


    private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");


    private static readonly string LogFilePath = Path.Combine(LogDirectory, "tool-calls.jsonl");


    private static readonly object _lock = new();


    public static bool Enabled { get; set; } = true;


    public static void Log(ToolCallEntry entry)
    {
        if (!Enabled) return;

        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(LogDirectory);
                var json = System.Text.Json.JsonSerializer.Serialize(entry, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                File.AppendAllText(LogFilePath, json + Environment.NewLine);
            }
        }
        catch
        {
            // Silently ignore logging failures
        }
    }


    public static List<ToolCallEntry> GetEntries(DateTime? since = null, string? toolFilter = null)
    {
        var entries = new List<ToolCallEntry>();
        if (!File.Exists(LogFilePath)) return entries;

        lock (_lock)
        {
            foreach (var line in File.ReadLines(LogFilePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = System.Text.Json.JsonSerializer.Deserialize<ToolCallEntry>(line, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    });
                    if (entry == null) continue;
                    if (since.HasValue && entry.Timestamp < since.Value) continue;
                    if (toolFilter != null && !entry.Tool.Contains(toolFilter, StringComparison.OrdinalIgnoreCase)) continue;
                    entries.Add(entry);
                }
                catch { }
            }
        }
        return entries;
    }


    public static UsageReport GenerateReport(DateTime? since = null)
    {
        var entries = GetEntries(since);
        var report = new UsageReport
        {
            GeneratedAt = DateTime.UtcNow,
            Since = since,
            TotalCalls = entries.Count,
            SuccessfulCalls = entries.Count(e => e.Success),
            FailedCalls = entries.Count(e => !e.Success),
            TotalDurationMs = entries.Sum(e => e.DurationMs),
            TotalInputChars = entries.Sum(e => e.InputChars),
            TotalOutputChars = entries.Sum(e => e.OutputChars),
            ToolStats = entries
                .GroupBy(e => e.Tool)
                .Select(g =>
                {
                    var stats = new ToolStats
                    {
                        Tool = g.Key,
                        Calls = g.Count(),
                        SuccessRate = g.Count() > 0 ? (double)g.Count(e => e.Success) / g.Count() * 100 : 0,
                        AvgDurationMs = g.Count() > 0 ? (long)g.Average(e => e.DurationMs) : 0,
                        TotalDurationMs = g.Sum(e => e.DurationMs)
                    };

                    // Add inference stats if present
                    var withTokens = g.Where(e => e.OutputTokens.HasValue).ToList();
                    if (withTokens.Count > 0)
                    {
                        stats.TotalInputTokens = withTokens.Sum(e => e.InputTokens ?? 0);
                        stats.TotalOutputTokens = withTokens.Sum(e => e.OutputTokens ?? 0);
                        stats.AvgTokensPerSecond = Math.Round(withTokens.Average(e => e.TokensPerSecond ?? 0), 1);
                    }

                    return stats;
                })
                .OrderByDescending(s => s.Calls)
                .ToList()
        };
        return report;
    }
}