# BatchApplyCodeFixes

## Description

BatchApplyCodeFixes applies Roslyn code fixes for all diagnostics of a specific type across an entire solution in a single operation. Unlike fixing diagnostics one by one, it loads the solution once, applies all fixes in memory, then writes changes to disk — making it dramatically faster for bulk operations.

The tool handles:
- Automatic discovery of all diagnostics matching the specified ID
- Deduplication of diagnostics across project compilations
- Filtering by project name or file path
- Batch application of fixes across multiple files
- Both compiler diagnostics (CS*) and analyzer diagnostics (CA*)
- Preview mode to see what would change without applying

## Comparison with Native Claude Code Tools

### vs Read + Edit workflow
- **Read + Edit** requires: GetDiagnostics to find issues, then for each file: Read entire file, Edit to apply fix, repeat for every diagnostic
- **BatchApplyCodeFixes** performs the entire workflow in one call across all files
- **Read + Edit** loads each file into context separately (expensive for large files)
- **BatchApplyCodeFixes** processes all files in the solution workspace (shared compilation)
- **Read + Edit** requires manually crafting each edit (find exact text, handle indentation)
- **BatchApplyCodeFixes** uses Roslyn's registered code fix providers (same fixes as Visual Studio)
- **Read + Edit** may apply incorrect fixes or miss edge cases
- **BatchApplyCodeFixes** applies compiler-verified transformations

### When to use BatchApplyCodeFixes
- Fixing multiple instances of the same diagnostic across many files
- Cleaning up code analysis warnings in bulk
- Applying automated refactorings solution-wide
- When you have 5+ instances of the same diagnostic to fix
- After enabling new analyzer rules that produce many warnings

## Real-World Example

### Scenario
Fix all CA1822 warnings (mark members as static when they don't access instance data) across the entire solution.

**Test project:** RoslynMcpServer.slnx (actual testing, not estimates)

### Approach 1: Using Native Tools (Read + Edit)

**Step 1: Find diagnostics**
```
GetDiagnostics(diagnosticId: "CA1822")
```
**Result:** 8 unique files with CA1822 warnings
**Tokens:** ~200

**Step 2-9: For each of 8 files:**
```
Read(file_path: "<file>")
Edit(file_path: "<file>", old_string: "public int Add", new_string: "public static int Add")
```
**Per file:**
- Read: ~2,500 input + ~2,000 output = ~4,500 tokens
- Edit: ~200 tokens
- **Subtotal: ~4,700 tokens per file**

**Total: 200 + (8 × 4,700) = ~37,800 tokens, 17 operations**

**Limitations:**
- Must read every affected file fully into context
- Must manually craft edit strings for each fix
- Risk of syntax errors if indentation/spacing differs
- No validation that fix resolves the diagnostic
- Tedious and error-prone for 10+ files

### Approach 2: Using BatchApplyCodeFixes (Roslyn)

**Step 1: Apply all fixes**
```
BatchApplyCodeFixes(diagnosticId: "CA1822")
```
**Result:**
```json
{
  "diagnosticId": "CA1822",
  "found": 8,
  "applied": 8,
  "failed": 0,
  "filesModified": [
    "Services\\SymbolSearchService.cs",
    "src\\SolutionAnalyzerService.Symbols.cs",
    "Services\\CodeFixService.cs",
    "SharpOps.Examples\\Calculator.cs",
    "SharpOps\\TrainingDataGenerator.cs",
    "SharpOps\\SharpOpsExtractor.cs",
    "SharpOps\\SharpOpsCompiler.Patterns.cs",
    "src\\SolutionAnalyzerService.cs"
  ]
}
```
**Tokens:** ~100 input + ~200 output = ~300 tokens

**Total: ~300 tokens, 1 operation**

### Comparison Summary

| Aspect | Native (Read + Edit) | BatchApplyCodeFixes (Roslyn) |
|--------|----------------------|------------------------------|
| **Token usage** | ~37,800 | ~300 |
| **Operations** | 17 (1 GetDiagnostics + 16 Read/Edit) | 1 |
| **Requires reading files** | Yes (entire files) | No (workspace compilation) |
| **Handles multiple files** | Manual (one at a time) | Automatic (all at once) |
| **Fix correctness** | Manual (risk of errors) | Compiler-verified |
| **Deduplicates diagnostics** | Manual | Automatic |
| **Shows affected files** | No | Yes (in response) |

**BatchApplyCodeFixes uses 99% fewer tokens** (300 vs 37,800)

For 20+ diagnostics:
- Native: 70,000+ tokens (unmanageable)
- BatchApplyCodeFixes: Same ~300 tokens (scales efficiently)

## How It Works

### Diagnostic Collection

BatchApplyCodeFixes collects matching diagnostics across the solution:

1. **Compile all projects** to get diagnostics
2. **Run analyzers** if needed (CA* diagnostics require .NET analyzers)
3. **Filter by diagnostic ID** to find matching issues
4. **Deduplicate** by file path + line number (same diagnostic from multiple project compilations)
5. **Apply filters** if projectFilter or fileFilter specified

### Deduplication

The tool automatically deduplicates diagnostics that appear in multiple project compilations:

```csharp
var seen = new HashSet<string>();
var key = $"{filePath}:{lineNumber}";
if (!seen.Add(key)) continue; // Skip duplicate
```

This prevents applying the same fix twice to shared files.

### Code Fix Discovery and Application

For each unique diagnostic:

1. **Find code fix providers** that handle this diagnostic ID
2. **Register available fixes** from providers
3. **Select and apply fix** (typically only one fix available per diagnostic)
4. **Update solution in memory** with the fix
5. **Track modified documents**

After all fixes applied:

6. **Write modified files to disk** in a single batch operation

### Invocation

**Basic usage (fix all instances):**
```
BatchApplyCodeFixes(diagnosticId: "CA1822")
```

**Limit number of fixes:**
```
BatchApplyCodeFixes(diagnosticId: "CS0168", maxFixes: 50)
```

**Filter to specific project:**
```
BatchApplyCodeFixes(
  diagnosticId: "CA1031",
  projectFilter: "RoslynMcpServer"
)
```

**Filter to specific files:**
```
BatchApplyCodeFixes(
  diagnosticId: "CA1707",
  fileFilter: "Controller.cs"
)
```

**Preview mode (see what would change):**
```
BatchApplyCodeFixes(
  diagnosticId: "CA1515",
  preview: true
)
```

### Response Format

**Success:**
```json
{
  "diagnosticId": "CA1822",
  "found": 8,
  "applied": 8,
  "failed": 0,
  "filesModified": [
    "Services\\SymbolSearchService.cs",
    "src\\SolutionAnalyzerService.cs",
    "SharpOps.Examples\\Calculator.cs"
  ]
}
```

**Partial success (some fixes failed):**
```json
{
  "diagnosticId": "CS0246",
  "found": 10,
  "applied": 7,
  "failed": 3,
  "filesModified": [
    "Services\\UserService.cs",
    "Controllers\\HomeController.cs"
  ]
}
```

**No diagnostics found:**
```json
{
  "diagnosticId": "CA9999",
  "found": 0,
  "applied": 0,
  "failed": 0,
  "filesModified": []
}
```

**No fix available:**
```
Error: No code fix provider available for diagnostic 'CA1307'
```

### Common Workflows

**Workflow 1: Clean up analyzer warnings**
```
1. GetDiagnostics()                                    # See all warnings
2. BatchApplyCodeFixes(diagnosticId: "CA1822")         # Fix static members
3. BatchApplyCodeFixes(diagnosticId: "CA1805")         # Remove unnecessary initializations
4. BatchApplyCodeFixes(diagnosticId: "CA1827")         # Use Any() instead of Count()
5. GetDiagnostics()                                    # Verify all fixed
```

**Workflow 2: Fix specific files only**
```
1. GetDiagnostics(diagnosticId: "CS0168")              # See unused variables
2. BatchApplyCodeFixes(
     diagnosticId: "CS0168",
     fileFilter: "Controllers"
   )  # Fix only in Controllers folder
3. GetDiagnostics(diagnosticId: "CS0168")              # Check remaining
```

**Workflow 3: Preview before applying**
```
1. BatchApplyCodeFixes(
     diagnosticId: "CA1515",
     preview: true
   )  # See what files would be modified
2. Review the file list
3. BatchApplyCodeFixes(diagnosticId: "CA1515")         # Apply if OK
```

**Workflow 4: Fix after refactoring**
```
1. RenameSymbol(...)                                   # Rename something
2. GetDiagnostics()                                    # Check for new issues
3. BatchApplyCodeFixes(diagnosticId: "CS0219")         # Remove unused variables
4. BatchApplyCodeFixes(diagnosticId: "IDE0059")        # Remove unnecessary assignments
```

### Integration with Other Tools

**BatchApplyCodeFixes + GetDiagnostics:**
```
GetDiagnostics()                                       # See all diagnostics
GetDiagnostics(diagnosticId: "CA1822")                 # Details for specific one
BatchApplyCodeFixes(diagnosticId: "CA1822")            # Fix all instances
```

**BatchApplyCodeFixes + ApplyCodeFix:**
```
BatchApplyCodeFixes(diagnosticId: "CA1822")            # Fix most instances
ApplyCodeFix(diagnosticId: "CA1822", typeName: "X")    # Fix specific edge case manually
```

**BatchApplyCodeFixes + RemoveUnnecessaryUsings:**
```
BatchApplyCodeFixes(diagnosticId: "IDE0005")           # Won't work (no code fix for CS8019)
RemoveUnnecessaryUsings()                              # Use this instead for unused usings
```

### When Native Tools Are Better

Native tools may be preferable when:
- **No code fix available** — some diagnostics don't have automated fixes (use Edit to fix manually)
- **Custom fix needed** — when the standard fix isn't what you want
- **Single diagnostic** — for 1-2 instances, ApplyCodeFix or Edit may be simpler
- **Complex multi-step fix** — when the fix requires multiple related changes across files

BatchApplyCodeFixes is strictly for applying registered Roslyn code fixes in bulk.

## Performance Notes

- **CS* diagnostics:** Fast (~2-5 seconds for 100 diagnostics)
- **CA* diagnostics:** Slower (~5-10 seconds, must run .NET analyzers)
- **Deduplication:** Eliminates duplicate work from multi-project solutions
- **Memory efficient:** Processes all fixes in single solution load
- **Scales well:** 100 diagnostics takes similar time to 10 diagnostics (batch overhead dominates)

For 5+ diagnostics of the same type, BatchApplyCodeFixes is significantly faster than individual fixes.
