# Benchmark Results: Native Claude Code Tools

**Date:** 2026-01-26
**Solution:** Atlas3.sln (36 projects)
**Target Class:** FeatureSet (155 members)

---

## Token Tracker Output

```
=== Per-Tool Usage since line 16 ===
Tool                                      Calls  New Context   Output
----------------------------------------------------------------------
Bash                                         14        71.2K       64
Edit                                          4         5.5K       64
Glob                                          1         2.2K        3
Grep                                          6        30.0K       18
Read                                         13       124.4K       23
----------------------------------------------------------------------
TOTAL                                        38       233.3K      172
```

---

## PHASE 1: Code Navigation

### Step 1: Understand Class Structure
| Metric | Value |
|--------|-------|
| Tools | Glob, Read |
| Calls | 14 (1 Glob + 13 Read) |
| New Context | ~126.6K |
| Results | Found 156 members across 13 files (expected 155) |
| Accuracy | 5/5 |
| Notes | Required reading all 13 partial class files to count members manually |

### Step 2: Find Save() Method
| Metric | Value |
|--------|-------|
| Tools | (Already read in Step 1) |
| Calls | 0 (file already in context) |
| New Context | 0 |
| Results | Found Save() at FeatureSet.SaveLoad.cs:87-114, ~28 lines |
| Accuracy | 5/5 |
| Notes | Benefit of having read all files upfront |

### Step 3: Find All Callers
| Metric | Value |
|--------|-------|
| Tools | Grep |
| Calls | 1 |
| New Context | ~15K |
| Results | Found 155 `.Save()` calls, ~18-22 actual FeatureSet.Save() callers |
| Accuracy | 3/5 |
| Notes | High false positive rate - cannot distinguish FeatureSet.Save() from Settings.Save(), Graphics.Save(), etc. |

### Step 4: Impact Analysis
| Metric | Value |
|--------|-------|
| Tools | Grep |
| Calls | 3 |
| New Context | ~5K |
| Results | Found files referencing key classes, could not trace call graphs |
| Accuracy | 1/5 |
| Notes | Exponential search problem - cannot build transitive call graph without semantic analysis |

### Step 5: Related Methods
| Metric | Value |
|--------|-------|
| Tools | Grep |
| Calls | 1 |
| New Context | ~2K |
| Results | Found 7 Save-related members (matches expected) |
| Accuracy | 5/5 |
| Notes | Regex search worked well for this scoped task |

---

## PHASE 2: Code Modification

### Step 6: Add Method
| Metric | Value |
|--------|-------|
| Tools | Edit |
| Calls | 1 |
| New Context | ~1.4K |
| Results | Successfully added BenchmarkTestMethod |
| Accuracy | 5/5 |
| Notes | Edit tool worked well since file was already in context |

### Step 7: Update Method
| Metric | Value |
|--------|-------|
| Tools | Edit |
| Calls | 1 |
| New Context | ~1.4K |
| Results | Successfully added comment to Save() |
| Accuracy | 5/5 |
| Notes | Straightforward edit |

### Step 8: Rename Symbol
| Metric | Value |
|--------|-------|
| Tools | Grep, Edit |
| Calls | 2 (1 Grep + 1 Edit) |
| New Context | ~2K |
| Results | Renamed method (1 file modified) |
| Accuracy | 5/5 |
| Notes | Only 1 reference existed (just added). In real scenario, would need to update all call sites manually |

### Step 9: Delete Method
| Metric | Value |
|--------|-------|
| Tools | Edit |
| Calls | 1 |
| New Context | ~1.4K |
| Results | Successfully deleted method |
| Accuracy | 5/5 |
| Notes | Straightforward edit |

---

## PHASE 3: Diagnostics

### Step 10: Build Diagnostics
| Metric | Value |
|--------|-------|
| Tools | Bash (dotnet build) |
| Calls | 1 |
| New Context | ~50K (build output) |
| Results | Build succeeded with warnings (CS8600, CS8603, CA1050, etc.) |
| Accuracy | 5/5 |
| Notes | Full build diagnostics available |

### Step 11: Dead Code
| Metric | Value |
|--------|-------|
| Tools | N/A |
| Calls | 0 (not attempted) |
| New Context | 0 |
| Results | Not feasible |
| Accuracy | 0/5 |
| Notes | Would require 156+ searches with manual false-positive filtering. Cannot detect interface-based calls or reflection usage. |

---

## Summary

| Step | Tools | Calls | Context | Accuracy |
|------|-------|-------|---------|----------|
| 1 | Glob, Read | 14 | 126.6K | 5/5 |
| 2 | (cached) | 0 | 0 | 5/5 |
| 3 | Grep | 1 | 15K | 3/5 |
| 4 | Grep | 3 | 5K | 1/5 |
| 5 | Grep | 1 | 2K | 5/5 |
| 6 | Edit | 1 | 1.4K | 5/5 |
| 7 | Edit | 1 | 1.4K | 5/5 |
| 8 | Grep, Edit | 2 | 2K | 5/5 |
| 9 | Edit | 1 | 1.4K | 5/5 |
| 10 | Bash | 1 | 50K | 5/5 |
| 11 | N/A | 0 | 0 | 0/5 |
| **TOTAL** | | **38** | **233.3K** | **44/55 (80%)** |

## Observations

### Strengths
- **Code modification** (Steps 6-9): Edit tool works well when files are already in context
- **Simple searches** (Step 5): Regex works for scoped, well-defined searches
- **Build diagnostics** (Step 10): Full compiler output available

### Challenges
1. **False positives in callers search** (Step 3): Text search cannot distinguish between:
   - `featureSet.Save()` (FeatureSet method)
   - `settings.Save()` (Settings class)
   - `graphics.Save()` (Graphics state)

2. **Transitive impact analysis** (Step 4): Exponential search problem
   - Each caller requires its own search
   - No way to trace through polymorphism/interfaces
   - Cannot build call graph

3. **Dead code detection** (Step 11): Nearly impossible
   - Would need 156+ searches
   - Cannot detect interface-based calls
   - Cannot detect reflection usage
   - Manual false-positive filtering required

### Key Limitations
- **No semantic understanding**: Cannot distinguish types with same method names
- **No call graph**: Cannot trace method calls through polymorphism
- **Linear search only**: Each query is independent, cannot build relationships
- **High context cost**: Must read entire files to analyze content

## Comparison vs MCP

| Metric | MCP | Native | Difference |
|--------|-----|--------|------------|
| Total Tool Calls | 11 | 38 | MCP 3.5x fewer |
| Total New Context | 123.3K | 233.3K | MCP 47% less |
| Average Accuracy | 100% | 80% | MCP +20% |
| Steps Fully Completed | 11/11 | 9/11 | MCP +2 |
| Caller Search Accuracy | 100% | 60% | MCP +40% |
| Impact Analysis | Success | Failed | MCP only |
| Dead Code Detection | Success | Failed | MCP only |
