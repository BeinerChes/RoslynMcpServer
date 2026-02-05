# GetReferences

## Description

GetReferences finds all references to a symbol at a given file position across the entire solution. It uses Roslyn's semantic understanding to locate every place where a symbol (type, method, property, field, parameter, variable) is used, including declarations, usages, type references, and documentation. This is essential for impact analysis before refactoring, understanding symbol scope, and ensuring no references are missed when making changes.

The tool handles:
- All symbol types: types, methods, properties, fields, parameters, local variables
- Cross-file search across the entire solution
- Semantic understanding (not text matching)
- Returns declarations, usages, type constraints, XML doc references
- Filtering by project or file pattern
- Structured results with file location, line number, and preview text

## Comparison with Native Claude Code Tools

### vs Grep (text search)
- **Grep** searches for text patterns and matches strings everywhere
- **GetReferences** uses semantic symbol understanding
- **Grep** matches comments, strings, unrelated code with same text
- **GetReferences** only finds actual symbol references
- **Grep** requires manual filtering of false positives
- **GetReferences** returns pre-filtered, semantically accurate results
- **Grep** can't distinguish between different symbols with the same name
- **GetReferences** tracks the exact symbol across all usages

### vs Read + manual scanning
- **Read** requires reading every file that might contain references
- **GetReferences** automatically finds all files with references
- **Read** requires manual identification of each reference
- **GetReferences** provides structured data with exact locations
- **Read** is impractical for symbols used in many files
- **GetReferences** scales to any number of references

### When to use GetReferences
- Before renaming a symbol (see what will be affected)
- Impact analysis before refactoring
- Understanding where a type/method is used
- Finding all usages before deprecation
- Ensuring no references are missed when deleting code
- When Grep would return too many false positives
- Cross-file symbol analysis

## Real-World Example

### Scenario
Find all places where the `Priority` enum is referenced in the codebase to understand its usage before modifying it.

**Test project:** SharpOps.Examples.csproj (actual testing, not estimates)

### Approach 1: Using Native Tools (Grep)

**Step 1: Search for text matches**
```
Grep(pattern: "Priority", path: "SharpOps.Examples", output_mode: "content", -n: true)
```

**Result:** 16 matches including:
- Actual references to the enum
- Property named "Priority" (same text, different context)
- Comments or strings containing "priority"
- Variable names containing "priority"

Must manually:
- Review each match to determine if it's a reference to the enum
- Exclude property declarations that happen to have the same name
- Exclude unrelated text matches
- Determine which are type references vs usages vs declarations

**Estimated tokens:** ~424 (full grep output with all matches)

**Total: 424 tokens, 1 operation + manual filtering**

### Approach 2: Using GetReferences (Roslyn)

**Step 1: Find the symbol location**
```
FindSymbol(pattern: "Priority", symbolKind: "type")
```
**Result:** `SharpOps.Examples.Priority (Enum) SharpOps.Examples\Priority.cs:3`
**Estimated tokens:** ~55

**Step 2: Get all references**
```
GetReferences(
  filePath: "D:\\repos\\RoslynMcpServer\\SharpOps.Examples\\Priority.cs",
  line: 3,
  column: 13
)
```

**Result:**
```json
{
  "symbol": "SharpOps.Examples.Priority",
  "count": 9,
  "refs": [
    "SharpOps.Examples\\Program.cs:10 - var t1 = board.AddTask(\"Fix login bug\", Priority.Critical);",
    "SharpOps.Examples\\Program.cs:11 - var t2 = board.AddTask(\"Update README\", Priority.Low);",
    "SharpOps.Examples\\Program.cs:12 - var t3 = board.AddTask(\"Add unit tests\", Priority.High);",
    "SharpOps.Examples\\Program.cs:13 - var t4 = board.AddTask(\"Deploy to staging\", Priority.Medium);",
    "SharpOps.Examples\\Program.cs:14 - var t5 = board.AddTask(\"Review PR #42\", Priority.High);",
    "SharpOps.Examples\\Program.cs:31 - var highPriority = board.GetByPriority(Priority.High);",
    "SharpOps.Examples\\TaskBoard.cs:20 - public TaskItem AddTask(string title, Priority priority)",
    "SharpOps.Examples\\TaskBoard.cs:72 - public List<TaskItem> GetByPriority(Priority priority)",
    "SharpOps.Examples\\TaskItem.cs:16 - public Priority Priority { get; init; }"
  ]
}
```

Provides:
- Exact count: 9 references
- File and line for each reference
- Preview of the code at each location
- Only actual references (no false positives)
- Fully qualified symbol name

**Estimated tokens:** ~232

**Total: 287 tokens (55 + 232), 2 operations, no manual filtering**

### Comparison Summary

| Aspect | Native (Grep) | GetReferences (Roslyn) |
|--------|---------------|------------------------|
| **Token usage** | 424 | 287 |
| **Operations required** | 1 (+ manual filtering) | 2 (find symbol + get refs) |
| **Result accuracy** | Text matches (false positives) | Semantic (exact references only) |
| **Manual filtering** | Yes (review each match) | No (pre-filtered) |
| **Structured data** | No (text output) | Yes (JSON with locations) |
| **Preview text** | Yes | Yes |
| **Cross-file** | Yes | Yes |
| **False positives** | Yes (comments, strings, unrelated text) | No (semantic analysis) |

**GetReferences uses 32% fewer tokens** (287 vs 424) and provides semantically accurate results without manual filtering.

For symbols with common names or many references:
- "Value" property: Grep hundreds of matches, GetReferences exact count
- Method used in 50 files: Grep impractical to filter, GetReferences handles automatically

## How It Works

### Semantic Symbol Tracking

GetReferences uses Roslyn's semantic analysis to track symbols:

1. Takes a file position (line, column) as input
2. Uses `FindSymbolAtPositionWithToleranceAsync` to identify the symbol at that location
3. Calls `SymbolFinder.FindReferencesAsync` to find all references across the solution
4. Collects all reference locations from the results
5. Optionally filters by project or file pattern
6. Returns structured data with file paths, line numbers, and preview text

This is **semantic tracking** - GetReferences understands the symbol through the compiler's type system, not text patterns.

### What Counts as a Reference

GetReferences returns **all** references including:
- **Declarations**: Where the symbol is defined
- **Usages**: Where the symbol is used in code
- **Type references**: Where the type appears in signatures, constraints
- **XML documentation**: References in doc comments
- **Attributes**: Symbol used in attribute arguments

This is comprehensive - it finds everything related to the symbol.

### Position-Based Invocation

You must provide the exact file position where the symbol appears:

```
GetReferences(
  filePath: "D:\\path\\to\\File.cs",
  line: 42,
  column: 15
)
```

The tool identifies the symbol at that position and finds all references to it.

**Tip**: Use `FindSymbol` first to locate the symbol, then use the file/line from the result.

### Filtering

**By project:**
```
GetReferences(filePath, line, column, projectFilter: "MyApp.*")
→ Only references in projects matching "MyApp.*"
```

**By file:**
```
GetReferences(filePath, line, column, fileFilter: "*Controller.cs")
→ Only references in files matching "*Controller.cs"
```

**Both filters:**
```
GetReferences(filePath, line, column, projectFilter: "MyApp.Api", fileFilter: "Services")
→ References in MyApp.Api project, in files containing "Services"
```

Filters support wildcards (`*`) and use contains matching.

### Invocation

**Basic usage:**
```
GetReferences(
  filePath: "D:\\repos\\Project\\Calculator.cs",
  line: 13,
  column: 20
)
```

**With max results limit:**
```
GetReferences(filePath, line, column, maxResults: 50)
→ Returns first 50 references (default: 100)
```

**With project filter:**
```
GetReferences(filePath, line, column, projectFilter: "Atlas.*")
→ Only references in projects starting with "Atlas"
```

**With file filter:**
```
GetReferences(filePath, line, column, fileFilter: "Tests")
→ Only references in test files
```

### Response Format

**Success:**
```json
{
  "symbol": "SharpOps.Examples.Calculator.Add",
  "count": 15,
  "refs": [
    "Calculator.cs:13 - public int Add(int a, int b)",
    "Program.cs:10 - var result = calc.Add(5, 10);",
    "CalculatorTests.cs:20 - Assert.Equal(15, calc.Add(5, 10));"
  ]
}
```

Fields:
- `symbol`: Fully qualified name of the symbol
- `count`: Total number of references found (after filters)
- `refs`: Array of reference strings in format `"file:line - preview"`

**No symbol found:**
```json
{
  "error": "No symbol found at File.cs:42:15"
}
```

**File not found:**
```json
{
  "error": "Source file not found: D:\\path\\to\\File.cs"
}
```

### Common Workflows

**Before renaming:**
```
1. FindSymbol(pattern: "OldName")
   # Locate the symbol
2. GetReferences(filePath, line, column)
   # See all places that will be affected
3. RenameSymbol(filePath, line, column, newName: "NewName")
   # Rename (updates all references automatically)
```

**Impact analysis:**
```
1. GetReferences(filePath, line, column)
   # See where it's used
2. Review the references
3. Decide if safe to modify/delete
```

**Understanding symbol scope:**
```
1. GetMethodBody(typeName, methodName)
   # Get the method line number
2. GetReferences(filePath, line, column)
   # See where the method is called
```

**Before deprecation:**
```
1. GetReferences(filePath, line, column)
   # Find all usages
2. For each reference, update to use new API
3. DeleteMember or mark as [Obsolete]
```

**Cross-project analysis:**
```
GetReferences(filePath, line, column, projectFilter: "*.Tests")
→ See which test projects reference this symbol
```

### Integration with Other Tools

**Typical refactoring workflow:**
```
1. FindSymbol(pattern: "MethodName")
   → Locate the symbol
2. GetReferences(filePath, line, column)
   → Understand usage
3. RenameSymbol or UpdateMethod or DeleteMember
   → Make changes
```

**Before deleting:**
```
1. GetReferences(filePath, line, column)
   → Verify no references exist
2. DeleteMember(typeName, memberName)
   → Safe to delete
```

**Finding dead code:**
```
1. GetReferences(filePath, line, column)
   → Check if used
2. If count == 1 (only declaration), consider removing
```

### GetReferences vs GetCallers

**GetReferences**: Returns **all** references (declarations, usages, docs, type constraints)
- Use for: Renaming, scope analysis, finding all mentions
- Works on: Any symbol (types, fields, parameters, etc.)

**GetCallers**: Returns **only call sites** (excludes declarations, docs)
- Use for: Impact analysis, execution flow understanding
- Works on: Methods, properties, events only

GetCallers is a filtered subset of GetReferences with added context (which method calls it). Choose based on your needs:
- Renaming? Use **GetReferences** (need all occurrences)
- Impact before refactoring? Use **GetCallers** (need just call sites)

### Performance

Fast - uses Roslyn's optimized semantic index. Typical query on a 50-file solution completes in <2 seconds. Performance scales with solution size and reference count.

### When Grep is Better

Use Grep instead of GetReferences for:
- Searching string literals or comments for specific text
- Finding TODO markers or other non-symbol text
- Searching non-C# files
- Searching across files not in the solution
- Fuzzy text matching where symbol semantics don't matter

## Gotchas

**Position must be exact**: Line and column must point to the symbol. If position is slightly off (whitespace, comment), you'll get "No symbol found". Use FindSymbol first to get exact location.

**Comprehensive results**: Returns ALL references including declarations, type constraints, XML docs. If you only want call sites, use GetCallers instead.

**maxResults is a limit**: Default 100, max 10000. If a symbol has more references, they're truncated. Check `count` in response to see total found vs returned.

**Filters apply after finding**: GetReferences finds all references first, then applies filters. This means `count` reflects filtered total, not global total.

**Local symbols**: For local variables or parameters, references are scoped to the containing method. For type members, references are solution-wide.

**Generated code**: Includes references in generated files (Designer.cs, etc.). Use fileFilter to exclude if needed.

**Partial classes**: Finds references across all partial class files.

**Properties vs fields**: For auto-properties, finds references to the property, not the backing field (which is compiler-generated).

The tool handles all complexity of semantic symbol tracking, cross-file search, and reference categorization, exposing a simple file-position-based interface.
