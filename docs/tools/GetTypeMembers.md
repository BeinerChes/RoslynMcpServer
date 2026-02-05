# GetTypeMembers

## Description

GetTypeMembers retrieves all members of a type (class, interface, struct) including methods, properties, fields, events, and constructors. It uses Roslyn's semantic understanding to provide structured information about a type's API surface without requiring you to read the entire file. The tool returns members grouped by kind with their signatures, making it easy to understand a class's structure at a glance.

The tool handles:
- All member kinds: methods, properties, fields, events, constructors
- Filtering by member kind (show only methods, only properties, etc.)
- Optional inclusion of inherited members from base classes
- Structured output grouped by member type
- Full signatures with parameter types and return types
- Sorted output (constructors first, then by kind, then alphabetically)

## Comparison with Native Claude Code Tools

### vs Read (reading entire file)
- **Read** returns the entire file contents with all implementation details
- **GetTypeMembers** returns only member signatures (API surface)
- **Read** includes comments, regions, implementation code
- **GetTypeMembers** provides structured data: grouped by kind, sorted, signature-only
- **Read** requires manual parsing to extract member information
- **GetTypeMembers** gives you ready-to-use structured data
- **Read** can be hundreds of lines for large classes
- **GetTypeMembers** gives compact overview regardless of class size

### vs Grep (searching for method patterns)
- **Grep** requires pattern matching for "public", "private", method names
- **GetTypeMembers** semantically understands all members
- **Grep** matches text everywhere (comments, strings, etc.)
- **GetTypeMembers** only returns actual member declarations
- **Grep** returns raw text without structure
- **GetTypeMembers** returns grouped, sorted, typed member information
- **Grep** can't distinguish methods from properties or fields without complex patterns
- **GetTypeMembers** categorizes members automatically

### When to use GetTypeMembers
- Understanding a class's structure before modifying it
- Finding what methods/properties a type exposes
- Discovering available members without reading implementation
- Getting an API overview of unfamiliar code
- Before using other tools (GetMethodBody, UpdateMethod, DeleteMember)
- When Read would return too much detail (large classes with complex implementations)

## Real-World Example

### Scenario
Get an overview of the TaskBoard class structure to understand what it does before modifying it.

**Test project:** SharpOps.Examples.csproj (actual testing, not estimates)

### Approach 1: Using Native Tools (Read)

**Single operation:**
```
Read(file_path: "SharpOps.Examples\\TaskBoard.cs")
```

**Result:** Returns entire file (85 lines):
- Namespace declaration
- All fields with initializers
- All XML documentation comments
- All method implementations with full code
- All using statements

Must manually parse to extract:
- What members exist
- Member signatures
- Member types (method vs property vs field)

**Estimated tokens:** ~1027 (full file content)

**Total: 1027 tokens, 1 operation**

You must manually scan the file to answer:
- "What methods does TaskBoard have?" → scan lines 20-84
- "Are there any properties?" → scan to find line 13
- "What fields exist?" → scan to find lines 6-8
- "What are the method signatures?" → extract from XML docs + declarations

### Approach 2: Using GetTypeMembers (Roslyn)

**Single operation:**
```
GetTypeMembers(typeName: "TaskBoard")
```

**Result:**
```json
{
  "type": "SharpOps.Examples.TaskBoard",
  "count": 10,
  "fields": [
    "int _nextId",
    "List<TaskItem> _tasks"
  ],
  "properties": [
    "int Count"
  ],
  "methods": [
    "TaskItem AddTask(string title, Priority priority)",
    "bool CompleteTask(int id)",
    "List<TaskItem> GetByPriority(Priority priority)",
    "double GetCompletionRate()",
    "List<TaskItem> GetPendingTasks()",
    "TaskItem? GetTask(int id)",
    "bool RemoveTask(int id)"
  ]
}
```

Provides:
- Fully qualified type name
- Total member count
- Members grouped by kind (fields, properties, methods)
- Full signatures with parameter types and return types
- Sorted (constructors first, then alphabetically by name)

**Estimated tokens:** ~114

**Total: 114 tokens, 1 operation**

Immediately answers:
- "What methods does TaskBoard have?" → 7 methods listed
- "Are there any properties?" → Yes, `Count` property
- "What fields exist?" → `_nextId` and `_tasks`
- "What are the method signatures?" → All shown with full types

### Comparison Summary

| Aspect | Native (Read) | GetTypeMembers (Roslyn) |
|--------|--------------|-------------------------|
| **Token usage** | 1027 | 114 |
| **Operations required** | 1 | 1 |
| **Result format** | Unstructured source code | Structured JSON by member kind |
| **Includes implementation** | Yes (all code) | No (signatures only) |
| **Includes documentation** | Yes (XML comments, inline comments) | No (API surface only) |
| **Manual parsing needed** | Yes | No |
| **Grouped by kind** | No (source order) | Yes (fields/properties/methods/etc.) |
| **Sorted** | No (source order) | Yes (constructors first, then alphabetical) |
| **Full signatures** | Must extract from code | Provided directly |

**GetTypeMembers uses 89% fewer tokens** (114 vs 1027)

For large classes with complex implementations:
- 500-line class: Read ~6000 tokens, GetTypeMembers ~200 tokens
- Multiple inheritance levels: Read requires reading multiple files, GetTypeMembers can include inherited members with `includeInherited: true`

## How It Works

### Semantic Type Analysis

GetTypeMembers uses Roslyn's compilation to access the semantic model of a type:

1. Loads the solution
2. Uses `SymbolFinder.FindSourceDeclarationsAsync` to locate the type by name
3. Gets the `INamedTypeSymbol` for the type
4. Calls `GetMembers()` on the type symbol
5. Filters members based on `memberKind` parameter
6. Optionally traverses base classes and interfaces if `includeInherited: true`
7. Returns structured data grouped by member kind

This is **semantic analysis** - GetTypeMembers understands C# structure through the compiler, not text patterns.

### Member Kind Filtering

**all** (default): All member types
```
GetTypeMembers(typeName: "TaskBoard")
→ Returns fields, properties, methods, events, constructors
```

**methods**: Only methods
```
GetTypeMembers(typeName: "TaskBoard", memberKind: "methods")
→ Returns: AddTask, CompleteTask, GetTask, etc. (no properties or fields)
```

**properties**: Only properties
```
GetTypeMembers(typeName: "TaskBoard", memberKind: "properties")
→ Returns: Count property only
```

**fields**: Only fields
```
GetTypeMembers(typeName: "TaskBoard", memberKind: "fields")
→ Returns: _nextId, _tasks
```

**events**: Only events
```
GetTypeMembers(typeName: "EventBus", memberKind: "events")
→ Returns only event declarations
```

**constructors**: Only constructors
```
GetTypeMembers(typeName: "TaskBoard", memberKind: "constructors")
→ Returns constructor signatures (if any explicit constructors exist)
```

### Inherited Members

By default, GetTypeMembers shows only members declared directly on the type. Use `includeInherited: true` to include base class and interface members:

```
GetTypeMembers(typeName: "DerivedClass", includeInherited: true)
```

This traverses:
- Base classes (excluding `System.Object`)
- Implemented interfaces
- Returns inherited members annotated with their source type

Note: Overridden members are deduplicated - only the most derived version is shown.

### Invocation

**Basic usage:**
```
GetTypeMembers(typeName: "Calculator")
```

**Filter by kind:**
```
GetTypeMembers(typeName: "UserService", memberKind: "methods")
→ Shows only methods
```

**Include inherited:**
```
GetTypeMembers(typeName: "DerivedService", includeInherited: true)
→ Shows members from base classes too
```

**Combine filters:**
```
GetTypeMembers(typeName: "RepositoryBase", memberKind: "properties", includeInherited: true)
→ Shows properties from this type and all base classes
```

### Response Format

**Success:**
```json
{
  "type": "SharpOps.Examples.Calculator",
  "count": 5,
  "fields": [
    "List<double> _history"
  ],
  "methods": [
    "int Add(int a, int b)",
    "void AddToHistory(double value)",
    "void Clear()",
    "int GetCount()"
  ]
}
```

Each group (fields, properties, methods, etc.) only appears if that kind has members. Empty groups are omitted for compactness.

**Type not found:**
```json
{
  "Success": false,
  "Error": "Type not found: MyClass. Try using the exact type name."
}
```

**With inherited members:**
```json
{
  "type": "MyApp.Services.DerivedService",
  "count": 12,
  "properties": [
    "string Name (from BaseService)",
    "int Id"
  ],
  "methods": [
    "void Process() (from BaseService)",
    "void Execute()"
  ]
}
```

Inherited members are annotated with `(from ClassName)`.

### Common Workflows

**Explore type before editing:**
```
1. FindSymbol(pattern: "UserRepository")
   # Locate the type
2. GetTypeMembers(typeName: "UserRepository")
   # See what members exist
3. GetMethodBody(typeName: "UserRepository", methodName: "GetUser")
   # Read specific method
4. UpdateMethod(typeName: "UserRepository", methodName: "GetUser", ...)
   # Edit the method
```

**Find what to delete:**
```
1. GetTypeMembers(typeName: "LegacyService")
   # See all members
2. GetCallers(filePath, line, column)
   # Check if method is used
3. DeleteMember(typeName: "LegacyService", memberName: "OldMethod")
   # Remove if unused
```

**Understand inheritance hierarchy:**
```
1. GetTypeMembers(typeName: "Controller", includeInherited: true)
   # See what the base classes provide
2. Grep(pattern: ": IController", glob: "*.cs")
   # Find all implementations
```

**Find methods to modify:**
```
1. GetTypeMembers(typeName: "OrderService", memberKind: "methods")
   # List all methods
2. GetMethodBody for each method of interest
3. UpdateMethod to make changes
```

**Discover API surface:**
```
GetTypeMembers(typeName: "PublicApi", memberKind: "methods")
→ Shows all public methods exposed by the type
```

### Integration with Other Tools

**Typical exploration workflow:**
```
1. FindSymbol(pattern: "ServiceName")
   → Locate the type
2. GetTypeMembers(typeName: "ServiceName")
   → See structure
3. GetMethodBody(typeName: "ServiceName", methodName: "MethodName")
   → Read implementation
```

**Before refactoring:**
```
1. GetTypeMembers(typeName: "OldClass")
   → See what members exist
2. For each member:
   - GetReferences or GetCallers to assess impact
   - RenameSymbol or UpdateMethod to refactor
```

**Finding overloads:**
```
GetTypeMembers(typeName: "Calculator", memberKind: "methods")
→ Shows all method overloads with full signatures
```

### Member Signature Format

Results use this format:
```
<ReturnType> <MemberName>(<Parameters>)
```

Examples:
```
int Add(int a, int b)                           # Method
List<TaskItem> GetByPriority(Priority priority) # Method with generic return
TaskItem? GetTask(int id)                       # Nullable return type
int Count                                       # Property
List<TaskItem> _tasks                           # Field
```

Constructors are shown without return types:
```
TaskBoard()                      # Default constructor
TaskBoard(int capacity)          # Constructor with parameters
```

### Performance

Very fast - reads type metadata from Roslyn's semantic model. Typical query on a 50-member class completes in <100ms. Performance is independent of implementation complexity - a 1000-line method takes the same time as a 1-line method since only signatures are analyzed.

### When Read is Better

Use Read instead of GetTypeMembers for:
- Reading implementation code
- Understanding algorithm details
- Seeing XML documentation comments
- Reviewing inline comments
- Code that isn't structured as C# symbols (comments, regions, disabled code)
- Files with syntax errors (GetTypeMembers requires valid C# to parse)

## Gotchas

**typeName is short name**: Use `"Calculator"` not `"SharpOps.Examples.Calculator"`. Roslyn searches across the solution. If multiple types have the same name, the first match is used.

**No constructors shown for implicit constructor**: If a class has no explicit constructors, the `constructors` group won't appear in the output. C# provides a default parameterless constructor implicitly.

**Inherited members require opt-in**: By default, only members declared directly on the type are shown. Use `includeInherited: true` to see base class and interface members.

**Private members are included**: All members are shown regardless of accessibility (public, private, protected, internal). Use the returned data to filter by visibility if needed.

**Properties vs fields**: Auto-properties like `public int Count { get; set; }` are shown as properties, not fields. The backing field (if auto-generated) is not visible.

**Generic types**: For generic types like `List<T>`, signatures show the type parameter: `void Add(T item)`.

**Extension methods**: Extension methods are not shown on the extended type - they appear on the static class where they're declared.

The tool handles all complexity of semantic analysis, inheritance traversal, and member categorization, exposing a simple type-name-based interface.
