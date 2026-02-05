# GetMethodBody

## Description

GetMethodBody retrieves the full source code of a specific method, including its implementation, XML documentation, and attributes. It uses Roslyn's semantic understanding to locate a method by name within a type and returns only that method's code, without requiring you to read the entire class file. This is essential for working with large classes where you only need to understand or modify one method.

The tool handles:
- Method lookup by name within a type
- Overload disambiguation using parameter types
- Constructor retrieval (use `.ctor` or the type name)
- Includes XML documentation comments and attributes
- Returns structured data: file location, line range, signature, and source code


## Comparison with Native Claude Code Tools

### vs Read (reading entire file)
- **Read** returns the entire file with all methods, fields, and implementation
- **GetMethodBody** returns only the specific method you need
- **Read** requires manual scanning to find the method
- **GetMethodBody** provides structured data with exact line numbers
- **Read** can be hundreds or thousands of tokens for large classes
- **GetMethodBody** returns only the method (typically 50-200 tokens)
- **Read** includes everything (all members, using statements, namespace)
- **GetMethodBody** focuses on one method with its signature and documentation

### vs Grep + Read (searching for method)
- **Grep** searches for text patterns and may match comments, strings, or wrong methods
- **GetMethodBody** semantically finds the exact method symbol
- **Grep + Read** requires two operations (find + read file)
- **GetMethodBody** is a single operation
- **Grep** can't distinguish overloads without complex patterns
- **GetMethodBody** handles overloads with `parameterTypes` parameter

### When to use GetMethodBody
- Reading a specific method's implementation before editing it
- Understanding algorithm details in one method
- Working with large classes (500+ line files)
- Preparing to use UpdateMethod to modify the method
- Finding a method's exact signature and location
- Reviewing XML documentation for a method
- When Read would return too much unnecessary code

## Real-World Example

### Scenario
Read the `AddTask` method implementation from TaskBoard to understand how it works before modifying it.

**Test project:** SharpOps.Examples.csproj (actual testing, not estimates)

### Approach 1: Using Native Tools (Read)

**Single operation:**
```
Read(file_path: "SharpOps.Examples\\TaskBoard.cs")
```

**Result:** Returns entire file (85 lines):
- All 7 methods with full implementations
- All XML documentation
- All fields and properties
- Namespace and using statements

Must manually:
- Scan through the file to find AddTask (lines 14-25)
- Extract the relevant code from the full file
- Ignore other methods (GetTask, CompleteTask, GetPendingTasks, RemoveTask, GetByPriority, GetCompletionRate)

**Estimated tokens:** ~1027 (full file content)

**Total: 1027 tokens, 1 operation**

### Approach 2: Using GetMethodBody (Roslyn)

**Single operation:**
```
GetMethodBody(typeName: "TaskBoard", methodName: "AddTask")
```

**Result:**
```json
{
  "file": "SharpOps.Examples\\TaskBoard.cs:20-25",
  "signature": "TaskItem AddTask(string title, Priority priority)",
  "code": "    /// <summary>\r\n    /// Creates a new task with auto-incremented ID, adds it to the board, and returns it.\r\n    /// </summary>\r\n    /// <param name=\"title\"></param>\r\n    /// <param name=\"priority\"></param>\r\n    /// <returns></returns>\r\n    public TaskItem AddTask(string title, Priority priority)\r\n    {\r\n        var task = new TaskItem { Id = _nextId++, Title = title, Priority = priority };\r\n        _tasks.Add(task);\r\n        return task;\r\n    }"
}
```

Provides:
- Exact location: `TaskBoard.cs:20-25`
- Full signature: `TaskItem AddTask(string title, Priority priority)`
- Complete method source including XML documentation
- Only the method you asked for (no other members)

**Estimated tokens:** ~164

**Total: 164 tokens, 1 operation**

### Comparison Summary

| Aspect | Native (Read) | GetMethodBody (Roslyn) |
|--------|---------------|------------------------|
| **Token usage** | 1027 | 164 |
| **Operations required** | 1 (+ manual scanning) | 1 |
| **Result format** | Full file source code | Structured JSON with method only |
| **Includes other methods** | Yes (all 7 methods) | No (only AddTask) |
| **Includes fields/properties** | Yes | No |
| **Manual extraction needed** | Yes (find method in file) | No (exact method returned) |
| **Line numbers** | Must count manually | Provided (20-25) |
| **Signature extraction** | Manual | Provided directly |

**GetMethodBody uses 84% fewer tokens** (164 vs 1027)

For very large classes:
- 1000-line class: Read ~12,000 tokens, GetMethodBody ~150 tokens
- Multiple overloads: Read requires manual inspection, GetMethodBody disambiguates with `parameterTypes`

## How It Works

### Semantic Method Lookup

GetMethodBody uses Roslyn's compilation to locate methods semantically:

1. Loads the solution
2. Uses `SymbolFinder.FindSourceDeclarationsAsync` to locate the type by name
3. Gets all methods with the specified name from the type symbol
4. If multiple overloads exist, matches by `parameterTypes` parameter
5. Retrieves the syntax node for the method declaration
6. Extracts the full source text including leading trivia (XML docs, attributes)
7. Returns structured data with file location, line range, signature, and code

This is **semantic lookup** - GetMethodBody understands C# structure through the compiler, not text patterns.

### Overload Handling

When a method has multiple overloads, GetMethodBody requires disambiguation:

**Single method:**
```
GetMethodBody(typeName: "Calculator", methodName: "Add")
→ Returns the method if only one exists
```

**Multiple overloads without parameterTypes:**
```
GetMethodBody(typeName: "Calculator", methodName: "Add")
→ Error: "Multiple overloads found for 'Add'. Specify parameterTypes to select one."
→ Returns: availableOverloads: ["int Add(int a, int b)", "double Add(double a, double b)"]
```

**Disambiguate with parameterTypes:**
```
GetMethodBody(typeName: "Calculator", methodName: "Add", parameterTypes: "int, int")
→ Returns the int Add(int, int) overload
```

The `parameterTypes` parameter accepts:
- Short form: `"int, int"`
- Full form: `"System.Int32, System.Int32"`
- Custom types: `"string, Priority"`
- Generic types: `"List<int>, bool"`

### Constructor Retrieval

To get a constructor, use either:
- The type name: `GetMethodBody(typeName: "TaskBoard", methodName: "TaskBoard")`
- The special name `.ctor`: `GetMethodBody(typeName: "TaskBoard", methodName: ".ctor")`

For overloaded constructors, use `parameterTypes`:
```
GetMethodBody(typeName: "MyClass", methodName: ".ctor", parameterTypes: "string, int")
```

### Invocation

**Basic usage:**
```
GetMethodBody(typeName: "Calculator", methodName: "Add")
```

**With overload disambiguation:**
```
GetMethodBody(typeName: "Calculator", methodName: "Add", parameterTypes: "int, int")
```

**Constructor:**
```
GetMethodBody(typeName: "TaskBoard", methodName: ".ctor")
```

**Property getter/setter:**
Properties are not methods, so use Read or GetTypeMembers to see property declarations.

### Response Format

**Success:**
```json
{
  "file": "SharpOps.Examples\\Calculator.cs:13-17",
  "signature": "int Add(int a, int b)",
  "code": "    public int Add(int a, int b)\r\n    {\r\n        return a + b;\r\n    }"
}
```

Fields:
- `file`: Relative path with line range (`startLine-endLine`)
- `signature`: Method signature with return type and parameters
- `code`: Full method source including XML docs and attributes

**Method not found:**
```json
{
  "error": "Method 'DoSomething' not found in type 'MyClass'"
}
```

**Multiple overloads (disambiguation needed):**
```json
{
  "error": "Multiple overloads found for 'Add'. Specify parameterTypes to select one.",
  "availableOverloads": [
    "int Add(int a, int b)",
    "double Add(double a, double b)",
    "int Add(int a, int b, int c)"
  ]
}
```

**No matching overload:**
```json
{
  "error": "No overload matches parameter types 'string, bool'",
  "availableOverloads": [
    "int Add(int a, int b)",
    "double Add(double a, double b)"
  ]
}
```

### Common Workflows

**Read before editing:**
```
1. GetTypeMembers(typeName: "UserService")
   # See what methods exist
2. GetMethodBody(typeName: "UserService", methodName: "GetUser")
   # Read the method
3. UpdateMethod(typeName: "UserService", methodName: "GetUser", newSourceCode: "...")
   # Edit it
```

**Understand algorithm:**
```
1. FindSymbol(pattern: "CalculateDiscount")
   # Locate the method
2. GetMethodBody(typeName: "PricingService", methodName: "CalculateDiscount")
   # Read implementation
```

**Review overloads:**
```
1. GetMethodBody(typeName: "Calculator", methodName: "Add")
   # Error: multiple overloads
2. Response shows availableOverloads
3. GetMethodBody(typeName: "Calculator", methodName: "Add", parameterTypes: "int, int")
   # Get specific overload
```

**Extract method refactoring:**
```
1. GetMethodBody(typeName: "LargeClass", methodName: "ProcessData")
   # Read the method
2. ExtractMethod(filePath, startLine, endLine, methodName: "ValidateInput")
   # Extract part of it
```

**Find and read constructor:**
```
GetMethodBody(typeName: "DatabaseContext", methodName: ".ctor")
```

### Integration with Other Tools

**Typical modification workflow:**
```
1. GetMethodBody(typeName, methodName)
   → Read current implementation
2. UpdateMethod(typeName, methodName, newSourceCode)
   → Modify it
3. GetDiagnostics()
   → Verify no errors
```

**Impact analysis before changes:**
```
1. GetMethodBody(typeName, methodName)
   → Understand what it does
2. GetCallers(filePath, line, column)
   → See who calls it
3. UpdateMethod or DeleteMember
   → Proceed with changes
```

### Line Range Information

The `file` field includes exact line numbers:
```
"file": "Services\\OrderService.cs:145-178"
```

This tells you:
- File path: `Services\OrderService.cs`
- Method starts at line 145
- Method ends at line 178
- Total: 34 lines

Use this for:
- Navigation (jump to line 145)
- Understanding method size
- Passing to other tools (GetCallers, RenameSymbol need line numbers)

### Source Code Format

The returned `code` includes:
- XML documentation comments (if present)
- Attributes (if present)
- Method signature
- Method body
- Preserves original formatting and indentation

Example:
```csharp
    /// <summary>
    /// Adds two integers
    /// </summary>
    [Obsolete("Use AddNumbers instead")]
    public int Add(int a, int b)
    {
        return a + b;
    }
```

All trivia (whitespace, comments, attributes) is preserved exactly as written.

### Performance

Very fast - uses Roslyn's semantic index. Typical query on a 50-method class completes in <100ms. Performance is independent of class size - retrieving one method from a 1000-line class takes the same time as from a 100-line class.

### When Read is Better

Use Read instead of GetMethodBody for:
- Reading multiple methods from the same class (Read once, scan for all)
- Understanding file structure and organization
- Reviewing using statements and namespaces
- Seeing all members in declaration order
- Files with syntax errors (GetMethodBody requires valid C# to parse)
- Non-C# files

## Gotchas

**typeName is short name**: Use `"Calculator"` not `"SharpOps.Examples.Calculator"`. Roslyn searches across the solution.

**Overloads require parameterTypes**: If multiple overloads exist and you don't specify `parameterTypes`, you get an error with the list of available overloads. Use the error response to determine the correct `parameterTypes` value.

**parameterTypes matching is flexible**: Accepts both short form (`"int, string"`) and full form (`"System.Int32, System.String"`). Case-insensitive matching.

**Constructors**: Use `.ctor` or the type name as `methodName`. For overloaded constructors, specify `parameterTypes`.

**Properties are not methods**: GetMethodBody retrieves methods, not properties. To see properties, use GetTypeMembers or Read.

**Extension methods**: Extension methods appear on the static class where they're declared, not on the extended type. Use GetMethodBody on the static class.

**Partial methods**: If a method is split across partial class files, GetMethodBody returns the implementation part (not the declaration-only part).

**Auto-properties**: For auto-properties like `public int Count { get; set; }`, there's no method body to retrieve. Use GetTypeMembers or Read instead.

**Generic methods**: For generic methods, omit the type parameters from methodName. Use `"Process"` not `"Process<T>"`. The signature in the response will show the type parameters.

The tool handles all complexity of semantic method lookup, overload disambiguation, and syntax node extraction, exposing a simple type-name + method-name interface.
