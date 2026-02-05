# FindDeadCode

## Description

FindDeadCode identifies methods and properties across a solution that have no callers — code that exists but is never used. It combines a fast graph database pass to find candidates with Roslyn's `FindReferencesAsync` for accurate validation, eliminating false positives from text-based searches.

The tool handles:
- Solution-wide dead code detection in a single call
- Two-phase analysis: graph DB for speed, Roslyn for accuracy
- Smart exclusions for entry points (`Main`, `Dispose`, `OnClick`, etc.)
- Structural detection of pure model/DTO classes (auto-properties only → excluded)
- Filtering by visibility (public/private), test files, type patterns, file patterns
- Results grouped by file with qualified names and line numbers
- Auto-builds the graph database if it doesn't exist

## Comparison with Native Claude Code Tools

### vs Grep + Read (manual dead code search)
- **Grep** finds text matches for a method name — but can't distinguish definitions from call sites
- **FindDeadCode** uses Roslyn's semantic `FindReferencesAsync` to count actual references
- **Grep** requires searching for every method individually across the entire codebase
- **FindDeadCode** scans all methods in one call via the graph database
- **Grep** matches false positives: comments, strings, similarly-named methods (`Add` matches `AddToHistory`)
- **FindDeadCode** only counts actual code references, not text matches
- **Grep** can't detect model/DTO classes or entry points — requires manual judgment
- **FindDeadCode** automatically excludes `Main`, `Dispose`, event handlers, and structurally-detected pure model classes

### vs GetCallers (per-method approach)
- **GetCallers** checks one method at a time — you'd need to call it for every method in the solution
- **FindDeadCode** checks all methods in a single call
- **GetCallers** is useful for verifying a specific method; **FindDeadCode** is for discovery

### When to use FindDeadCode
- Periodic codebase cleanup to remove technical debt
- Before major refactoring to identify what can be safely removed
- After removing features to find orphaned code
- Code review to verify new code is actually reachable
- Combined with `DeleteMember` for automated cleanup workflows

## Real-World Example

### Scenario
Find all unused methods and properties in a solution containing a Calculator class, TaskBoard class, and TaskItem record, where some methods are called from Program.cs and others are not.

**Test project:** SharpOps.Examples.csproj (actual testing, not estimates)

**Setup:** Added 3 unused methods (`Calculator.CalculateAverage`, `TaskBoard.ArchiveCompleted`, `TaskBoard.SearchByTitle`) alongside existing code that is partially used from Program.cs.

### Approach 1: Using Native Tools

**Step 1: Find all source files**
```
Glob(pattern: "SharpOps.Examples/**/*.cs")
```
Returns 5 source files (excluding obj/).
**Tokens:** ~200

**Step 2: Read all source files to catalog methods**
```
Read("Calculator.cs")      # ~400 tokens
Read("TaskBoard.cs")       # ~700 tokens
Read("TaskItem.cs")        # ~250 tokens
Read("Program.cs")         # ~400 tokens
Read("Priority.cs")        # ~100 tokens
```
Must read every file to build a mental inventory of all methods.
**Tokens:** ~1,850

**Step 3: For each method, Grep for references**
```
Grep("CalculateAverage", path: "SharpOps.Examples")  # 1 match (definition only)
Grep("ArchiveCompleted", path: "SharpOps.Examples")   # 1 match (definition only)
Grep("SearchByTitle", path: "SharpOps.Examples")      # 1 match (definition only)
Grep("AddToHistory", path: "SharpOps.Examples")       # 1 match (definition only)
Grep("GetCount", path: "SharpOps.Examples")           # 1 match (definition only)
Grep("Add\\(", path: "SharpOps.Examples")             # false positives with AddTask
Grep("Clear\\(", path: "SharpOps.Examples")           # matches _history.Clear() too
```
Must grep each method individually. Common names like `Add` and `Clear` produce false positives.
**Tokens:** ~700

**Step 4: Manual analysis**
Cross-reference grep results against file reads to determine which matches are definitions vs calls. For ambiguous names (`Add`, `Clear`, `Count`), must re-read files to verify context.
**Tokens:** ~500 (additional re-reads and reasoning)

**Total: ~3,250 tokens, 12+ operations**

**Limitations:**
- Only searched SharpOps.Examples — a real analysis must search the entire solution
- Common method names produce false positives that need manual filtering
- Can't distinguish method definitions from call sites
- No awareness of interfaces, virtual methods, or implicit usage
- Must be repeated for every project in the solution
- Doesn't detect entry points or model classes automatically

### Approach 2: Using FindDeadCode (Roslyn)

**Step 1: Find all dead code**
```
FindDeadCode(includePrivate: true)
```
**Result:** 45 dead symbols across 25 files, grouped by file. For SharpOps.Examples:
- `Calculator.cs`: 5 dead methods (Add, AddToHistory, GetCount, Clear, CalculateAverage)
- `TaskBoard.cs`: 2 dead methods (ArchiveCompleted, SearchByTitle)
- `TaskItem.cs`: 1 unused property (CreatedAt — never read despite being on a used type)

Validated 84 candidates, filtered 39 false positives via Roslyn `FindReferencesAsync`.
**Tokens:** ~800 (response with 45 results)

**Total: ~800 tokens, 1 operation**

### Comparison Summary

| Aspect | Native (Grep + Read) | FindDeadCode (Roslyn) |
|--------|---------------------|----------------------|
| **Token usage** | ~3,250 | ~800 |
| **Operations** | 12+ | 1 |
| **Scope** | One project at a time | Entire solution |
| **Accuracy** | Low (text matching) | High (semantic references) |
| **False positives** | Many (common names, comments) | Filtered out (39 removed) |
| **Entry point detection** | Manual | Automatic |
| **Model/DTO exclusion** | Manual | Structural detection |
| **Scales to large solutions** | Poorly (O(methods × files)) | Well (graph DB + targeted validation) |

**FindDeadCode uses 75% fewer tokens** (800 vs 3,250) and finds dead code across the **entire solution** — not just one project. In a real-world solution with hundreds of files, the native approach becomes impractical while FindDeadCode scales via the graph database.

## How It Works

### Two-Phase Analysis

FindDeadCode uses a two-phase approach for both speed and accuracy:

**Phase 1: Graph Database (fast candidate finding)**
The call graph database (built by `GraphAnalyze`) stores all method/property symbols and their caller relationships. `GetSymbolsWithNoCallersAsync` efficiently queries for symbols with zero incoming edges. This narrows thousands of symbols down to ~100 candidates in milliseconds.

**Phase 2: Roslyn Validation (accurate filtering)**
Each candidate is validated using `SymbolFinder.FindReferencesAsync`, which performs semantic reference finding. This filters out false positives from the graph — symbols that are referenced through patterns the graph may not capture (reflection, dynamic dispatch, etc.). In testing, this phase filtered 39 of 84 candidates as false positives.

### Smart Exclusions

Before Roslyn validation, candidates are filtered through several heuristics:

**Entry points** — Methods that are called implicitly:
- `Main`, `ConfigureServices`, `Configure` (framework entry points)
- `Dispose`, `DisposeAsync` (IDisposable pattern)
- `On*` methods (event handlers: `OnClick`, `OnLoad`, etc.)
- `get_*` / `set_*` (property accessors called implicitly)

**Pure model/DTO classes** — Types with only auto-properties:
- Detected structurally (not by naming convention)
- A class with only `{ get; set; }` properties and no methods is treated as a data model
- Properties on these classes are excluded since they're used for serialization/binding

**Visibility filtering** — Private members excluded by default (often intentionally unused helper methods). Enable with `includePrivate: true`.

**Test files** — Test project files excluded by default. Enable with `includeTests: true`.

### Invocation

**Basic usage (find all public dead code):**
```
FindDeadCode()
```

**Include private members:**
```
FindDeadCode(includePrivate: true)
```

**Exclude specific type patterns:**
```
FindDeadCode(excludeTypePatterns: ["Result", "Response", "Dto"])
```

**Exclude specific file paths:**
```
FindDeadCode(excludeFilePatterns: ["Models/", "Generated/"])
```

**Limit results:**
```
FindDeadCode(maxResults: 20)
```

### Response Format

**Success:**
```json
{
  "success": true,
  "totalFound": 45,
  "totalFiles": 25,
  "byFile": [
    {
      "filePath": "SharpOps.Examples\\Calculator.cs",
      "fileName": "Calculator.cs",
      "count": 5,
      "symbols": [
        {
          "name": "CalculateAverage",
          "qualifiedName": "SharpOps.Examples.Calculator.CalculateAverage()",
          "kind": "Method",
          "filePath": "SharpOps.Examples\\Calculator.cs",
          "fileName": "Calculator.cs",
          "line": 41
        }
      ]
    }
  ],
  "note": "Validated 84 candidates, filtered 39 false positives."
}
```

**Error (no graph database):**
```json
{
  "success": false,
  "error": "Failed to build graph: ..."
}
```

Note: If no graph database exists, FindDeadCode automatically triggers `GraphAnalyze` to build one before proceeding.

### Common Workflows

**1. Cleanup dead code:**
```
1. FindDeadCode(includePrivate: true)       → identify dead symbols
2. Review results (check each is truly dead)
3. DeleteMember(typeName, memberName)        → remove each dead symbol
4. GetDiagnostics()                          → verify no compilation errors
5. RemoveUnnecessaryUsings()                 → clean up orphaned usings
```

**2. Pre-refactoring audit:**
```
1. FindDeadCode()                            → baseline dead code count
2. [perform refactoring]
3. FindDeadCode()                            → check for newly orphaned code
4. Compare results to find code orphaned by the refactoring
```

**3. Focused cleanup on a project:**
```
1. FindDeadCode(excludeFilePatterns: ["Tests/", "Migrations/"])
   → find dead code excluding test and migration files
2. For each result in the target project:
   DeleteMember(typeName, memberName)
```

### When Native Tools Are Better

- **Non-C# files** — FindDeadCode only works on C# symbols
- **String-referenced code** — Methods called via reflection or string-based dispatch won't be found by Roslyn
- **Convention-based frameworks** — Some frameworks call methods by convention (e.g., ASP.NET controller actions) — these may appear as dead code but aren't
- **Single method check** — If you just want to know if one specific method has callers, `GetCallers` is faster and more direct
