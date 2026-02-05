# AddUsing

## Description

AddUsing adds a using directive to a C# file in the correct location with proper sorting. It locates the target file either by type name (semantic search) or by file path (supports partial matching), inserts the using directive in alphabetically sorted order among existing usings, and is idempotent (returns success even if the using already exists).

The tool handles:
- File location by type name (no file path needed)
- File location by partial file path match
- Automatic alphabetical sorting of using directives
- Idempotent insertion (safe to call multiple times)
- Both formats: `"System.Text.Json"` or `"using System.Text.Json;"`

## Comparison with Native Claude Code Tools

### vs Edit tool
- **Edit** requires reading the entire file, finding where usings are located, determining the correct insertion point, and crafting a match string with exact spacing
- **AddUsing** locates the file by type name, finds the using block automatically, and inserts in sorted order
- **Edit** requires you to manually maintain alphabetical order
- **AddUsing** sorts automatically (best practice)
- **Edit** may duplicate the using if it already exists
- **AddUsing** is idempotent (safe to call multiple times)

### vs Write tool
- **Write** requires reading the entire file, modifying the using section, then writing back all content
- **AddUsing** performs surgical insertion without reading/writing entire file
- **Write** requires manual sorting of usings
- **AddUsing** maintains sorted order automatically

### When to use AddUsing
- Adding namespaces when using new types in existing files
- Fixing missing using directive compiler errors (CS0246)
- Preparing files before adding code that uses specific namespaces
- When you know the type name but not the file path
- Ensuring usings are properly sorted (following .NET conventions)

## Real-World Example

### Scenario
Add `using System.Text.Json;` to the Calculator.cs file to use JSON serialization.

**Target:** Insert the using directive in the correct sorted position.

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
**Result:** Full file content (~400 tokens)
```csharp
namespace SharpOps.Examples;

public class Calculator
{
    ...
}
```

**Step 3: Determine insertion point**
Must analyze existing using directives (if any) and determine alphabetical position. In this case, file uses file-scoped namespace, so using must go at the top.

**Step 4: Insert using**
```
Edit(
  file_path: "D:\repos\RoslynMcpServer\SharpOps.Examples\Calculator.cs",
  old_string: "namespace SharpOps.Examples;",
  new_string: "using System.Text.Json;

namespace SharpOps.Examples;"
)
```
**Tokens:** ~100

**Total: ~550 tokens, 3 steps**

**Risks:**
- Must determine if file-scoped or block-scoped namespace
- Must maintain alphabetical order manually
- May insert duplicate if using already exists
- Must handle blank line spacing correctly
- Must know exact namespace location

### Approach 2: Using AddUsing (Roslyn)

**Step 1: Add the using**
```
AddUsing(
  typeName: "Calculator",
  usingDirective: "System.Text.Json"
)
```
**Total: ~40 tokens, 1 step**

**Benefits:**
- No file path needed (finds by type name)
- No need to read entire file
- Automatic alphabetical sorting
- Idempotent (safe to call multiple times)
- Works with both file-scoped and block-scoped namespaces

### Alternative: Add using by file path

If you know the file path but not the type:
```
AddUsing(
  filePath: "Calculator.cs",
  usingDirective: "System.Text.Json"
)
```

Supports partial path match, so `"Calculator"` or `"Examples/Calculator"` also works.

### Comparison Summary

| Aspect | Native Edit | AddUsing (Roslyn) |
|--------|-------------|------------------|
| **Token usage** | ~550 | ~40 |
| **Steps required** | 3 (Glob → Read → Edit) | 1 |
| **File path needed** | Yes (must find it) | No (just type name) |
| **Read entire file** | Yes | No |
| **Sorting** | Manual | Automatic |
| **Idempotent** | No (may duplicate) | Yes |
| **Namespace format handling** | Manual | Automatic |
| **Error risk** | Medium | Low |

**AddUsing uses 93% fewer tokens** (40 vs 550)

On large files (500+ lines):
- Edit: ~5000+ tokens (read entire file)
- AddUsing: ~40 tokens (just type + using)

### Batch Adding Multiple Usings

**Scenario:** Add several using directives for a new feature.

**Native approach:** ~550 tokens per using = ~2750 tokens for 5 usings

**AddUsing approach:**
```
AddUsing(typeName: "Calculator", usingDirective: "System.Text.Json")
AddUsing(typeName: "Calculator", usingDirective: "System.Linq")
AddUsing(typeName: "Calculator", usingDirective: "System.Collections.Generic")
AddUsing(typeName: "Calculator", usingDirective: "System.Threading.Tasks")
AddUsing(typeName: "Calculator", usingDirective: "Microsoft.Extensions.Logging")
```
**Total: ~200 tokens for 5 usings**

All inserted in correct sorted order automatically.

### When Edit is Better
- Non-C# files
- Adding non-using content (comments, attributes, etc.)
- Working with malformed files where Roslyn can't parse

## How It Works

### File Location

**By type name (recommended):**
```
AddUsing(typeName: "Calculator", usingDirective: "System.Text.Json")
```
The tool uses Roslyn's semantic model to search for the type across the solution, finds the containing file, and adds the using directive there.

**By file path:**
```
AddUsing(filePath: "Calculator.cs", usingDirective: "System.Text.Json")
```
Supports partial path matching:
- `"Calculator.cs"` - matches any file ending with Calculator.cs
- `"Examples/Calculator"` - matches path containing Examples and Calculator
- Full path also works

### Using Directive Parsing

Accepts two formats:
- **Namespace only:** `"System.Text.Json"` (preferred)
- **Full directive:** `"using System.Text.Json;"` (also supported)

The tool normalizes both to the correct format.

### Insertion Logic

1. **Locates using directives section** - Finds existing using directives in the file
2. **Checks for duplicates** - If the using already exists, returns success without modification (idempotent)
3. **Determines insertion point** - Computes alphabetically sorted position among existing usings
4. **Inserts in sorted order** - Adds the new using directive maintaining alphabetical order
5. **Formats properly** - Ensures correct spacing and line breaks

The tool handles both namespace styles:
- **File-scoped namespace:** `namespace MyApp.Services;` (C# 10+)
- **Block-scoped namespace:** `namespace MyApp.Services { }`

Usings are inserted before the namespace declaration in both cases, following .NET conventions.

### Sorting Behavior

Usings are sorted alphabetically following standard .NET conventions:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyApp.Core;
```

System namespaces before non-System namespaces is NOT enforced (relies on alphabetical order only).

### Idempotency

Calling AddUsing multiple times with the same using directive is safe:
```
AddUsing(typeName: "Calculator", usingDirective: "System.Text.Json")
AddUsing(typeName: "Calculator", usingDirective: "System.Text.Json") // No-op, returns success
```

This makes it safe to use in scripts or automation where you're not sure if the using exists.

### Common Workflows

**Add using to fix compiler error:**
```
1. GetDiagnostics() → CS0246: The type or namespace name 'JsonSerializer' could not be found
2. AddUsing(typeName: "MyClass", usingDirective: "System.Text.Json")
```

**Prepare file before adding code:**
```
1. AddUsing(typeName: "UserService", usingDirective: "System.Text.Json")
2. AddUsing(typeName: "UserService", usingDirective: "Microsoft.Extensions.Logging")
3. AddMember(typeName: "UserService", memberCode: "...", auto: true)
```

**Add using by file path:**
```
AddUsing(filePath: "Services/UserService", usingDirective: "System.Linq")
```

**Batch add usings:**
```
foreach using in ["System.Text.Json", "System.Linq", "System.Threading.Tasks"]:
    AddUsing(typeName: "Calculator", usingDirective: using)
```

### Integration with Code Generation

When using AddMember with auto=true, you may need to add usings first:
```
1. AddUsing(typeName: "UserService", usingDirective: "System.Text.Json")
2. AddMember(
     typeName: "UserService",
     memberCode: "public string SerializeUser(User user)",
     auto: true,
     comment: "Serializes user to JSON"
   )
```

This ensures the generated code can reference types from the using.

### Error Handling

**Type not found:**
```
Error: Type 'Foo' not found in solution
```

**File not found (when using filePath):**
```
Error: File matching 'Foo.cs' not found
```

**Invalid using directive:**
```
Error: Invalid using directive format
```

**Success (idempotent):**
```
{
  "success": true,
  "alreadyExists": true,
  "message": "Using directive already exists"
}
```

The tool handles all complexity of file location, duplicate detection, sorted insertion, and namespace format handling, exposing a simple type-name or file-path-based interface.
