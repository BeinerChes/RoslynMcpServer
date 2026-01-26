# MCP Benchmark Session

## CRITICAL: After EVERY tool call, you MUST:

1. Run: `python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" since <MARK>`
2. Note the **New Context** (cache write) and **Cost** from output
3. Append result to `C:\Users\cbein\source\repos\RoslynMcpServer\docs\benchmark\results_mcp.md`
4. Then proceed to next step

## Setup

First, mark the starting position:
```bash
python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" mark
```
Save this MARK number for all subsequent `since` calls.

## The Task

Read and complete all steps in: `C:\Users\cbein\source\repos\RoslynMcpServer\docs\benchmark\test_task.md`

**Solution path:** `D:\repos\Atlas3_EDEV\Atlas3.sln`

## Results Format

After each tool call, append to results_mcp.md:

```
| Step | Tool | Calls | New Context | Cost | Result |
```

Example:
```
| 1 | roslyn_get_type_members | 1 | 45K | $0.85 | 155 members found |
```

## After All Steps

1. Final summary: `python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" tool <MARK>`
2. Cost breakdown: `python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" cost <MARK>`
3. Revert changes: `cd "D:\repos\Atlas3_EDEV" && git restore .`
