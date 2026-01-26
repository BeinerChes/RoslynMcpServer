# MCP Benchmark - Fresh Session Prompt

**Copy and paste this into a fresh Claude Code session.**

---

## Setup

You are running a benchmark to measure Roslyn MCP tool efficiency.

**Solution:** `D:\repos\Atlas3_EDEV\Atlas3.sln`
**Target Class:** `Atlas.Data.FeatureSet` (155 members, 13 partial files)
**Tracker:** `C:\Users\cbein\source\repos\RoslynMcpServer\docs\benchmark\token_tracker.py`

**Rules:**
1. Before EACH step, run: `python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" mark`
2. Use ONLY the specified MCP tool for each step (one tool call per step)
3. After ALL steps, run: `python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" tool 0`
4. Record results in `C:\Users\cbein\source\repos\RoslynMcpServer\docs\benchmark\results_mcp.md`

---

## PHASE 1: Code Navigation

### Step 1: Understand Class Structure
**Tool:** `roslyn_get_type_members(solutionPath, typeName: "FeatureSet", compact: false)`
**Goal:** Get all 155 members with file locations

### Step 2: Find Save() Method
**Tool:** `roslyn_get_method_body(solutionPath, typeName: "FeatureSet", methodName: "Save")`
**Goal:** Get full source code of Save() method

### Step 3: Find All Callers of Save()
**Tool:** `roslyn_get_callers(solutionPath, filePath: "<path from step 2>", line: <line from step 2>, column: 25)`
**Goal:** Find all call sites of FeatureSet.Save()

### Step 4: Impact Analysis
**Tool:** `roslyn_graph_impact(solutionPath, symbolName: "Atlas.Data.FeatureSet.Save")`
**Goal:** Find transitive impact (callers of callers)

### Step 5: Find Related Save Methods
**Tool:** `roslyn_find_symbol(solutionPath, pattern: "Save", symbolKind: "member")`
**Goal:** Find all Save-related methods in solution

---

## PHASE 2: Code Modification

### Step 6: Add a New Method
**Tool:** `roslyn_add_member(solutionPath, typeName: "FeatureSet", memberCode: "<see below>")`
```csharp
/// <summary>
/// Benchmark test method - to be removed
/// </summary>
public void BenchmarkTestMethod()
{
    // This method was added by benchmark test
    System.Diagnostics.Debug.WriteLine("Benchmark test");
}
```
**Goal:** Add method to FeatureSet class

### Step 7: Update Save() Method
**Tool:** `roslyn_update_method(solutionPath, typeName: "FeatureSet", methodName: "Save", newSourceCode: "<modified source>")`
**Goal:** Add comment `// BENCHMARK: Testing method modification` after opening brace
**Note:** First get current source with roslyn_get_method_body, then modify it

### Step 8: Rename the Test Method
**Tool:** `roslyn_rename_symbol(solutionPath, filePath: "<path>", line: <line>, column: <col>, newName: "BenchmarkTestMethodRenamed")`
**Goal:** Rename BenchmarkTestMethod across solution

### Step 9: Delete the Test Method
**Tool:** `roslyn_delete_member(solutionPath, typeName: "FeatureSet", memberName: "BenchmarkTestMethodRenamed")`
**Goal:** Remove the benchmark test method

---

## PHASE 3: Diagnostics

### Step 10: Get Build Diagnostics
**Tool:** `roslyn_get_diagnostics(solutionPath, projectFilter: "Atlas.Data")`
**Goal:** Get compiler warnings/errors for Atlas.Data project

### Step 11: Find Dead Code
**Tool:** `roslyn_find_dead_code(solutionPath, maxResults: 50)`
**Goal:** Find unused methods/properties

---

## After All Steps

1. Run token tracker:
```bash
python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" tool 0
```

2. Revert Atlas changes:
```bash
cd "D:\repos\Atlas3_EDEV" && git restore . && git status
```

3. Update results file with metrics

---

## Expected Output Format

After each step, briefly note:
- Tool call count (should be 1)
- Key results (member count, callers found, etc.)
- Any errors

At the end, the token tracker will show per-tool context usage.
