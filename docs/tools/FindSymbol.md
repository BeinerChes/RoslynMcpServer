# FindSymbol

## Description

FindSymbol searches for symbols (types, methods, properties, fields, namespaces) in a .NET solution by name pattern. It uses Roslyn's semantic search to find symbols across the entire solution and returns fully qualified names, file locations, signatures, and symbol kinds. When no results are found, it automatically falls back to fuzzy matching via the graph database to suggest similar symbol names.

The tool handles:
- Semantic symbol search (not text matching)
- Multiple match types: exact, contains, prefix, suffix, case-insensitive
- Symbol kind filtering: types, members, namespaces, or all
- Fuzzy fallback suggestions when no exact matches found
- Fully qualified names with complete type information
- File paths and line numbers for each match

## Comparison with Native Claude Code Tools

### vs Grep
- **Grep** searches for text patterns and matches strings everywhere (comments, strings, variable names)
- **FindSymbol** searches semantically and only finds actual symbol declarations
- **Grep** returns raw text matches without context
- **FindSymbol** returns fully qualified names, signatures, file locations, and symbol kinds
- **Grep** requires manual filtering of results (comments vs code vs strings)
- **FindSymbol** only returns actual symbols, pre-filtered and contextualized
- **Grep** can't distinguish between different symbols with the same name
- **FindSymbol** shows full signatures to disambiguate overloads

### vs Glob + Read
- **Glob + Read** requires finding files first, then reading each to find symbols
- **FindSymbol** searches all files automatically via Roslyn's semantic index
- **Glob + Read** requires manual parsing of code to understand symbol types
- **FindSymbol** provides structured data: fully qualified names, kinds, signatures

### When to use FindSymbol
- Finding where a type/method/property is defined
- Locating symbols before using other tools (GetReferences, RenameSymbol, etc.)
- Discovering what symbols exist in unfamiliar code
- Finding symbols when you know part of the name but not the full path
- Getting exact file locations and signatures for symbols
- When grep would return too many false positives (common names like "Add", "Get", "Value")

## Real-World Example

### Scenario
Find the `AddTask` method in the SharpOps.Examples project to understand where it's defined and what its signature is.

**Test project:** SharpOps.Examples.csproj (actual testing, not estimates)

### Approach 1: Using Native Tools (Grep)

**Step 1: Search for text matches**
```
Grep(pattern: "AddTask", path: "SharpOps.Examples", output_mode: "files_with_matches")
```
**Result:** 2 files (TaskBoard.cs, Program.cs)
**Measured tokens:** 133

**Step 2: Get line numbers and context**
```
Grep(pattern: "AddTask", path: "SharpOps.Examples", output_mode: "content", -n: true)
```
**Result:**
```
Program.cs:10: var t1 = board.AddTask("Fix login bug", Priority.Critical);
Program.cs:11: var t2 = board.AddTask("Update README", Priority.Low);
... (5 usage lines)
TaskBoard.cs:20: public TaskItem AddTask(string title, Priority priority)
```
**Measured tokens:** 308

Must manually determine which line is the definition vs usages. Result doesn't show:
- Full namespace (SharpOps.Examples.TaskBoard)
- Return type details
- Full file path with directory
- Symbol kind (method vs property vs type)

**Total: ~441 tokens, 2 operations**

### Approach 2: Using FindSymbol (Roslyn)

**Single operation:**
```
FindSymbol(pattern: "AddTask")
```

**Result:**
```json
{
  "count": 1,
  "symbols": [
    "SharpOps.Examples.TaskBoard.AddTask(string, SharpOps.Examples.Priority) (Method) SharpOps.Examples\\TaskBoard.cs:20"
  ]
}
```

Provides:
- Fully qualified name: `SharpOps.Examples.TaskBoard.AddTask`
- Complete signature: `AddTask(string, SharpOps.Examples.Priority)`
- Symbol kind: `Method`
- Exact location: `SharpOps.Examples\TaskBoard.cs:20`

**Measured tokens:** 114

**Total: 114 tokens, 1 operation**

### Comparison Summary

| Aspect | Native (Grep) | FindSymbol (Roslyn) |
|--------|---------------|---------------------|
| **Token usage** | 441 | 114 |
| **Operations required** | 2 (files + content) | 1 |
| **Result quality** | Text matches (includes usages + definitions) | Only definitions with full context |
| **Fully qualified name** | No | Yes |
| **Complete signature** | No (must read file) | Yes |
| **Symbol kind** | No | Yes (Method/Type/Property/Field) |
| **File path** | Relative only | Full path with line number |
| **False positives** | Yes (comments, strings, partial matches) | No (semantic search) |
| **Disambiguation** | Manual | Automatic (shows full signatures) |

**FindSymbol uses 74% fewer tokens** (114 vs 441)

For symbols with common names ("Get", "Value", "Data"):
- Grep: Returns 100+ matches across files, requiring extensive filtering
- FindSymbol: Returns only actual symbol declarations with full context

## How It Works

### Semantic Symbol Search

FindSymbol uses Roslyn's compilation to build a semantic index of all symbols in the solution. When you search for a pattern:

1. Loads the solution
2. For each project, gets all symbols from the semantic model
3. Filters by symbol kind (if specified): types, members, namespaces
4. Matches against the pattern using the specified match type
5. Returns symbol metadata: name, kind, location, signature

This is **semantic search** - FindSymbol understands C# structure, not just text.

### Match Types

**contains** (default): Symbol name contains the pattern
```
FindSymbol(pattern: "Task")
→ Finds: TaskBoard, AddTask, GetTask, TaskItem, etc.
```

**exact**: Exact symbol name match (case-sensitive)
```
FindSymbol(pattern: "AddTask", matchType: "exact")
→ Finds: AddTask only
```

**exactIgnoreCase**: Exact match, case-insensitive
```
FindSymbol(pattern: "addtask", matchType: "exactIgnoreCase")
→ Finds: AddTask
```

**prefix**: Symbol name starts with pattern
```
FindSymbol(pattern: "Add", matchType: "prefix")
→ Finds: AddTask, AddToHistory, but NOT GetTask
```

**suffix**: Symbol name ends with pattern
```
FindSymbol(pattern: "Task", matchType: "suffix")
→ Finds: AddTask, GetTask, but NOT TaskBoard
```

### Symbol Kind Filtering

**all** (default): All symbol types
```
FindSymbol(pattern: "Priority")
→ Finds: Priority enum, Priority parameter in methods, etc.
```

**type**: Only types (classes, interfaces, enums, structs)
```
FindSymbol(pattern: "Priority", symbolKind: "type")
→ Finds: Priority enum only
```

**member**: Only members (methods, properties, fields)
```
FindSymbol(pattern: "Add", symbolKind: "member")
→ Finds: Add method, AddTask method, etc. (no types)
```

**namespace**: Only namespaces
```
FindSymbol(pattern: "Examples", symbolKind: "namespace")
→ Finds: SharpOps.Examples namespace
```

**typeAndMember**: Types and members, excluding namespaces
```
FindSymbol(pattern: "Task")
→ Finds: TaskBoard type, AddTask method, etc.
```

### Fuzzy Fallback Feature

When no results are found, FindSymbol automatically queries the graph database for fuzzy suggestions:

```
FindSymbol(pattern: "AddTaask")  # Typo
→ No exact matches
→ Fuzzy suggestions: "AddTask", "AddToHistory"
```

This requires prior call to `GraphAnalyze()` to build the graph database.

### Invocation

**Basic search:**
```
FindSymbol(pattern: "Calculator")
```

**With filters:**
```
FindSymbol(pattern: "Add", symbolKind: "member", matchType: "prefix")
→ Finds only members (methods/properties) starting with "Add"
```

**Limit results:**
```
FindSymbol(pattern: "Get", maxResults: 10)
→ Returns first 10 matches
```

### Response Format

**Success with results:**
```json
{
  "count": 3,
  "symbols": [
    "SharpOps.Examples.Calculator.Add(int, int) (Method) SharpOps.Examples\\Calculator.cs:13",
    "SharpOps.Examples.Calculator.AddToHistory(double) (Method) SharpOps.Examples\\Calculator.cs:21",
    "SharpOps.Examples.TaskBoard.AddTask(string, Priority) (Method) SharpOps.Examples\\TaskBoard.cs:20"
  ]
}
```

Each symbol string contains:
- Fully qualified name with signature
- Symbol kind in parentheses
- File path and line number

**No results (with fuzzy suggestions):**
```json
{
  "count": 0,
  "symbols": [],
  "suggestions": ["AddTask", "AddToHistory"]
}
```

**Error:**
```json
{
  "success": false,
  "error": "Solution not found"
}
```

### Common Workflows

**Find a symbol before using other tools:**
```
1. FindSymbol(pattern: "UserService")
   # Result: SharpOps.Services.UserService (Type) Services\UserService.cs:10
2. GetTypeMembers(typeName: "UserService")  # Explore structure
3. GetMethodBody(typeName: "UserService", methodName: "GetUser")  # Read specific method
```

**Find overloaded methods:**
```
FindSymbol(pattern: "Add")
# Result shows all overloads with full signatures:
# - Calculator.Add(int, int)
# - Calculator.Add(double, double)
# - Calculator.Add(int, int, int)
```

**Search by partial name:**
```
FindSymbol(pattern: "Service", matchType: "suffix")
# Finds: UserService, AuthService, EmailService, etc.
```

**Find types only:**
```
FindSymbol(pattern: "Exception", symbolKind: "type")
# Excludes methods/properties, only type declarations
```

**Discover symbols in unfamiliar code:**
```
FindSymbol(pattern: "")  # Empty pattern with contains match
# Returns all symbols (up to maxResults limit)
# Use maxResults to sample the codebase
```

### Integration with Other Tools

**Typical workflow:**
```
1. FindSymbol(pattern: "UserRepository")
   → Locate the symbol
2. GetTypeMembers(typeName: "UserRepository")
   → See what members it has
3. GetReferences(filePath, line, column)
   → Find all usages
4. RenameSymbol(filePath, line, column, newName: "UserStore")
   → Refactor
```

**For refactoring:**
```
1. FindSymbol(pattern: "OldMethod")
   → Get location
2. GetCallers(filePath, line, column)
   → Assess impact
3. DeleteMember(typeName, memberName: "OldMethod")
   → Remove if unused
```

### Symbol Name Format

Results use this format:
```
<FullyQualifiedName>(<Parameters>) (SymbolKind) <FilePath>:<Line>
```

Examples:
```
SharpOps.Examples.Calculator.Add(int, int) (Method) SharpOps.Examples\Calculator.cs:13
SharpOps.Examples.Priority (Type) SharpOps.Examples\Priority.cs:3
SharpOps.Examples.TaskBoard.Count (Property) SharpOps.Examples\TaskBoard.cs:15
SharpOps.Examples (Namespace)
```

### Performance

Fast - searches use Roslyn's optimized semantic index. Typical search across a 100-file solution completes in <1 second.

### When Grep is Better

Use Grep instead of FindSymbol for:
- Searching for string literals in code
- Finding comments containing keywords
- Searching configuration values
- Text that isn't a C# symbol (TODO markers, URLs, etc.)
- Non-C# files

## Gotchas

**Short pattern names:** Pattern "Get" will match hundreds of symbols. Use `matchType: "exact"` or `symbolKind` filtering to narrow results.

**Fuzzy fallback requires GraphAnalyze:** Suggestions on no-match only work if you've run `GraphAnalyze()` previously to build the graph database. Otherwise, you'll get no suggestions.

**Case sensitivity:** Default `matchType: "contains"` is case-sensitive. Use `matchType: "exactIgnoreCase"` for case-insensitive search.

**Namespace search:** When searching for namespaces, result won't have a file path (namespaces aren't declared in a specific location).

**Constructor search:** To find constructors, search for the type name with `symbolKind: "member"` or use pattern `.ctor`.

The tool handles all complexity of semantic symbol indexing, signature formatting, and fuzzy matching, exposing a simple pattern-based search interface.
