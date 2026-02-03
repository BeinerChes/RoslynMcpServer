---
name: usage-report
description: Generate a usage report for Roslyn MCP tools showing call counts, success rates, and performance metrics
user-invocable: true
arguments: "[hours]"
---

# Usage Report

Generate a markdown report of Roslyn MCP tool usage statistics.

## Instructions

1. Call the `roslyn_get_usage_report` tool with optional hours parameter
2. Read the generated report file
3. Display the report to the user

## Steps

<step>
Call the usage report tool:
- If user specified hours: `roslyn_get_usage_report(hours: <hours>)`
- Otherwise: `roslyn_get_usage_report()`
</step>

<step>
Read and display the report from `.roslyn-mcp/reports/usage-report.md`
</step>
