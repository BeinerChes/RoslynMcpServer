# GetCallers

## Description

GetCallers finds all call sites of a method or property by name. Unlike GetReferences (which returns all references including declarations, documentation, and type mentions), GetCallers returns only actual invocations — the places where the code actually calls or accesses the symbol. This makes it essential for understanding execution flow and impact analysis before refactoring.

The tool handles:
- Methods, properties, and events
- Smart name resolution (supports `MethodName`, `Type.Method`, or `Namespace.Type.Method`)
- Cross-project search
- Filtering by project or file patterns
- Pagination for large result sets
- Graph DB caching for fast results (with automatic fallback to live Roslyn)

## Comparison with Native Claude Code Tools

### vs Grep
- **Grep** matches text patterns including declarations, comments, documentation, and strings
- **GetCallers** uses semantic analysis to return only actual call sites
- **Grep** requires manual filtering to distinguish calls from other mentions
- **GetCallers** automatically excludes declarations, docs, and non-code references
- **Grep** doesn't understand overloads (can't distinguish `Add(int, int)` from `Add(double, double)`)
- **GetCallers** resolves to the specific symbol semantically

### When to use GetCallers
- Impact analysis before refactoring a method
- Understanding execution flow ("who calls this?")
- Finding usage patterns of an API
- Identifying dead code (methods with 0 callers)
- Planning changes that affect callers

## Real-World Example

### Scenario
Find all places that call `TaskBoard.CompleteTask` to understand how task completion flows through the code.

**Test project:** SharpOps.Examples.csproj (actual testing, not estimates)

### Approach 1: Using Native Grep

**Step 1: Grep for the method name**
```
Grep(pattern: "CompleteTask", glob: "**/*.cs", output_mode: "content", -n: true)
```
**Result:**
```
SharpOps.Examples\TaskBoard.cs:40:    public bool CompleteTask(int id)
SharpOps.Examples\Program.cs:24:board.CompleteTask(t1.Id);
SharpOps.Examples\Program.cs:25:board.CompleteTask(t3.Id);
```
**Tokens:** ~500 input, ~200 output = ~700 total

**Step 2: Manual filtering**
Must analyze results:
- Line 40 is the declaration (not a call site)
- Lines 24-25 are actual calls

**Total: ~700 tokens, 1 operation + manual analysis**

**Limitations:**
- Includes declaration in results
- Grep matches appear in comments, strings, documentation
- No understanding of which `CompleteTask` (if multiple types have same method name)
- For common names (like `Update`, `Execute`), grep returns hundreds of false positives

### Approach 2: Using GetCallers (Roslyn)

**Step 1: Get callers**
```
GetCallers(symbolName: "TaskBoard.CompleteTask")
```
**Result:**
```json
{
  "symbol": "TaskBoard.CompleteTask(Int32)",
  "count": 2,
  "callers": [
    "Program.<Main>$ SharpOps.Examples\\Program.cs:24",
    "Program.<Main>$ SharpOps.Examples\\Program.cs:25"
  ]
}
```
**Tokens:** ~50 input, ~100 output = ~150 total

**Total: ~150 tokens, 1 operation**

### Comparison Summary

| Aspect | Native Grep | GetCallers (Roslyn) |
|--------|-------------|---------------------|
| **Token usage** | ~700 | ~150 |
| **Operations** | 1 + manual filtering | 1 |
| **Includes declaration** | Yes (must filter) | No |
| **Includes comments/docs** | Yes (must filter) | No |
| **Semantic understanding** | No (text match) | Yes (compiler-aware) |
| **Overload disambiguation** | No | Yes (automatic) |
| **Shows calling method** | No | Yes (Program.<Main>$) |
| **False positives** | Common (strings, docs) | None |

**GetCallers uses 79% fewer tokens** (150 vs 700)

For methods with common names:
- `Update`: Grep returns 100+ matches (fields, comments, other types), GetCallers returns exact call sites
- `Execute`: Grep matches every type with `Execute`, GetCallers finds calls to specific method

On larger codebases (50+ files):
- Grep: 5000+ tokens to search + manual filtering of 50+ matches
- GetCallers: ~150 tokens, precise results immediately

## How It Works

### Symbol Resolution

GetCallers accepts flexible symbol name formats:
- **Short name:** `"CompleteTask"` → finds in any type
- **Type.Method:** `"TaskBoard.CompleteTask"` → precise match
- **Namespace.Type.Method:** `"SharpOps.Examples.TaskBoard.CompleteTask"` → fully qualified

The tool:
1. Parses the symbol name
2. Uses `SymbolFinder.FindDeclarationsAsync` to locate matching symbols across all projects
3. Filters to callable symbols (methods, properties, events)
4. Matches against the provided name for precision

### Call Site Discovery

Using Roslyn's `SymbolFinder.FindCallersAsync`, the tool finds actual call sites:
- Method invocations
- Property accesses (get/set)
- Event subscriptions

**Excluded** (unlike GetReferences):
- Symbol declaration
- XML documentation mentions
- Comments
- String literals
- Type references
- Parameter type constraints

### Graph Database Acceleration

The tool uses a two-tier approach:

**Tier 1: Graph cache (fast)**
- Pre-built graph of all call edges in the solution
- Instant lookup for "who calls this symbol?"
- Automatic staleness detection and refresh
- Source: `"graph"` or `"graph+refresh"`

**Tier 2: Live Roslyn (accurate)**
- Falls back when graph unavailable or returns 0 callers
- Always accurate, never stale
- Compiles solution on-demand
- Source: `"live"` or `"live+graph-miss"`

The fallback ensures correctness even with stale graphs or newly added callers.

### Invocation

**Basic usage:**
```
GetCallers(symbolName: "AddTask")
```

**Type-qualified:**
```
GetCallers(symbolName: "TaskBoard.AddTask")
```

**With project filter:**
```
GetCallers(
  symbolName: "Save",
  projectFilter: "*.Core"
)
```

**With file filter:**
```
GetCallers(
  symbolName: "LogError",
  fileFilter: "*Service.cs"
)
```

**Pagination:**
```
GetCallers(
  symbolName: "ProcessData",
  maxResults: 50,
  offset: 100
)
```

### Response Format

**Success:**
```json
{
  "symbol": "TaskBoard.CompleteTask(Int32)",
  "count": 2,
  "callers": [
    "Program.<Main>$ SharpOps.Examples\\Program.cs:24",
    "Program.<Main>$ SharpOps.Examples\\Program.cs:25"
  ]
}
```

**Empty (0 callers):**
```json
{
  "symbol": "TaskBoard.Clear()",
  "count": 0,
  "callers": []
}
```

**Symbol not found:**
```json
{
  "symbol": "NonExistentMethod",
  "count": 0,
  "callers": []
}
```

Note: "Not found" returns empty list, not an error, to avoid breaking parallel MCP tool calls.

### Common Workflows

**Impact analysis before refactoring:**
```
1. GetCallers(symbolName: "OldMethod")  # See who calls it
2. UpdateMethod(...)                    # Make changes
3. GetCallers(symbolName: "OldMethod")  # Verify callers still work
```

**Finding dead code:**
```
1. GetCallers(symbolName: "UnusedMethod")
   # Result: count: 0
2. DeleteMember(typeName: "MyClass", memberName: "UnusedMethod")
```

**Understanding execution flow:**
```
1. GetCallers(symbolName: "ProcessOrder")
   # See entry points
2. For each caller:
   GetCallers(symbolName: "CallerMethod")
   # Trace call chain upstream
```

**Comparing two implementations:**
```
1. GetCallers(symbolName: "OldImplementation")  # Who still uses old way?
2. GetCallers(symbolName: "NewImplementation")  # Who uses new way?
```

**Pre-deletion safety check:**
```
1. GetCallers(symbolName: "MethodToDelete")
   # If count > 0, review callers first
   # If count = 0, safe to delete
2. DeleteMember(...)
```

### Integration with Other Tools

**GetCallers + GetReferences:**
```
GetCallers(symbolName: "SaveData")      # Only calls
GetReferences(filePath, line, column)   # All references (declarations, docs, etc.)
```

**GetCallers + GraphImpact:**
```
GetCallers(symbolName: "CoreMethod")    # Direct callers
GraphImpact(symbolName: "CoreMethod")   # Transitive callers (full impact tree)
```

**GetCallers + FindDeadCode:**
```
FindDeadCode()                          # Find methods with 0 callers
# Verify with:
GetCallers(symbolName: "SuspectedDeadMethod")
```

### When Grep is Better

Grep is still useful for:
- **Non-C# files** (JavaScript, JSON, config files)
- **Finding all mentions** including comments and documentation
- **String content** ("find where we log 'error'")
- **Pattern matching** (regex searches across multiple symbols)
- **Non-code references** (method name in markdown docs)

GetCallers is strictly for semantic call-site analysis in C# code.

## Performance Notes

- **Graph path:** <100ms for most queries (instant lookup in pre-built graph)
- **Live path:** 2-5 seconds (compiles solution, runs semantic analysis)
- **First call:** May be slower (graph build or solution load)
- **Subsequent calls:** Fast (cached compilation)

The graph cache makes repeated queries nearly instant, essential for interactive refactoring workflows.
