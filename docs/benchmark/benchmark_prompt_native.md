# Native Tools Benchmark - Fresh Session Prompt

**Copy and paste this into a fresh Claude Code session (with MCP DISABLED).**

---

## Pre-requisites

**IMPORTANT:** Before starting, disable MCP in RoslynMcpServer:
```bash
cd "C:\Users\cbein\source\repos\RoslynMcpServer"
mv .mcp.json .mcp.json.disabled
```

---

## Setup

You are running a benchmark to measure native Claude Code tool efficiency.
This is a comparison against Roslyn MCP tools.

**Solution:** `D:\repos\Atlas3_EDEV\Atlas3.sln`
**Target Class:** `Atlas.Data.FeatureSet` (155 members, 13 partial files)
**Tracker:** `C:\Users\cbein\source\repos\RoslynMcpServer\docs\benchmark\token_tracker.py`

**Rules:**
1. Before EACH step, run: `python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" mark`
2. Use ONLY native tools: Glob, Grep, Read, Edit, Write, Bash
3. After ALL steps, run: `python "C:/Users/cbein/source/repos/RoslynMcpServer/docs/benchmark/token_tracker.py" tool 0`
4. Record results in `C:\Users\cbein\source\repos\RoslynMcpServer\docs\benchmark\results_native.md`

---

## PHASE 1: Code Navigation

### Step 1: Understand Class Structure
**Tools:** Glob + Read
**Goal:** Find all FeatureSet*.cs files, read them, count all members (constructors, methods, properties, fields, events)
**Expected:** 155 members across 13 files

### Step 2: Find Save() Method
**Tools:** Grep + Read
**Goal:** Find and extract the full Save() method implementation
**Expected:** Method in FeatureSet.SaveLoad.cs, ~28 lines

### Step 3: Find All Callers of Save()
**Tools:** Grep
**Goal:** Find all places that call `.Save()` on FeatureSet instances
**Challenge:** Filter out false positives (other Save methods, comments, strings)
**Expected:** ~18 callers

### Step 4: Impact Analysis
**Tools:** Grep (recursive)
**Goal:** For each caller found in Step 3, find ITS callers (transitive impact)
**Expected:** ~39 affected symbols across ~22 files

### Step 5: Find Related Save Methods
**Tools:** Grep
**Goal:** Find all methods in FeatureSet with "Save" in the name
**Expected:** 7 methods (Save, SaveAs overloads, PerformSave, OnFeatureSetSaved, FeatureSetSaved event)

---

## PHASE 2: Code Modification

### Step 6: Add a New Method
**Tools:** Read + Edit
**Goal:** Add this method to FeatureSet.cs (find appropriate location first):
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

### Step 7: Update Save() Method
**Tools:** Read + Edit
**Goal:** Add comment `// BENCHMARK: Testing method modification` after Save() opening brace

### Step 8: Rename the Test Method
**Tools:** Grep + Edit (multiple)
**Goal:** Rename BenchmarkTestMethod to BenchmarkTestMethodRenamed everywhere
**Challenge:** Must find and update all references

### Step 9: Delete the Test Method
**Tools:** Read + Edit
**Goal:** Remove the entire BenchmarkTestMethodRenamed method

---

## PHASE 3: Diagnostics

### Step 10: Get Build Diagnostics
**Tools:** Bash (dotnet build)
**Goal:** Build Atlas.Data project and capture warnings/errors
```bash
cd "D:\repos\Atlas3_EDEV" && dotnet build Atlas.Data/Atlas.Data.csproj 2>&1 | head -100
```

### Step 11: Find Dead Code
**Tools:** Manual analysis (Grep + Read)
**Goal:** Find methods with no callers
**Challenge:** This is very difficult without semantic analysis - note limitations

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

4. Re-enable MCP:
```bash
cd "C:\Users\cbein\source\repos\RoslynMcpServer"
mv .mcp.json.disabled .mcp.json
```

---

## Expected Challenges

| Step | Challenge |
|------|-----------|
| 1 | Must read 13 files and manually count members |
| 3 | False positives from text search (other .Save() calls) |
| 4 | Exponential search - each caller needs its own search |
| 8 | Must find all references manually |
| 11 | Nearly impossible without semantic analysis |

---

## Notes for Comparison

Track these metrics:
- Tool calls per step
- New context tokens per step
- Accuracy (did you find the correct results?)
- Completeness (did you find ALL results?)
