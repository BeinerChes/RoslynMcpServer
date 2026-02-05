# UpdateMethod

## Description

UpdateMethod replaces a method's implementation with new source code while preserving the surrounding class structure. It uses Roslyn's semantic understanding to precisely locate the target method within a type, even in large files with multiple overloads, and performs surgical replacement without affecting other code.

The tool supports three distinct modes:
1. **Full replacement** - Replace the entire method including signature and body
2. **Edit mode** - Make targeted text replacements within the method body (token-efficient for small changes)
3. **Auto mode** - Regenerate the method body using the built-in SharpTinyCoder AI model

## Comparison with Native Claude Code Tools

### vs Edit tool
- **Edit** requires reading the entire file first, manually locating the method, and crafting exact string matches
- **UpdateMethod** locates methods by name/type semantically, handles overloads automatically, and works without knowing file paths or line numbers
- **Edit** uses text-based search (must match the exact characters including spacing)
- **UpdateMethod** uses semantic understanding (finds methods by their symbol, not their text representation)

### vs Write tool
- **Write** overwrites entire files, requiring you to read, modify, and write back the complete content
- **UpdateMethod** modifies only the target method, leaving surrounding code untouched
- **Write** loses formatting context and requires manual preservation of file structure
- **UpdateMethod** preserves indentation, spacing, and code organization automatically

### When to use UpdateMethod
- Modifying methods in classes with 100+ lines where reading the entire file wastes tokens
- Working with overloaded methods where manual disambiguation is error-prone
- Making changes across files where you don't want to track file paths
- Leveraging AI code generation for method bodies

## Real-World Example

### Scenario
Change the `Add` method in `Calculator` class to also log the operation to history.

**Current implementation:**
```csharp
public int Add(int a, int b)
{
    return a + b;
}
```

**Target implementation:**
```csharp
public int Add(int a, int b)
{
    var result = a + b;
    _history.Add(result);
    return result;
}
```

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
**Result:** Full file content (30 lines, ~400 tokens)

**Step 3: Craft exact match string**
```
Edit(
  file_path: "D:\repos\RoslynMcpServer\SharpOps.Examples\Calculator.cs",
  old_string: "    public int Add(int a, int b)
    {
        return a + b;
    }",
  new_string: "    public int Add(int a, int b)
    {
        var result = a + b;
        _history.Add(result);
        return result;
    }"
)
```
**Tokens:** ~150

**Total: ~600 tokens, 3 steps**

**Risks:**
- Must carefully preserve indentation and spacing
- If indentation differs (tabs vs spaces), match fails
- Any syntax error in old_string requires re-reading file

### Approach 2: Using UpdateMethod (Roslyn)

**Step 1: Update the method directly**
```
UpdateMethod(
  typeName: "Calculator",
  methodName: "Add",
  newSourceCode: "public int Add(int a, int b)
{
    var result = a + b;
    _history.Add(result);
    return result;
}"
)
```
**Total: ~120 tokens, 1 step**

**Benefits:**
- No file path needed (Roslyn finds it via semantic analysis)
- No need to read entire file
- Indentation/formatting handled automatically
- Works even if file has 1000+ lines

### Comparison Summary

| Aspect | Native Edit | UpdateMethod (Roslyn) |
|--------|-------------|----------------------|
| **Token usage** | ~600 | ~120 |
| **Steps required** | 3 (Glob → Read → Edit) | 1 |
| **File path needed** | Yes | No |
| **Read entire file** | Yes | No |
| **Exact string match** | Required | Not required |
| **Handles overloads** | Manual | Automatic |
| **Indentation preservation** | Manual | Automatic |
| **Works on large files** | Token-expensive | Token-efficient |

**UpdateMethod uses 80% fewer tokens** (120 vs 600) for this operation.

On large files (500+ lines):
- Edit: ~5000 tokens (read entire file)
- UpdateMethod: ~120 tokens (only method code)

### When Edit is Better
- Non-C# files (JSON, XML, markdown)
- Making changes that span multiple methods
- When you need to see surrounding context for decision making
- Simple text replacements in small files

## How It Works

### Mode 1: Full Replacement
The tool takes a type name and method name, uses Roslyn to load the solution's semantic model, and searches the type hierarchy for matching methods. If multiple overloads exist, it requires parameter types to disambiguate. Once located, it parses your new source code into a Roslyn syntax tree, validates it's a well-formed method, then performs a node replacement operation. The surrounding file structure (using statements, other methods, XML docs) remains intact.

### Mode 2: Edit Mode (oldText/newText)
Instead of providing complete method code, you specify a text fragment to find and its replacement. The tool retrieves the existing method body, performs string matching with normalized line endings, and validates the match is unique (or uses `replaceAll=true` for multiple occurrences). This mode is token-efficient for small changes like renaming a variable or fixing a single line.

### Mode 3: Auto Mode
When `auto=true` is set, the tool first retrieves the existing method to extract its signature (return type, name, parameters). It strips XML documentation comments and other trivia to get a clean signature, then passes it to HandleAutoGenerateAsync which:
1. Loads the containing type's symbol information to extract field/property context
2. Calls the SharpTinyCoder model (4.3M parameter transformer trained on C# code)
3. Generates SharpOps intermediate representation (a simplified AST format)
4. Compiles SharpOps back to C# syntax
5. Validates the generated code parses without errors

If generation fails or produces invalid syntax, a `NotImplementedException` stub is inserted. The response includes `autoGenerationFailed` and `generatedCode` fields so you can review the output. Auto mode skips finetune data collection since model output shouldn't become training data. When you correct wrong auto-generated code with a non-auto UpdateMethod call, that correction IS collected for model improvement.

### Common Workflow
1. Use `GetTypeMembers` to explore a class structure
2. Call `UpdateMethod` with type name + method name (no file path needed)
3. For overloaded methods, add `parameterTypes` parameter
4. For AI generation, start with `auto=true`, review `generatedCode`, fix if wrong
5. Add `comment` parameter to update/add XML documentation

The tool handles all the complexity of Roslyn workspace management, syntax tree manipulation, formatting preservation, and file I/O, exposing a simple name-based interface.
