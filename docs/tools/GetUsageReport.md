# GetUsageReport

## Description

GetUsageReport is an analytics tool that generates comprehensive usage statistics for all Roslyn MCP tools. It analyzes the tool call log (JSONL format) and produces aggregated metrics including call counts, success rates, performance data, and AI model inference statistics.

The tool provides visibility into tool adoption patterns, helps identify problematic tools with low success rates, and reveals performance bottlenecks. This is essential for monitoring MCP server health, understanding which tools are most valuable, and identifying areas for optimization.

The tool handles:
- Filtering by time range (last N hours or all-time statistics)
- Filtering by tool name (partial match for focused analysis)
- Aggregating thousands of log entries into concise summaries
- Calculating success rates, average durations, and total durations per tool
- Tracking AI model inference metrics (tokens in/out, tokens per second)
- Generating formatted markdown reports written to disk
- Returning JSON summary with report path and key metrics

## Comparison with Native Claude Code Tools

### vs Read + Manual Analysis
- **Read** requires loading the entire log file into context (potentially 200,000+ tokens for large logs)
- **GetUsageReport** processes logs server-side and returns only aggregated results (~500 tokens)
- **Read** provides raw JSONL data requiring manual parsing and aggregation
- **GetUsageReport** delivers pre-calculated statistics with formatted tables
- **Read** loads all historical data regardless of time range
- **GetUsageReport** filters by hours parameter server-side
- **Read** requires writing analysis scripts to calculate metrics
- **GetUsageReport** provides instant metrics: success rates, averages, totals

### vs Bash + jq/awk
For users comfortable with shell scripting:
- **Bash + jq** requires complex command chains to parse JSONL and aggregate stats
- **GetUsageReport** uses a single tool call with semantic parameters
- **Bash + jq** outputs unformatted data needing markdown conversion
- **GetUsageReport** generates formatted markdown tables ready for viewing
- **Bash + jq** doesn't persist results
- **GetUsageReport** writes reports to disk for historical comparison

### When to use GetUsageReport
- Understanding which Roslyn tools are used most frequently
- Identifying tools with high failure rates (candidates for improvement)
- Finding performance bottlenecks (tools with high average duration)
- Analyzing recent activity (filter by hours for focused review)
- Tracking model inference performance (tokens/sec for auto-generation)
- Generating reports for project documentation or retrospectives

## Real-World Example

### Scenario
After a development session using various Roslyn tools (FindSymbol, GetMethodBody, UpdateMethod, etc.), I want to:
1. See which tools I used in the last 24 hours
2. Check overall success rate to identify any problematic tools
3. View performance metrics to understand which operations are slow
4. Filter to only "Get" tools for navigation pattern analysis

**Test project:** RoslynMcpServer itself (actual testing with 1,667 real log entries)

### Approach 1: Using Native Tools

**Step 1:** Check log file size
```
Bash: ls -lh .roslyn-mcp/logs/tool-calls.jsonl
```
Output: `892K` (1,667 lines)

**Step 2:** Read entire log file
```
Read(file_path: ".roslyn-mcp/logs/tool-calls.jsonl")
```
Result: 1,667 lines × ~500 chars = ~835,000 chars = ~200,000+ tokens

**Step 3:** Write aggregation script
```
Write(file_path: "analyze_logs.py", content: <Python script to parse JSONL, filter by time, calculate stats>)
```
~500 tokens for script

**Step 4:** Run script
```
Bash: python analyze_logs.py
```
~100 tokens for output

**Step 5:** Format as markdown manually
~200 tokens to create tables

**Total: ~201,000 tokens, 5 operations (completely impractical)**

**Practical alternative:** Skip reading log, write blind script based on schema knowledge
- Write Python script: ~500 tokens
- Run script: ~100 tokens
- Read generated output: ~500 tokens

**More realistic total: ~1,100 tokens, 3 operations**

### Approach 2: Using GetUsageReport (Roslyn)

**Step 1:** Generate all-time report
```
GetUsageReport()
```
Server-side processing: parses 1,667 entries, aggregates stats
Returns: JSON with report path, total calls, success rate, tool count
~50 tokens

**Step 2:** Read generated report
```
Read(file_path: ".roslyn-mcp/reports/usage-report.md")
```
Returns: Formatted markdown with summary table and per-tool statistics
~500 tokens

**Step 3:** Generate filtered report (last 24 hours, "Get" tools only)
```
GetUsageReport(hours: 24, toolFilter: "Get")
Read(file_path: ".roslyn-mcp/reports/usage-report.md")
```
Returns: Filtered stats showing only Get* tools from last 24 hours
~550 tokens

**Total: ~600 tokens for basic report, ~1,100 tokens for basic + filtered, 2-4 operations**

### Comparison Summary

| Aspect | Native (Read + Script) | GetUsageReport (Roslyn) |
|--------|------------------------|-------------------------|
| **Token usage (basic)** | ~1,100 tokens (realistic blind script) | ~550 tokens |
| **Token usage (if reading log)** | ~201,000 tokens | ~550 tokens |
| **Operations** | 3 (write script, run, read output) | 2 (generate, read report) |
| **Server-side aggregation** | No (client must process all data) | Yes (processes 1,667 entries server-side) |
| **Time filtering** | Manual in script | `hours` parameter |
| **Tool filtering** | Manual in script | `toolFilter` parameter |
| **Output format** | Custom (requires formatting logic) | Formatted markdown tables |
| **Persistence** | Manual (script must write file) | Automatic (reports/ directory) |
| **Model inference stats** | Must manually parse and aggregate | Automatically included if present |

**Key advantages:**
- **50-365× token reduction** compared to reading raw log file
- **Server-side aggregation**: Processes thousands of entries without context window impact
- **Semantic filtering**: `hours` and `toolFilter` parameters vs manual script logic
- **Instant insights**: Pre-calculated success rates, averages, totals
- **Historical tracking**: Reports written to disk for comparison over time

**When manual analysis is better:**
- Custom metrics not provided by GetUsageReport (e.g., tool correlation analysis)
- One-off exploratory analysis requiring flexible queries
- Debugging specific failed calls (GetUsageReport shows aggregates, not individual entries)

## How It Works

### Log File Structure

The tool reads from `.roslyn-mcp/logs/tool-calls.jsonl`, a JSON Lines file where each line is a log entry:

```json
{
  "timestamp": "2026-02-05T18:44:27.4059403Z",
  "tool": "UpdateMethod",
  "parameters": {"typeName": "Calculator", "methodName": "Add", ...},
  "success": true,
  "resultSummary": "...",
  "durationMs": 2158,
  "inputChars": 450,
  "outputChars": 120
}
```

For AI model inference calls, additional fields are logged:
```json
{
  "tool": "generate_method_inference",
  "inputTokens": 45,
  "outputTokens": 52,
  "tokensPerSecond": 164.5,
  ...
}
```

### Aggregation Process

The tool calls `ToolCallLogger.GenerateReport(since)`:

1. **Load entries**: Reads all JSONL entries from log file
2. **Filter by time**: If `hours` specified, filters to `DateTime.UtcNow.AddHours(-hours)`
3. **Filter by tool**: If `toolFilter` specified, filters to tools containing the string (case-insensitive)
4. **Aggregate per tool**:
   - Count total calls
   - Count successful vs failed calls
   - Calculate success rate percentage
   - Sum total duration and calculate average
   - Sum input/output characters
   - For model inference: sum tokens and calculate avg tokens/sec
5. **Generate report**: Builds markdown with summary table and per-tool statistics
6. **Write to disk**: Saves report to `.roslyn-mcp/reports/usage-report.md`
7. **Return summary**: JSON with report path, total calls, success rate, tool count

### Invocation

**Basic usage (all-time stats):**
```
GetUsageReport()
```

**Last 24 hours:**
```
GetUsageReport(hours: 24)
```

**Filter by tool name:**
```
GetUsageReport(toolFilter: "Get")
```
Matches: GetMethodBody, GetTypeMembers, GetInstructions, GetCallers, etc.

**Combined filtering:**
```
GetUsageReport(hours: 8, toolFilter: "Update")
```
Shows only Update* tools from the last 8 hours

### Response Format

**Success:**
```json
{
  "reportPath": "D:\\repos\\RoslynMcpServer\\.roslyn-mcp\\reports\\usage-report.md",
  "totalCalls": 1668,
  "successRate": 99.8,
  "toolCount": 27
}
```

The actual report is written to the file path. Read it with:
```
Read(file_path: <reportPath>)
```

**Generated Report Structure:**
```markdown
# Roslyn MCP Usage Report

**Generated:** 2026-02-05 22:05:56 UTC
**Period:** All time (or "Since YYYY-MM-DD HH:mm:ss UTC")

## Summary

| Metric | Value |
|--------|-------|
| Total Calls | 1,668 |
| Successful | 1,664 |
| Failed | 4 |
| Success Rate | 99.8% |
| Total Duration | 5,277,755 ms |
| Total Input | 450,520 chars |
| Total Output | 132,858 chars |

## Tool Statistics

| Tool | Calls | Success % | Avg (ms) | Total (ms) |
|------|------:|----------:|---------:|-----------:|
| GetMethodBody | 489 | 100.0% | 3320 | 1623516 |
| FindSymbol | 205 | 100.0% | 3496 | 716798 |
| ...

## Model Inference Statistics

(Only appears if model inference calls were logged)

| Tool | Calls | In Tokens | Out Tokens | Avg tok/s |
|------|------:|----------:|-----------:|----------:|
| generate_method_inference | 55 | 2,570 | 2,542 | 164.5 |
```

### Common Workflows

**1. Session Retrospective**
At end of work session, review what tools were used:
```
GetUsageReport(hours: 8)
Read(file_path: ".roslyn-mcp/reports/usage-report.md")
```
Insights: Which tools dominated? Any failures to investigate?

**2. Performance Profiling**
Identify slow tools for optimization:
```
GetUsageReport()
Read(file_path: ".roslyn-mcp/reports/usage-report.md")
```
Look at "Avg (ms)" column - tools over 10 seconds may need optimization

**3. Feature Adoption Analysis**
After adding a new tool, check adoption:
```
GetUsageReport(toolFilter: "NewToolName")
Read(file_path: ".roslyn-mcp/reports/usage-report.md")
```
Shows if the new tool is being used and its success rate

**4. Debugging High Failure Rates**
Identify tools with reliability issues:
```
GetUsageReport()
Read(file_path: ".roslyn-mcp/reports/usage-report.md")
```
Check "Success %" column - anything under 95% needs investigation

**5. Model Inference Monitoring**
Track AI model performance:
```
GetUsageReport()
Read(file_path: ".roslyn-mcp/reports/usage-report.md")
```
Model Inference Statistics table shows tokens/sec (should be >100 for good performance)

### Report Persistence

Reports are always written to `.roslyn-mcp/reports/usage-report.md`. Each call overwrites the previous report.

**For historical tracking:**
```bash
# Save timestamped copy
cp .roslyn-mcp/reports/usage-report.md reports/usage-$(date +%Y%m%d).md
```

### When Manual Log Analysis is Better

GetUsageReport provides aggregated statistics. For detailed debugging, read the raw log:

**Use raw log for:**
- Finding exact timestamp of a specific tool call
- Debugging individual failures (parameters, error messages)
- Analyzing parameter patterns (what values are commonly used)
- Correlating tool call sequences (what tools are used together)

**Example: Debug a specific failure**
```
Read(file_path: ".roslyn-mcp/logs/tool-calls.jsonl")
Grep(pattern: "\"success\":false", path: ".roslyn-mcp/logs/tool-calls.jsonl", output_mode: "content")
```
Shows actual failure details vs GetUsageReport which just shows failure count.
