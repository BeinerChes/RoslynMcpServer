# Benchmark Test Task: Complete Tool Comparison

## Target
- **Solution:** `D:\repos\Atlas3_EDEV\Atlas3.sln`
- **Class:** `Atlas.Data.FeatureSet` (155 members, 13+ partial files)
- **Test Branch:** `temp_claude_benchmark` (for modifications)

---

## Tools Being Compared

### MCP Tools (Roslyn)
| Category | Tools |
|----------|-------|
| **Navigation** | roslyn_find_symbol, roslyn_get_type_members, roslyn_get_method_body |
| **References** | roslyn_get_references, roslyn_get_callers, roslyn_get_implementations |
| **Modification** | roslyn_update_method, roslyn_add_member, roslyn_delete_member, roslyn_rename_symbol |
| **Diagnostics** | roslyn_get_diagnostics, roslyn_apply_code_fix |
| **Call Graph** | roslyn_graph_analyze, roslyn_query_graph, roslyn_graph_impact, roslyn_find_dead_code |

### Native Tools (Claude Code)
| Category | Tools |
|----------|-------|
| **Navigation** | Glob, Grep, Read |
| **Modification** | Edit, Write |
| **Diagnostics** | Bash (dotnet build) |

---

## Task Definition

### PHASE 1: Code Navigation (Steps 1-5)

#### Step 1: Understand Class Structure
**Goal:** Get complete picture of FeatureSet's members and files

**MCP:** `roslyn_get_type_members`
**Native:** Glob + Read multiple files

#### Step 2: Find Specific Method
**Goal:** Locate `Save()` method and get full implementation

**MCP:** `roslyn_get_method_body`
**Native:** Grep + Read

#### Step 3: Find All Callers
**Goal:** Find all places that call `FeatureSet.Save()`

**MCP:** `roslyn_get_callers`
**Native:** Grep ".Save(" + filter false positives

#### Step 4: Impact Analysis
**Goal:** Transitive impact if Save() changes

**MCP:** `roslyn_graph_impact`
**Native:** Recursive Grep for each caller

#### Step 5: Find Related Methods
**Goal:** Find all Save-related methods in FeatureSet

**MCP:** `roslyn_find_symbol`
**Native:** Grep "Save" in FeatureSet files

---

### PHASE 2: Code Modification (Steps 6-9)

#### Step 6: Add a New Method
**Goal:** Add a test method to FeatureSet

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

**MCP:** `roslyn_add_member`
**Native:** Edit (find insertion point, add code)

#### Step 7: Update an Existing Method
**Goal:** Add a comment to the beginning of Save() method

Add this line after `public virtual void Save() {`:
```csharp
// BENCHMARK: Testing method modification
```

**MCP:** `roslyn_update_method`
**Native:** Edit

#### Step 8: Rename a Symbol
**Goal:** Rename `BenchmarkTestMethod` to `BenchmarkTestMethodRenamed`

**MCP:** `roslyn_rename_symbol`
**Native:** Grep + multiple Edit calls

#### Step 9: Delete the Test Method
**Goal:** Remove the benchmark test method we added

**MCP:** `roslyn_delete_member`
**Native:** Edit (find and remove entire method)

---

### PHASE 3: Diagnostics (Steps 10-11)

#### Step 10: Get Build Diagnostics
**Goal:** Check for any compiler errors/warnings in FeatureSet

**MCP:** `roslyn_get_diagnostics`
**Native:** Bash `dotnet build` + parse output

#### Step 11: Find Dead Code
**Goal:** Find unused methods in Atlas.Data project

**MCP:** `roslyn_find_dead_code`
**Native:** Manual analysis (not really feasible)

---

## Metrics to Capture

| Metric | Description |
|--------|-------------|
| Tool Calls | Number of tool invocations |
| Accuracy | Correct results (1-5) |
| Completeness | All results found (1-5) |
| Errors | Any errors encountered |
| Notes | Observations |

---

## After Testing

1. **Revert Atlas branch:** `git checkout main && git branch -D temp_claude_benchmark`
2. **Re-enable MCP:** Rename `.mcp.json.disabled` back
3. **Compare results** in results.md
