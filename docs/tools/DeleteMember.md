# DeleteMember

## Description

DeleteMember removes a member (method, property, or field) from a type using Roslyn's semantic understanding. It precisely locates the target member, removes it along with all associated attributes and XML documentation comments, and preserves the surrounding code structure and formatting.

The tool handles:
- Semantic member lookup (finds by name, not text search)
- Automatic handling of overloaded methods (with `parameterTypes` disambiguation)
- Complete removal including attributes and XML docs
- Proper whitespace cleanup after deletion
- Preservation of surrounding members and formatting

## Comparison with Native Claude Code Tools

### vs Edit tool
- **Edit** requires reading the entire file, finding the exact text of the member including indentation, attributes, and XML docs, then crafting a precise match string
- **DeleteMember** locates the member semantically by name, removes it completely including all decorations
- **Edit** fails if whitespace/indentation differs from your match string
- **DeleteMember** uses semantic understanding (finds by symbol, not text)
- **Edit** requires you to manually identify where the member starts and ends (attributes? XML docs? decorators?)
- **DeleteMember** automatically identifies and removes all associated trivia

### vs Write tool
- **Write** requires reading the entire file, manually removing the member lines, then writing back the complete modified content
- **DeleteMember** performs surgical removal without reading/writing the entire file
- **Write** risks syntax errors if you miss a brace or don't handle whitespace correctly
- **DeleteMember** validates syntax and handles formatting automatically

### When to use DeleteMember
- Removing dead code identified by `FindDeadCode`
- Cleaning up deprecated methods/properties
- Refactoring classes to remove unused members
- Working with large files where reading the entire file wastes tokens
- Handling overloaded methods where precise selection is needed

## Real-World Example

### Scenario
Remove the `Clear` method from the `Calculator` class.

**Current code:**
```csharp
public class Calculator
{
    private List<double> _history = new();

    public int Add(int a, int b) { ... }

    public void AddToHistory(double value) { ... }

    public void Clear()
    {
        _history.Clear();
    }

    public int GetCount() { ... }
}
```

**Target:** Remove the `Clear` method entirely.

### Approach 1: Using Native Edit Tool

**Step 1: Find the file**
```
Glob(pattern: "**/*Calculator.cs")
```
**Result:** `SharpOps.Examples\Calculator.cs`
**Tokens:** ~50

**Step 2: Read the entire file**
```
Read(file_path: "D:\repos\RoslynMcpServer\SharpOps.Examples\Calculator.cs")
```
**Result:** Full file content (30+ lines, ~400 tokens)

**Step 3: Identify exact member text**
Must find the exact text including:
- Any XML documentation
- Any attributes
- Method signature
- Body
- Proper indentation

**Step 4: Craft exact match string**
```
Edit(
  file_path: "D:\repos\RoslynMcpServer\SharpOps.Examples\Calculator.cs",
  old_string: "    public void Clear()
    {
        _history.Clear();
    }",
  new_string: ""
)
```
**Tokens:** ~120

**Total: ~570 tokens, 3 steps**

**Risks:**
- Must preserve exact indentation (spaces vs tabs)
- May leave extra blank lines after deletion
- If method has XML docs or attributes, must include them in old_string
- Easy to miss associated trivia (comments, regions, etc.)
- Must manually handle whitespace cleanup

### Approach 2: Using DeleteMember (Roslyn)

**Step 1: Delete the member**
```
DeleteMember(
  typeName: "Calculator",
  memberName: "Clear"
)
```
**Total: ~40 tokens, 1 step**

**Benefits:**
- No file path needed
- No need to read entire file
- Automatic removal of XML docs and attributes
- Proper whitespace cleanup
- Works even if file has 1000+ lines

### Handling Overloaded Methods

**Scenario:** Delete one specific overload of `Add`:

```csharp
public int Add(int a, int b) { ... }
public double Add(double a, double b) { ... }
public int Add(int a, int b, int c) { ... }
```

**Native Edit:** Must read file, identify exact overload by full signature text, craft exact match including spacing

**DeleteMember:**
```
DeleteMember(
  typeName: "Calculator",
  memberName: "Add",
  parameterTypes: "double, double"
)
```

The tool disambiguates automatically using parameter types.

### Comparison Summary

| Aspect | Native Edit | DeleteMember (Roslyn) |
|--------|-------------|----------------------|
| **Token usage** | ~570 | ~40 |
| **Steps required** | 3 (Glob → Read → Edit) | 1 |
| **File path needed** | Yes | No |
| **Read entire file** | Yes | No |
| **Exact text match** | Required | Not required |
| **Handles XML docs** | Manual (include in old_string) | Automatic |
| **Handles attributes** | Manual (include in old_string) | Automatic |
| **Whitespace cleanup** | Manual | Automatic |
| **Overload disambiguation** | Manual (craft unique match) | Automatic (parameterTypes) |
| **Error risk** | High | Low |

**DeleteMember uses 93% fewer tokens** (40 vs 570)

On large files (500+ lines):
- Edit: ~5000+ tokens (read entire file + craft match)
- DeleteMember: ~40 tokens (just type + member name)

### When Edit is Better
- Non-C# files (JSON, XML, config)
- Removing non-code content (comments, regions, etc. that aren't members)
- Need to remove multiple members simultaneously
- Working with malformed code where Roslyn can't parse

## How It Works

### Member Discovery
The tool takes a type name and member name, uses Roslyn to load the solution and find the type by semantic search. Within the type, it searches for members with the matching name.

If multiple members match (overloaded methods, or method/property with same name):
1. If `memberKind` is provided ("method", "property", "field"), it filters by kind
2. If `parameterTypes` is provided for methods, it matches the signature
3. If still ambiguous, returns an error with available options

### Complete Removal
Once the member is located, the tool removes:
1. **The member node itself** (signature + body)
2. **XML documentation comments** (/// <summary>, <param>, <returns>, etc.)
3. **Attributes** ([Obsolete], [JsonProperty], custom attributes, etc.)
4. **Leading trivia** (blank lines, comments immediately before the member)
5. **Trailing trivia** (cleanup extra whitespace)

The removal is performed as a syntax tree node deletion, preserving all other nodes in the tree.

### Disambiguation Strategies

**Single member with that name:**
```
DeleteMember(typeName: "Calculator", memberName: "Clear")
```
Simple - just delete it.

**Multiple kinds (method and property with same name):**
```
DeleteMember(
  typeName: "Calculator",
  memberName: "Count",
  memberKind: "property"  // vs "method"
)
```

**Overloaded methods:**
```
DeleteMember(
  typeName: "Calculator",
  memberName: "Add",
  parameterTypes: "int, int, int"  // Matches 3-param overload
)
```

**Combination:**
```
DeleteMember(
  typeName: "Calculator",
  memberName: "GetValue",
  memberKind: "method",
  parameterTypes: "string"
)
```

### Integration with FindDeadCode

Common workflow for cleaning up unused code:

**Step 1: Find dead code**
```
FindDeadCode(includePrivate: true, maxResults: 100)
```
**Result:**
```json
[
  {"symbol": "Calculator.OldMethod", "callers": 0},
  {"symbol": "Calculator.UnusedProperty", "callers": 0}
]
```

**Step 2: Delete each one**
```
DeleteMember(typeName: "Calculator", memberName: "OldMethod")
DeleteMember(typeName: "Calculator", memberName: "UnusedProperty")
```

This workflow is essential for maintaining clean codebases and removing technical debt.

### Error Handling

**Member not found:**
```
Error: Member 'Foo' not found in type 'Calculator'
```

**Ambiguous (multiple matches):**
```
Error: Multiple members found named 'Add'. Specify parameterTypes:
- Add(int, int)
- Add(double, double)
- Add(int, int, int)
```

**Type not found:**
```
Error: Type not found: Calculator
```

### Common Workflows

**Delete a simple method:**
```
DeleteMember(typeName: "UserService", memberName: "GetAll")
```

**Delete a specific overload:**
```
DeleteMember(
  typeName: "UserService",
  memberName: "GetById",
  parameterTypes: "string"
)
```

**Delete a property:**
```
DeleteMember(
  typeName: "User",
  memberName: "Age",
  memberKind: "property"
)
```

**Delete a field:**
```
DeleteMember(
  typeName: "Calculator",
  memberName: "_tempValue",
  memberKind: "field"
)
```

**Bulk cleanup after FindDeadCode:**
```
1. FindDeadCode() → get list
2. For each dead symbol:
   DeleteMember(typeName, memberName)
```

The tool handles all complexity of semantic member lookup, complete trivia removal, disambiguation, and file I/O, exposing a simple name-based interface.
