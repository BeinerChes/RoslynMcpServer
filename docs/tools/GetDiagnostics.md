# GetDiagnostics

## Description

GetDiagnostics compiles a .NET solution and returns compiler diagnostics (errors, warnings, and info messages) in structured JSON format. It operates in two modes: summary mode (counts by diagnostic code) and detail mode (file/line/method info for a specific diagnostic ID). Unlike `dotnet build`, it provides structured, filterable output optimized for programmatic analysis and automated fixing.

The tool handles:
- Full solution compilation with Roslyn workspace
- Both compiler diagnostics (CS*) and analyzer diagnostics (CA*)
- Summary mode: counts and descriptions grouped by diagnostic ID
- Detail mode: precise file/line/method location for each diagnostic
- Filtering by severity (error/warning/info), project, or diagnostic ID
- Pagination for large result sets
- Detection of fixable diagnostics (shows `[fix]` flag)
- Reporting of suppressed diagnostics

## Comparison with Native Claude Code Tools

### vs Bash (`dotnet build`)
- **dotnet build** outputs unstructured console text mixed with build progress
- **GetDiagnostics** returns structured JSON with counts, locations, and metadata
- **dotnet build** requires manual parsing with grep/regex to extract diagnostics
- **GetDiagnostics** provides pre-parsed, filterable results
- **dotnet build** shows all diagnostics at once (potentially thousands of lines)
- **GetDiagnostics** offers summary mode (counts only) or detail mode with pagination
- **dotnet build** doesn't indicate which diagnostics are auto-fixable
- **GetDiagnostics** marks fixable diagnostics with `[fix]` flag for use with BatchApplyCodeFixes
- **dotnet build** output varies wildly in size (1K-100K tokens for large solutions)
- **GetDiagnostics** provides compact output (200-1,500 tokens)

### When to use GetDiagnostics
- Understanding code quality issues before fixing them
- Finding all instances of a specific warning/error type
- Checking build status programmatically
- Identifying auto-fixable diagnostics for BatchApplyCodeFixes
- Filtering diagnostics by project or severity
- Getting structured data for analysis or reporting

## Real-World Example

### Scenario
Check the solution for compilation warnings and find all CA1822 warnings (members that should be static).

**Test project:** RoslynMcpServer.slnx (actual testing, not estimates)

### Approach 1: Using Native Tools (Bash + dotnet build)

**Step 1: Build and capture output**
```bash
dotnet build 2>&1 | grep ": warning"
```
**Result:** Console output with warnings (sample):
```
D:\repos\RoslynMcpServer\src\McpServer.cs(164,46): warning CS8604: Possible null reference argument.
Services\SymbolSearchService.cs(10,41): warning CA1822: Member 'SearchSymbolsAsync' does not access instance data
... (hundreds more lines)
```
**Tokens:** ~100 command + ~5,000-15,000 output (unstructured) = ~5,000-15,000 tokens

**Step 2: Parse output to find specific diagnostic**
```bash
dotnet build 2>&1 | grep "CA1822"
```
**Result:** Lines matching CA1822
**Tokens:** ~100 command + ~500 output = ~600 tokens

**Total: ~5,600-15,600 tokens, 2 operations**

**Limitations:**
- Unstructured text output requires manual parsing
- No count summary (must count lines manually)
- No fixability indication
- Verbose output (includes build progress, paths, etc.)
- Must run build twice for summary + detail views
- Hard to filter by project or severity

### Approach 2: Using GetDiagnostics (Roslyn)

**Step 1: Get summary**
```
GetDiagnostics()
```
**Result:**
```json
{
  "errors": 0,
  "warnings": 3215,
  "summary": [
    "CA2007 (Warning) [fix]: Consider calling ConfigureAwait... (1338)",
    "CA1707 (Warning) [fix]: Identifiers should not contain underscores (218)",
    "CA1822 (Warning) [fix]: Mark members as static (14)",
    ...
  ]
}
```
**Tokens:** ~50 input + ~1,200 output = ~1,250 tokens

**Step 2: Get details for CA1822**
```
GetDiagnostics(diagnosticId: "CA1822", maxResults: 20)
```
**Result:**
```json
{
  "total": 14,
  "entries": [
    "Services\\SymbolSearchService.cs:10 SymbolSearchService.SearchSymbolsAsync - Member 'SearchSymbolsAsync' does not access instance data and can be marked as static",
    "src\\SolutionAnalyzerService.cs:92 SolutionAnalyzerService.GetProjectsInBuildOrderAsync - Member 'GetProjectsInBuildOrderAsync' does not access instance data",
    ...
  ]
}
```
**Tokens:** ~70 input + ~400 output = ~470 tokens

**Total: ~1,720 tokens, 2 operations**

### Comparison Summary

| Aspect | Native (dotnet build) | GetDiagnostics (Roslyn) |
|--------|----------------------|-------------------------|
| **Token usage** | ~5,600-15,600 | ~1,720 |
| **Operations** | 2 (build + grep) | 2 (summary + detail) |
| **Output format** | Unstructured text | Structured JSON |
| **Parsing required** | Yes (manual grep/regex) | No (pre-parsed) |
| **Summary view** | No (all output at once) | Yes (counts by ID) |
| **Detail view** | Full build output | Filtered by diagnostic ID |
| **Fixability flag** | No | Yes (`[fix]` indicates auto-fixable) |
| **Pagination** | No | Yes (maxResults, offset) |
| **Severity filter** | Manual (grep) | Built-in (error/warning/info) |
| **Project filter** | Manual (grep) | Built-in (projectFilter param) |

**GetDiagnostics uses 89% fewer tokens** (1,720 vs 5,600-15,600) and provides structured, filterable output.

## How It Works

### Compilation Process

GetDiagnostics compiles the solution using Roslyn's workspace:

1. **Load solution** into MSBuildWorkspace
2. **Compile all projects** to get compiler diagnostics (CS*)
3. **Run analyzers** if requested (CA* diagnostics require .NET analyzers)
4. **Collect diagnostics** from all projects
5. **Filter and group** based on parameters

### Two Modes of Operation

**Summary Mode (no diagnosticId parameter):**
- Groups diagnostics by ID
- Returns counts for each diagnostic type
- Indicates which diagnostics are auto-fixable
- Shows suppressed counts
- Compact output (~1,000-1,500 tokens for large solutions)

**Detail Mode (with diagnosticId parameter):**
- Filters to specific diagnostic ID
- Returns file path, line number, containing type/method
- Supports pagination (maxResults, offset)
- Can filter by project or severity
- Compact output (~200-500 tokens per page)

### Diagnostic Collection

The tool collects diagnostics from multiple sources:

```csharp
// Compiler diagnostics (CS*, IDE*)
var compilerDiagnostics = compilation.GetDiagnostics();

// Analyzer diagnostics (CA*)
var analyzerDiagnostics = await compilation.WithAnalyzers(analyzers)
    .GetAnalyzerDiagnosticsAsync();
```

### Filtering and Grouping

Diagnostics are filtered and grouped before returning:

1. **Severity filter**: "error", "warning", "info", or "all"
2. **Project filter**: Partial match on project name
3. **Diagnostic ID**: Specific ID like "CA1822" or "CS0168"
4. **Suppression detection**: Identifies `#pragma warning disable` suppressions

### Invocation

**Basic usage (summary):**
```
GetDiagnostics()
```

**Filter by severity:**
```
GetDiagnostics(severityFilter: "error")
```

**Get details for specific diagnostic:**
```
GetDiagnostics(diagnosticId: "CA1822")
```

**Filter to specific project:**
```
GetDiagnostics(
  diagnosticId: "CS0168",
  projectFilter: "RoslynMcpServer"
)
```

**Pagination:**
```
GetDiagnostics(
  diagnosticId: "CA1031",
  maxResults: 50,
  offset: 0
)
```

### Response Format

**Summary mode:**
```json
{
  "errors": 0,
  "warnings": 3215,
  "summary": [
    "CA2007 (Warning) [fix]: Consider calling ConfigureAwait on the awaited task (1338) (1338 suppressed)",
    "CA1707 (Warning) [fix]: Identifiers should not contain underscores (218) (218 suppressed)",
    "CA1822 (Warning) [fix]: Mark members as static (14)"
  ]
}
```

**Detail mode:**
```json
{
  "total": 14,
  "entries": [
    "Services\\SymbolSearchService.cs:10 SymbolSearchService.SearchSymbolsAsync - Member 'SearchSymbolsAsync' does not access instance data and can be marked as static",
    "src\\SolutionAnalyzerService.cs:92 SolutionAnalyzerService.GetProjectsInBuildOrderAsync - Member 'GetProjectsInBuildOrderAsync' does not access instance data"
  ]
}
```

**No diagnostics found:**
```json
{
  "errors": 0,
  "warnings": 0,
  "summary": []
}
```

### Understanding the Output

**Summary format:** `ID (Severity) [fix]: Description (Count) (Suppressed)`
- `[fix]` appears only if diagnostic is auto-fixable with BatchApplyCodeFixes
- `(X suppressed)` shows count of suppressed instances via `#pragma`

**Detail format:** `file:line Type.Method - message`
- Relative file paths (from solution root)
- Line number for navigation
- Containing type and method for context
- Full diagnostic message

### Common Workflows

**Workflow 1: Check build status**
```
1. GetDiagnostics(severityFilter: "error")     # Any compilation errors?
2. If errors > 0, review and fix
3. GetDiagnostics(severityFilter: "warning")   # Check warnings
```

**Workflow 2: Fix specific diagnostic type**
```
1. GetDiagnostics()                            # Get summary with [fix] flags
2. Find fixable diagnostic (e.g., "CA1822 (Warning) [fix]")
3. GetDiagnostics(diagnosticId: "CA1822")      # See all instances
4. BatchApplyCodeFixes(diagnosticId: "CA1822") # Fix all
5. GetDiagnostics(diagnosticId: "CA1822")      # Verify fixed
```

**Workflow 3: Clean up specific project**
```
1. GetDiagnostics(projectFilter: "Tests")      # See all diagnostics in Tests project
2. Get details for specific IDs
3. Fix manually or with BatchApplyCodeFixes
```

**Workflow 4: Before committing code**
```
1. GetDiagnostics(severityFilter: "error")     # No errors allowed
2. GetDiagnostics()                            # Review warning summary
3. BatchApplyCodeFixes(diagnosticId: "...")    # Fix auto-fixable warnings
4. GetDiagnostics()                            # Final check
```

### Integration with Other Tools

**GetDiagnostics + BatchApplyCodeFixes:**
```
GetDiagnostics()                               # Find fixable diagnostics (look for [fix] flag)
BatchApplyCodeFixes(diagnosticId: "CA1822")    # Fix all instances
GetDiagnostics(diagnosticId: "CA1822")         # Verify fixed (should return 0)
```

**GetDiagnostics + ApplyCodeFix:**
```
GetDiagnostics(diagnosticId: "CS0168")         # Find all instances
ApplyCodeFix(diagnosticId: "CS0168", ...)      # Fix specific one manually
```

**GetDiagnostics + UpdateMethod:**
```
GetDiagnostics(diagnosticId: "CA1502")         # Find complex methods
# CA1502 not auto-fixable - refactor manually
UpdateMethod(...)                              # Simplify method
GetDiagnostics(diagnosticId: "CA1502")         # Verify improved
```

### Diagnostic ID Reference

**Common Compiler Diagnostics (CS*):**
- **CS0168**: Variable declared but never used
- **CS0618**: Using obsolete member
- **CS8019**: Unnecessary using directive
- **CS8604**: Possible null reference

**Common Analyzer Diagnostics (CA*):**
- **CA1822**: Member can be marked as static
- **CA1031**: Do not catch general exception types
- **CA1707**: Identifiers should not contain underscores
- **CA2007**: Consider calling ConfigureAwait

See [Microsoft docs](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/) for full CA* reference.

### When dotnet build is Better

Use `dotnet build` via Bash when:
- **You need full build output** including restore, compilation progress, timing
- **Testing actual build process** (not just diagnostic checking)
- **Debugging MSBuild issues** (need verbose build logs)
- **CI/CD pipelines** where build output is logged for auditing

GetDiagnostics is optimized for diagnostic analysis and automated fixing, not build execution.

## Performance Notes

- **CS* diagnostics:** Fast (~2-5 seconds, uses compilation diagnostics)
- **CA* diagnostics:** Slower (~10-20 seconds, runs .NET analyzers)
- **Summary mode:** Slightly faster than detail mode (less data processing)
- **First call:** May be slower (loads solution, analyzers)
- **Subsequent calls:** Faster (workspace cached)

For checking specific diagnostics, detail mode with diagnosticId is faster than summary mode.
