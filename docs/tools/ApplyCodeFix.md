# ApplyCodeFix

## Description

ApplyCodeFix automatically applies Roslyn's suggested fixes for compiler diagnostics and code analysis warnings. It uses semantic understanding to locate diagnostics by ID (like CA1822 or CS0168), optionally narrows to specific types or members, discovers available code fixes from registered providers, and applies the transformation — all without requiring file paths or line numbers.

The tool handles:
- Diagnostic lookup by ID across the entire solution
- Optional filtering by type name and/or member name
- Automatic discovery of available code fix providers
- Multiple fix selection when alternatives exist
- Cross-file changes when fixes affect multiple documents
- Both compiler diagnostics (CS*) and analyzer diagnostics (CA*)

## Comparison with Native Claude Code Tools

### vs Read + Edit workflow
- **Read + Edit** requires: 1) GetDiagnostics to find issues, 2) Parse output to extract file/line, 3) Read the file, 4) Manually determine the fix, 5) Edit to apply
- **ApplyCodeFix** performs the entire workflow in one call: finds diagnostic, discovers fix, applies it
- **Read + Edit** requires understanding what the fix should be (add static? remove unused variable?)
- **ApplyCodeFix** uses Roslyn's built-in code fix providers — the same fixes used by Visual Studio
- **Read + Edit** may apply incorrect fixes (wrong syntax, missing edge cases)
- **ApplyCodeFix** applies compiler-verified transformations that preserve semantics

### When to use ApplyCodeFix
- Fixing compiler warnings and errors automatically
- Applying code analysis suggestions (CA* rules)
- Batch fixing common issues across a codebase
- Ensuring fixes match IDE behavior exactly
- Avoiding manual text manipulation for semantic changes
- When you don't know the exact file/line location of a diagnostic

## Real-World Example

### Scenario
Fix the CA1822 diagnostic on `Calculator.Add` — the analyzer suggests marking the method as `static` since it doesn't access instance data.

**Test project:** SharpOps.Examples.csproj (actual testing, not estimates)

### Approach 1: Using Native Tools (Read + Edit)

**Step 1: Read the file**
```
Read(file_path: "D:\\repos\\RoslynMcpServer\\SharpOps.Examples\\Calculator.cs")
```
**Result:** Full file content (40 lines)
**Tokens:** ~480 input + ~320 output = ~800 tokens

**Step 2: Edit to add static keyword**
```
Edit(
  file_path: "D:\\repos\\RoslynMcpServer\\SharpOps.Examples\\Calculator.cs",
  old_string: "    public int Add(int a, int b)",
  new_string: "    public static int Add(int a, int b)"
)
```
**Tokens:** ~190 input + ~30 output = ~220 tokens

**Total: ~1,020 tokens, 2 operations**

**Limitations:**
- Must know exact file location (from GetDiagnostics output)
- Must manually read file to understand context
- Must determine correct fix (add static keyword before return type)
- Risk of syntax errors if indentation/spacing differs
- No validation that fix resolves the diagnostic

### Approach 2: Using ApplyCodeFix (Roslyn)

**Step 1: Apply the fix**
```
ApplyCodeFix(
  diagnosticId: "CA1822",
  typeName: "Calculator",
  memberName: "Add"
)
```
**Result:**
```json
{
  "success": true,
  "filePath": "D:\\repos\\RoslynMcpServer\\SharpOps.Examples\\Calculator.cs",
  "diagnosticId": "CA1822",
  "diagnosticMessage": "Member 'Add' does not access instance data and can be marked as static",
  "line": 13,
  "column": 16,
  "appliedFixTitle": "Make static",
  "availableFixes": [
    {
      "index": 0,
      "title": "Make static",
      "equivalenceKey": "MarkMembersAsStaticCodeFix"
    }
  ],
  "filesChanged": 1
}
```
**Tokens:** ~80 input + ~180 output = ~260 tokens

**Total: ~260 tokens, 1 operation**

### Comparison Summary

| Aspect | Native (Read + Edit) | ApplyCodeFix (Roslyn) |
|--------|----------------------|-----------------------|
| **Token usage** | ~1,020 | ~260 |
| **Operations** | 2 (Read, Edit) | 1 |
| **Requires file path** | Yes | No (infers from diagnostic) |
| **Requires knowing fix** | Yes (manual) | No (uses code fix providers) |
| **Handles overloads** | Manual disambiguation | Automatic via memberName |
| **Fix correctness** | Manual (risk of errors) | Compiler-verified |
| **Multi-file fixes** | Manual (read all files) | Automatic |
| **Shows available fixes** | No | Yes (availableFixes array) |

**ApplyCodeFix uses 75% fewer tokens** (260 vs 1,020)

For multiple diagnostics:
- Native: ~1,000 tokens per diagnostic (read file each time, manual fixes)
- ApplyCodeFix: ~260 tokens per diagnostic (or use BatchApplyCodeFixes for bulk operations)

On larger files (500+ lines):
- Native: 3,000+ tokens to read file
- ApplyCodeFix: Same ~260 tokens (doesn't need to read entire file)

## How It Works

### Diagnostic Discovery

ApplyCodeFix searches for diagnostics across the entire solution:

1. **Compile all projects** in the solution to collect diagnostics
2. **Run analyzers** if needed (CA* diagnostics require .NET analyzers)
3. **Filter by diagnostic ID** to find matching issues
4. **Optional filtering** by type name and/or member name using syntax tree walking

If `typeName` and `memberName` are omitted, the tool fixes the **first** instance found. Use narrowing for precision:
- `typeName: "Calculator"` — fixes first CA1822 in Calculator class
- `typeName: "Calculator", memberName: "Add"` — fixes CA1822 on Calculator.Add specifically

### Type/Member Filtering

The `FilterByTypeAndMemberAsync` method uses syntax tree analysis:

```csharp
// For each diagnostic, walk up the syntax tree
var enclosingType = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
var enclosingMember = node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();

// Match against typeName/memberName
if (typeName && enclosingType.Identifier.Text != typeName) continue;
if (memberName && memberIdentifier != memberName) continue;
```

This allows precise targeting without knowing file paths or line numbers.

### Code Fix Discovery

Roslyn's code fix system:

1. **Load code fix providers** from assemblies:
   - `Microsoft.CodeAnalysis.CSharp.Features` (CS* diagnostics)
   - Bundled .NET analyzers (CA* diagnostics)

2. **Find relevant providers** that handle the diagnostic ID:
   ```csharp
   providers.Where(p => p.FixableDiagnosticIds.Contains(diagnosticId))
   ```

3. **Collect available fixes** from all matching providers:
   ```csharp
   var context = new CodeFixContext(document, diagnostic, registerFix, CancellationToken.None);
   await provider.RegisterCodeFixesAsync(context);
   ```

4. **Select fix** by index (default 0, or user-specified via `fixIndex`)

### Applying the Fix

Once a fix is selected:

1. **Get operations** from the CodeAction:
   ```csharp
   var operations = await codeAction.GetOperationsAsync();
   var applyChangesOp = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
   ```

2. **Apply to solution**:
   ```csharp
   var changedSolution = applyChangesOp.ChangedSolution;
   var changedDocuments = changedSolution.GetChanges(solution).GetProjectChanges()
       .SelectMany(p => p.GetChangedDocuments());
   ```

3. **Write files to disk**:
   ```csharp
   foreach (var docId in changedDocuments) {
       var doc = changedSolution.GetDocument(docId);
       var text = await doc.GetTextAsync();
       await File.WriteAllTextAsync(doc.FilePath, text.ToString());
   }
   ```

The entire transformation is atomic — either all files are updated or none are.

### Invocation

**Basic usage (fixes first instance):**
```
ApplyCodeFix(diagnosticId: "CA1822")
```

**Narrow to specific type:**
```
ApplyCodeFix(
  diagnosticId: "CA1822",
  typeName: "Calculator"
)
```

**Narrow to specific member:**
```
ApplyCodeFix(
  diagnosticId: "CA1822",
  typeName: "Calculator",
  memberName: "Add"
)
```

**Select specific fix when multiple available:**
```
ApplyCodeFix(
  diagnosticId: "CS0246",
  typeName: "Program",
  fixIndex: 1
)
```

### Response Format

**Success:**
```json
{
  "success": true,
  "filePath": "SharpOps.Examples\\Calculator.cs",
  "diagnosticId": "CA1822",
  "diagnosticMessage": "Member 'Add' does not access instance data and can be marked as static",
  "line": 13,
  "column": 16,
  "appliedFixTitle": "Make static",
  "availableFixes": [
    {
      "index": 0,
      "title": "Make static",
      "equivalenceKey": "MarkMembersAsStaticCodeFix"
    }
  ],
  "filesChanged": 1
}
```

**Multiple fixes available (must select):**
```json
{
  "success": false,
  "error": "Multiple fixes available. Specify fixIndex to select one.",
  "filePath": "Program.cs",
  "diagnosticId": "CS0246",
  "diagnosticMessage": "The type or namespace name 'Foo' could not be found",
  "line": 10,
  "column": 5,
  "availableFixes": [
    {
      "index": 0,
      "title": "using System.Foo;",
      "equivalenceKey": "AddImport"
    },
    {
      "index": 1,
      "title": "Generate type 'Foo'",
      "equivalenceKey": "GenerateType"
    }
  ]
}
```

**No diagnostic found:**
```json
{
  "success": false,
  "error": "No diagnostics found with ID 'CA9999'",
  "diagnosticId": "CA9999"
}
```

**No fix available:**
```json
{
  "success": false,
  "error": "No code fix available for diagnostic 'CS1002': ; expected",
  "filePath": "Program.cs",
  "diagnosticId": "CS1002",
  "line": 15,
  "column": 20
}
```

### Common Workflows

**Workflow 1: Fix all warnings of one type**
```
1. GetDiagnostics(diagnosticId: "CA1822")  # See summary
2. ApplyCodeFix(diagnosticId: "CA1822")    # Fix first instance
3. Repeat step 2 until all fixed
   OR use BatchApplyCodeFixes(diagnosticId: "CA1822") for bulk
```

**Workflow 2: Fix specific member issue**
```
1. GetDiagnostics(diagnosticId: "CS0168")  # Find unused variables
2. Review the list
3. ApplyCodeFix(
     diagnosticId: "CS0168",
     typeName: "MyClass",
     memberName: "MyMethod"
   )  # Fix specific one
```

**Workflow 3: Handle multiple fix options**
```
1. ApplyCodeFix(diagnosticId: "CS0246")  # Missing type
   # Returns error with availableFixes list
2. Review options:
   - index 0: "using System.Foo;"
   - index 1: "Generate type 'Foo'"
3. ApplyCodeFix(diagnosticId: "CS0246", fixIndex: 0)  # Choose import
```

**Workflow 4: Clean up after refactoring**
```
1. RenameSymbol(...)                       # Rename something
2. GetDiagnostics()                        # Check for new warnings
3. ApplyCodeFix(diagnosticId: "CS0219")    # Remove unused variables
4. ApplyCodeFix(diagnosticId: "IDE0059")   # Remove unnecessary assignments
```

### Integration with Other Tools

**ApplyCodeFix + GetDiagnostics:**
```
GetDiagnostics()                           # Summary of all issues
GetDiagnostics(diagnosticId: "CA1822")     # Details for specific type
ApplyCodeFix(diagnosticId: "CA1822")       # Fix them
```

**ApplyCodeFix + BatchApplyCodeFixes:**
```
ApplyCodeFix(...)                          # Fix single instance (precise control)
BatchApplyCodeFixes(diagnosticId: "...")   # Fix all instances (bulk operation)
```

**ApplyCodeFix + FindDeadCode:**
```
FindDeadCode()                             # Identify unused methods
# Review the list
DeleteMember(...)                          # Remove confirmed dead code
# OR
ApplyCodeFix(diagnosticId: "IDE0051")      # Let code fix remove it
```

### When Edit is Better

Edit is still useful for:
- **No code fix available** — some diagnostics don't have automated fixes
- **Custom fixes** — when you need a fix that differs from the standard one
- **Editing comments or docs** — ApplyCodeFix only handles semantic code
- **Syntax errors** — Roslyn may not offer fixes for parse errors
- **Bulk text changes** — replacing patterns across files

ApplyCodeFix is strictly for applying registered Roslyn code fixes.

## Performance Notes

- **CS* diagnostics:** Fast (uses compilation diagnostics, ~1-2 seconds)
- **CA* diagnostics:** Slower (runs .NET analyzers, ~3-5 seconds per project)
- **First call:** May be slower (loads code fix providers from assemblies)
- **Subsequent calls:** Faster (providers cached in memory)
- **Bulk fixes:** Use BatchApplyCodeFixes for 10+ fixes to avoid repeated solution loads

The tool is designed for interactive fixing of individual diagnostics. For bulk operations, BatchApplyCodeFixes is more efficient.
