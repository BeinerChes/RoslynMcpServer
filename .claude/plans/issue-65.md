# Plan: Graph tools cannot query constructors with .ctor syntax

## Problem Statement

`QueryGraph` fails to find constructors when queried with `.ctor` syntax because:
- Roslyn's `ToDisplayString()` returns `Namespace.Type.Type(params)` for constructors
- Users query with `Namespace.Type..ctor(params)`
- The SQL pattern matching doesn't handle this translation

## Root Cause

In `GraphDatabase.Symbols.cs:FindSymbolAsync`:
- Stored QualifiedName: `Atlas.Data.FeatureSet.FeatureSet(string)`
- User query: `Atlas.Data.FeatureSet..ctor(string)`
- SQL pattern `'%.' || @SearchName` doesn't match

## Solution

Modify `FindSymbolAsync` to detect `.ctor` queries and translate them:
1. Check if searchName contains `.ctor`
2. Extract the type path (e.g., `Atlas.Data.FeatureSet` from `Atlas.Data.FeatureSet..ctor(string)`)
3. Extract parameter signature if present
4. Search for symbols where `Name = '.ctor'` AND `QualifiedName` matches the type path pattern

## Completed Fixes

1. **GraphDatabase.Symbols.cs** - Added `FindConstructorAsync` method that translates `.ctor` syntax to Roslyn's actual naming convention:
   - `MyApp.Type..ctor(string)` → searches for `MyApp.Type.Type(string)`
   - Handles both full namespace and partial type paths
   - Returns candidates when multiple constructors exist

2. **GraphSymbolSearchTests.cs** - Added 6 unit tests covering:
   - Full path with params: `MyApp.Models.User..ctor(string)`
   - Multiple params: `MyApp.Models.User..ctor(string, int)`
   - No params, single constructor: `MyApp.Services.OrderService..ctor`
   - No params, multiple constructors: returns candidates
   - Empty parens: `OrderService..ctor()`
   - Partial path: `User..ctor(string)`

## Test Results

All 6 constructor tests passing:
```
Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6
```

## Current Status

✅ **Complete** - Real-world testing passed on Atlas3 solution.

## Real-World Test Results (Atlas3.sln)

| Query | Result |
|-------|--------|
| `Atlas.Data.FeatureSet..ctor` | Multiple constructors found, lists 5 candidates ✓ |
| `Atlas.Data.FeatureSet..ctor(string)` | Found exact: `FeatureSet.FeatureSet(string)` ✓ |
| `FeatureSet..ctor(string, bool)` | Partial path resolved correctly ✓ |
| `Atlas.Data.FeatureSet..ctor(string, bool, bool)` | Full path with callees returned ✓ |

## Next Steps

1. ~~Modify `FindSymbolAsync` to handle `.ctor` syntax~~ ✅
2. ~~Add unit tests for constructor queries~~ ✅
3. ~~Test with real solution~~ ✅
4. Commit, push, and create PR

## Workflow Reminder (MANDATORY)
After each fix:
1. Run `GetDiagnostics` to check for errors
2. Re-read and follow CLAUDE.md
3. Update this plan
4. Keep working until issue is resolved
