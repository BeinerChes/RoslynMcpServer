# Roslyn MCP Server vs Native Claude Code - Benchmark

## Purpose

A fair, skeptical comparison to determine if Roslyn MCP tools actually improve Claude Code's ability to work with C# codebases.

## Methodology

### Test Subject
- **Solution:** `D:\repos\Atlas3_EDEV\Atlas3.sln`
- **Target Class:** `FeatureSet` (large, complex class)

### Test Task
A realistic development task that requires:
1. Understanding class structure
2. Finding references/callers
3. Making code modifications
4. Impact analysis

### Metrics Captured

| Metric | Description |
|--------|-------------|
| **Token Usage** | Input + output tokens for each operation |
| **Time** | Wall clock time for each operation |
| **Accuracy** | Did it find the correct results? |
| **Completeness** | Did it find ALL relevant results? |
| **Tool Calls** | Number of tool invocations required |
| **Context Overhead** | Tokens spent on re-reading instructions (hooks) |

### Phases

1. **Phase 1: MCP Tools** - Complete task using Roslyn MCP tools
2. **Phase 2: Native Tools** - Complete same task using only Read/Edit/Grep/Glob
3. **Phase 3: Analysis** - Compare metrics and draw conclusions

## Tool Documentation

See individual tool files in this directory:
- [roslyn_find_symbol.md](tools/roslyn_find_symbol.md)
- [roslyn_get_type_members.md](tools/roslyn_get_type_members.md)
- [roslyn_get_method_body.md](tools/roslyn_get_method_body.md)
- etc.

## Test Task

See [test_task.md](test_task.md) for the specific task definition.

## Results

See [results.md](results.md) for final comparison.
