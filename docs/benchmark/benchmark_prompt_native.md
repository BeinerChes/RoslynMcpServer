# Native Tools Benchmark Session

## IMPORTANT: MCP must be disabled for this test

Before starting, rename `.mcp.json` to `.mcp.json.disabled` in the RoslynMcpServer folder.

## CRITICAL: After EVERY tool call, you MUST:

1. Run: `python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" since <MARK>`
2. Append result to `C:\Users\cbein\source\repos\RoslynMcpServer\docs\benchmark\results_native.md`
3. Then proceed to next step

## Setup

First, mark the starting position:
```bash
python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" mark
```
Save this MARK number for all subsequent `since` calls.

## The Task

Read and complete all steps in: `C:\Users\cbein\source\repos\RoslynMcpServer\docs\benchmark\test_task.md`

**Solution path:** `D:\repos\Atlas3_EDEV\Atlas3.sln`

**Available tools:** Glob, Grep, Read, Edit, Write, Bash (no MCP/Roslyn tools)

## Results Format

After each tool call, append to results_native.md:

```
| Step | Tool | Calls | Context | Result |
```

## After All Steps

1. Final summary: `python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" tool <MARK>`
2. Cost breakdown: `python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" cost <MARK>`
3. Revert changes: `cd "D:\repos\Atlas3_EDEV" && git restore .`
4. Re-enable MCP: Rename `.mcp.json.disabled` back to `.mcp.json`
