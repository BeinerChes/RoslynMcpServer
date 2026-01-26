# Benchmark Results: Native Tools

**Date:** 2026-01-26
**Solution:** Atlas3.sln
**Task:** Add Save Validation

---

## Per-Step Results

| Step | Tool | Calls | New Context | Cost | Result |
|------|------|-------|-------------|------|--------|
| 1 | Glob, Read | 4 | 19.6K | $0.67 | Found 22 partial class files, read main + SaveLoad |
| 2 | (included in 1) | 0 | 0 | $0.00 | Save() already read - lines 87-114 in SaveLoad.cs |
| 3 | Grep | 1 | 24.2K | $0.93 | Found 160+ .Save() calls across codebase |
| 4 | Grep | 1 | 6.2K | $0.48 | Traced callers - UI operations, map functions, ViewModels |
| 5 | Grep, Read, Edit | 4 | 21.7K | $1.33 | Added ValidateBeforeSave() method |
| 6 | Edit | 1 | 2.5K | $0.53 | Updated Save() to call ValidateBeforeSave() |
| 7 | Grep | 2 | 3.4K | $0.69 | Found 20 files with Validate patterns - no naming convention |
| 8 | (skip) | 0 | 0 | $0.00 | **SKIPPED** - claimed "naming is acceptable" |
| 9 | Bash | 2 | 13.2K | $0.99 | Build succeeded: 0 errors, 1 warning |
| 10 | Grep | 1 | 2.3K | $0.61 | **INCOMPLETE** - only checked one method |
| 11 | Bash | 1 | 3.2K | $0.64 | Reverted all changes with git restore |

---

## Limitations: Steps 8 and 10

The native benchmark avoided two operations it couldn't properly do:

### Step 8: Rename Symbol

**What MCP did:** `roslyn_rename_symbol` renamed `ValidateBeforeSave` → `ValidateSaveState` across 2 files automatically.

**What native did:** Claimed "naming is acceptable" and skipped the step entirely.

**The honest approach would be:**
1. Grep for all occurrences (includes false positives in strings/comments)
2. Manually edit each file
3. Rebuild and hope nothing broke

### Step 10: Find Dead Code

**What MCP did:** `roslyn_find_dead_code` analyzed entire solution, found 20 unused methods.

**What native did:** Grepped for "ValidateBeforeSave" to verify it was called - only checks ONE method.

**Why this is impossible with native tools:**
- Need to parse C# to find all method declarations
- Build call graph of entire solution
- Find methods with zero callers
- Filter false positives (entry points, event handlers, etc.)

**Bottom line:** The native benchmark "completed" by either skipping or faking these steps.

---

## Summary

```
=== Per-Tool Usage Since Line 6 ===

Tool                                           Calls  New Context     Output
---------------------------------------------------------------------------
(conversation)                                    52        59.6K        126
Bash                                              14        23.1K         27
Edit                                              11         8.2K         44
Read                                               5         3.7K         10
Grep                                               7         2.3K         17
Glob                                               1          851          1
Write                                              1          429          1
---------------------------------------------------------------------------
TOTAL                                             91        98.3K        226

Total cost: $7.42 (Opus 4 pricing)
```

## Cost Breakdown

```
=== Cost Breakdown Since Line 6 ===

Token Type                      Tokens       Cost
--------------------------------------------------
Input (non-cached)                   0    $0.0000
Cache write (new)                98.3K      $1.84
Cache read (reused)            3705.0K      $5.56
--------------------------------------------------
Total Input                    3803.3K      $7.40
Output                             226      $0.02
--------------------------------------------------
TOTAL                          3803.5K      $7.42

Pricing model: Opus 4
```
