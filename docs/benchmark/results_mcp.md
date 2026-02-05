# Benchmark Results: MCP Tools

**Date:** 2026-01-26
**Solution:** Atlas3.sln
**Task:** Add Save Validation

---

## Per-Step Results

| Step | Tool | Calls | New Context | Cost | Result |
|------|------|-------|-------------|------|--------|
| 0 | GetInstructions x2 | 2 | 15.4K | $0.29 | Loaded tools + code instructions |
| 1 | GetTypeMembers | 1 | 17.8K | $0.33 | 155 members found |
| 2 | GetMethodBody | 1 | 5.3K | $0.10 | Save() is 28 lines, calls PerformSave() |
| 3 | GetCallers | 1 | 4.5K | $0.09 | 18 callers across 12 files |
| 4 | GraphImpact | 1 | 11.3K | $0.21 | 44 affected symbols, 22 files |
| 5 | AddMember | 1 | 2.4K | $0.04 | Added ValidateBeforeSave() at line 148 |
| 6 | UpdateMethod | 1 | 3.0K | $0.06 | Save() now calls ValidateBeforeSave() |
| 7 | FindSymbol | 1 | 10.1K | $0.19 | 31 Validate* methods found |
| 8 | RenameSymbol | 2 | 4.3K | $0.08 | Renamed to ValidateSaveState, 2 files |
| 9 | GetDiagnostics | 2 | 2.6K | $0.05 | No errors in Atlas.Data (103 pre-existing) |
| 10 | FindDeadCode | 1 | 8.0K | $0.15 | 20 pre-existing dead code, none from changes |
| 11 | DeleteMember | 1 | 2.3K | $0.04 | Deleted ValidateSaveState() |

---

## Summary

```
=== Per-Tool Usage Since Line 15 ===

Tool                                           Calls  New Context     Output
---------------------------------------------------------------------------
(conversation)                                    55        48.8K        158
Bash                                              15        30.5K         26
Edit                                              12         3.6K         63
mcp__roslyn__FindSymbol                    2          874          7
mcp__roslyn__RenameSymbol                  2          770          2
mcp__roslyn__GetDiagnostics                2          655          4
Read                                               2          532          4
mcp__roslyn__GetTypeMembers               1          489          1
mcp__roslyn__GetInstructions               2          484          2
mcp__roslyn__UpdateMethod                  1          465          2
mcp__roslyn__GraphImpact                   1          368          6
mcp__roslyn__DeleteMember                  1          363          1
mcp__roslyn__FindDeadCode                 1          361          6
mcp__roslyn__GetMethodBody                1          355          2
mcp__roslyn__GetCallers                    1          354          6
mcp__roslyn__AddMember                     1          353          1
---------------------------------------------------------------------------
TOTAL                                            100        89.3K        291

Total cost: $9.52 (Opus 4 pricing)
```

## Cost Breakdown

```
=== Cost Breakdown Since Line 15 ===

Token Type                      Tokens       Cost
--------------------------------------------------
Input (non-cached)                   0    $0.0000
Cache write (new)                89.3K      $1.68
Cache read (reused)            5214.8K      $7.82
--------------------------------------------------
Total Input                    5304.1K      $9.50
Output                             291      $0.02
--------------------------------------------------
TOTAL                          5304.4K      $9.52

Pricing model: Opus 4
```
