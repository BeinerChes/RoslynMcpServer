# Roslyn MCP vs Native Tools Benchmark

A fair comparison of Roslyn MCP tools vs Claude Code's native tools (Grep/Glob/Read/Edit) for C# development tasks.

## Quick Results

| Metric | MCP Tools | Native Tools |
|--------|-----------|--------------|
| Cost | $9.52 | $7.42 |
| Context | 89.3K | 98.3K |
| Task Complete | Yes | Partial |

**Key finding:** Native tools cannot replicate semantic analysis (dead code detection, impact analysis, accurate caller finding). Cost is similar, but capabilities differ significantly.

See [results.md](results.md) for full analysis.

## The Task

"Add validation to `FeatureSet.Save()` in Atlas3.sln"

11 steps covering:
- Understanding class structure (155 members)
- Finding callers (18 call sites)
- Impact analysis (44 affected symbols)
- Code modification
- Dead code detection
- Cleanup

See [test_task.md](test_task.md) for details.

## Running the Benchmark

### Prerequisites
- Claude Code with Opus 4
- Access to test solution (Atlas3.sln)
- Token tracker: `python token_tracker.py`

### MCP Session
1. Ensure MCP is enabled
2. Start fresh session
3. Follow [benchmark_prompt_mcp.md](benchmark_prompt_mcp.md)

### Native Session
1. Disable MCP: rename `.mcp.json` to `.mcp.json.disabled`
2. Start fresh session
3. Follow [benchmark_prompt_native.md](benchmark_prompt_native.md)

## Files

| File | Purpose |
|------|---------|
| results.md | Main comparison and analysis |
| results_mcp.md | Detailed MCP benchmark data |
| results_native.md | Detailed native benchmark data |
| test_task.md | Task definition |
| token_tracker.py | Token usage measurement |
| benchmark_prompt_*.md | Session prompts |
