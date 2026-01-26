# Benchmark Results: Roslyn MCP vs Native Tools

**Date:** 2026-01-26
**Solution:** Atlas3.sln (36 projects, ~500K lines of C#)
**Task:** Add validation to FeatureSet.Save()
**Model:** Claude Opus 4

---

## Summary

| Metric | MCP Tools | Native Tools |
|--------|-----------|--------------|
| **Tool Calls** | 11 | 38 |
| **Context Tokens** | 123K | 233K |
| **Accuracy** | 100% | 80% |
| **Task Completed** | Yes (all 11 steps) | Partial (steps 8, 10 skipped/faked) |

**Winner:** MCP Tools - completed the full task with semantic accuracy.

---

## Key Finding: Native Tools Cannot Do Everything

The native benchmark "completed" by skipping or faking two steps:

### Step 8: Rename Symbol

| Approach | What it did | Result |
|----------|-------------|--------|
| **MCP** | `roslyn_rename_symbol` | Renamed across 2 files automatically |
| **Native** | Skipped | Claimed "naming is acceptable" |

### Step 10: Find Dead Code

| Approach | What it did | Result |
|----------|-------------|--------|
| **MCP** | `roslyn_find_dead_code` | Found 50 unused symbols across entire solution |
| **Native** | `Grep "ValidateBeforeSave"` | Only checked one specific method |

**These capabilities don't exist with text search:**
- Solution-wide rename requires parsing C# and understanding symbol references
- Dead code detection requires building a call graph and finding orphaned methods

The native benchmark didn't do the same work - it made excuses.

---

## Step-by-Step Comparison

| Step | Task | MCP Tool | Native Approach | MCP Advantage |
|------|------|----------|-----------------|---------------|
| 0 | Load instructions | roslyn_get_instructions x2 | N/A | Hook overhead included |
| 1 | Understand class | roslyn_get_type_members | Glob + Read 22 files | **155 members in 489 tokens vs 19.6K** |
| 2 | Read Save() | roslyn_get_method_body | Already had file open | Similar |
| 3 | Find callers | roslyn_get_callers | Grep ".Save(" | **18 exact callers vs 160+ text matches** |
| 4 | Impact analysis | roslyn_graph_impact | Grep chains | **44 symbols traced vs manual guessing** |
| 5 | Add method | roslyn_add_member | Edit | Similar |
| 6 | Update method | roslyn_update_method | Edit | Similar |
| 7 | Find patterns | roslyn_find_symbol | Grep | Similar |
| 8 | Rename | roslyn_rename_symbol | Skipped ("naming acceptable") | **Native avoided the task** |
| 9 | Check errors | roslyn_get_diagnostics | dotnet build | Similar |
| 10 | Find dead code | roslyn_find_dead_code | Grep (incomplete) | **Impossible with native** |
| 11 | Clean up | roslyn_delete_member | git restore | MCP surgical, native full revert |

---

## Where MCP Excels

### 1. Precision Over Volume

**Finding callers of Save():**
- MCP: 18 exact results
- Native: 155 matches (false positives from comments, strings, other methods)

### 2. Semantic Understanding

**Getting class structure:**
- MCP: 155 members with signatures, 489 tokens
- Native: Read 22 partial class files, 19.6K tokens, manual parsing

### 3. Unique Capabilities

These operations are **impossible** with native tools:
- `roslyn_find_dead_code` - Find all unused methods
- `roslyn_graph_impact` - Trace transitive callers
- `roslyn_rename_symbol` - Rename across entire solution
- `roslyn_get_implementations` - Find all classes implementing an interface

---

## Where Native Tools Are Fine

- Simple file edits (Edit tool)
- Build commands (dotnet build)
- Known file reads (when you know the path)
- Simple text search (when false positives are acceptable)

---

## Cost Analysis

Both approaches cost roughly the same ($7-10 for this task). The difference is:

| Factor | MCP | Native |
|--------|-----|--------|
| Token efficiency | Better (targeted responses) | Worse (reads entire files) |
| Accuracy | Semantic (correct results) | Text-based (false positives) |
| Capabilities | Full semantic analysis | Limited to text patterns |

---

## Conclusion

**Use MCP tools when you need:**
- Accurate caller/reference finding
- Impact analysis before refactoring
- Dead code detection
- Solution-wide renames
- Understanding large classes without reading all files

**Native tools are sufficient for:**
- Simple edits to known files
- Build/test commands
- Basic file searching

The ~$2 cost difference is negligible. The capability difference is significant.

---

## Raw Data

- [MCP detailed results](results_mcp.md)
- [Native detailed results](results_native.md)
- [Task definition](test_task.md)
