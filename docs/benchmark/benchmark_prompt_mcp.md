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

## Step 0: Load Instructions (simulates normal MCP workflow)

Before starting the task, call these instructions (as hooks would normally trigger):

```
roslyn_get_instructions(topic: "tools", solutionPath: "D:\\repos\\Atlas3_EDEV\\Atlas3.sln")
roslyn_get_instructions(topic: "code", solutionPath: "D:\\repos\\Atlas3_EDEV\\Atlas3.sln")
```

Track this as Step 0 in results.

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
| 0 | roslyn_get_instructions x2 | 2 | 12K | $0.22 | Loaded tools + code instructions |
| 1 | roslyn_get_type_members | 1 | 45K | $0.85 | 155 members found |
```

## After All Steps

1. Final summary: `python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" tool <MARK>`
2. Cost breakdown: `python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" cost <MARK>`
3. Revert changes: `cd "D:\repos\Atlas3_EDEV" && git restore .`
