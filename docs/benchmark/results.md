# Benchmark Results: Roslyn MCP vs Native Claude Code Tools

**Date:** (pending)
**Solution:** Atlas3.sln (36 projects, ~500K lines of C#)
**Task:** Add validation to FeatureSet.Save()

---

## Executive Summary

| Metric | MCP Tools | Native Tools |
|--------|-----------|--------------|
| **Tool Calls** | TBD | TBD |
| **Context Tokens** | TBD | TBD |
| **Accuracy** | TBD | TBD |
| **Cost** | TBD | TBD |

---

## The Task

"Add validation to `FeatureSet.Save()` that prevents saving empty feature sets."

11 steps covering:
1. **Understanding** (Steps 1-4): Class structure, method lookup, caller tracing, impact analysis
2. **Modification** (Steps 5-8): Add method, update method, find patterns, rename
3. **Verification** (Steps 9-11): Compile check, dead code, cleanup

See [test_task.md](test_task.md) for full details.

---

## Detailed Results

- [MCP Results](results_mcp.md)
- [Native Results](results_native.md)

---

## Running the Benchmark

### MCP Session
1. Start fresh Claude Code session with MCP enabled
2. Paste contents of [benchmark_prompt_mcp.md](benchmark_prompt_mcp.md)

### Native Session
1. Disable MCP (rename `.mcp.json` to `.mcp.json.disabled`)
2. Start fresh Claude Code session
3. Paste contents of [benchmark_prompt_native.md](benchmark_prompt_native.md)
