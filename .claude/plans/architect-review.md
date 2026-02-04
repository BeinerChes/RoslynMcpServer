# Architecture Review: RoslynMcpServer
**Date:** 2026-01-26
**Reviewer:** Claude (Principal Engineer Analysis)

## Executive Summary

RoslynMcpServer is a well-structured MCP (Model Context Protocol) server providing C# code analysis via Microsoft Roslyn. The solution follows a clean layered architecture with a separate Graph project for persistence and the main project for tool registration and analysis.

**Key Strengths:**
- Good separation of concerns with Graph layer isolated
- Effective use of partial classes for organizing large service classes
- Comprehensive test coverage with dedicated test project
- Knowledge base with semantic search capabilities

**Critical Concerns:**
1. **Security vulnerability** in GraphApi (path injection) - P0
2. **Excessive coupling** in SolutionAnalyzerService (224 types) and RoslynTools (116 types)
3. **High cyclomatic complexity** in several methods (up to 52)
4. **214 dead code items** indicating incomplete refactoring
5. **Performance issues** with repeated array/JSON allocations

---

## Metrics

| Metric | Value |
|--------|-------|
| Projects | 4 |
| Source files | 85 |
| Symbols in call graph | 1,822 |
| Edges (calls/reads/accesses) | 3,983 |
| Total warnings | 2,260 |
| Dead code items | 214 |
| Knowledge entries created | 7 |

---

## Architecture Overview

```
                    ┌─────────────────────┐
                    │  RoslynMcpServer    │
                    │  (MCP Tools)        │
                    ├─────────────────────┤
                    │ - RoslynTools       │
                    │ - SolutionAnalyzer  │
                    │ - HookValidation    │
                    │ - Instructions      │
                    └─────────┬───────────┘
                              │
              ┌───────────────┼───────────────┐
              │               │               │
              ▼               ▼               ▼
┌─────────────────┐  ┌───────────────┐  ┌────────────────┐
│ RoslynMcpServer │  │ RoslynMcpServer│  │ RoslynMcpServer│
│ .Graph          │  │ .Tests         │  │ .Web           │
├─────────────────┤  ├───────────────┤  ├────────────────┤
│ - GraphDatabase │  │ - xUnit tests │  │ - GraphApi     │
│ - GraphAnalyzer │  │               │  │ - Web UI       │
│ - Knowledge DB  │  │               │  │                │
└─────────────────┘  └───────────────┘  └────────────────┘
```

---

## Critical Findings (P0)

### 1. [SECURITY] File Path Injection in GraphApi

**Location:** `RoslynMcpServer.Web/GraphApi.cs:137, 215`
**Severity:** P0 - CRITICAL
**Impact:** Attackers can probe file system, potential information disclosure

**Problem:**
```csharp
// User input flows directly to File.Exists()
var solutionPath = context.Request.Query["path"];
if (File.Exists(solutionPath)) // VULNERABLE
```

**Implementation Steps:**
1. Add path validation helper method
2. Normalize and verify paths are within allowed directories
3. Add unit tests for path traversal attempts

**Code Fix:**
```csharp
private static bool IsValidSolutionPath(string path, out string normalizedPath)
{
    normalizedPath = null;
    if (string.IsNullOrEmpty(path)) return false;

    var fullPath = Path.GetFullPath(path);
    // Only allow .sln/.slnx files
    if (!fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) &&
        !fullPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        return false;

    normalizedPath = fullPath;
    return true;
}
```

**Acceptance Criteria:**
- [ ] Path traversal attempts rejected
- [ ] Only .sln/.slnx files accepted
- [ ] Security tests added

---

## High Priority (P1)

### 2. Reduce SolutionAnalyzerService Coupling (224 → <96 types)

**Location:** `src/SolutionAnalyzerService.*.cs`
**Severity:** P1
**Impact:** Hard to test, modify, and understand

**Implementation Steps:**
1. Extract `CodeFixService` (ApplyCodeFixAsync, BatchApplyCodeFixAsync, GetCodeFixProviders)
2. Extract `SymbolSearchService` (SearchSymbolsAsync, FindReferencesAsync)
3. Extract `DiagnosticsService` (GetDiagnosticsAsync, RunAnalyzersAsync)
4. Keep SolutionAnalyzerService as facade/coordinator

**Acceptance Criteria:**
- [ ] Each extracted service < 50 types coupling
- [ ] All existing tests pass
- [ ] No new warnings introduced

---

### 3. Reduce BatchApplyCodeFixAsync Complexity (52 → <26)

**Location:** `src/SolutionAnalyzerService.BatchCodeFix.cs:16`
**Severity:** P1
**Impact:** Hard to maintain, test, and debug

**Implementation Steps:**
1. Extract diagnostic collection into separate method
2. Extract fix application loop into separate method
3. Extract result aggregation into separate method
4. Consider using a pipeline pattern

**Acceptance Criteria:**
- [ ] Cyclomatic complexity < 26
- [ ] All existing tests pass
- [ ] Logic unchanged

---

### 4. Fix IDisposable Implementation (CA1816)

**Locations:**
- `src/HookValidationServer.cs:178`
- `RoslynMcpServer.Tests/Services/SetupHooksTests.cs:17`
- `RoslynMcpServer.Tests/Graph/GraphDatabaseTests.cs:556`

**Implementation Steps:**
1. Add `GC.SuppressFinalize(this)` to each Dispose method
2. Consider implementing full dispose pattern if needed

**Code Fix:**
```csharp
public void Dispose()
{
    // Existing disposal logic
    _listener?.Stop();
    _listener?.Close();

    // Add this line
    GC.SuppressFinalize(this);
}
```

**Acceptance Criteria:**
- [ ] CA1816 warnings resolved
- [ ] No memory leaks in long-running scenarios

---

## Medium Priority (P2)

### 5. Fix Constant Array Allocations (CA1861) - 92 occurrences

**Impact:** Unnecessary GC pressure on every tool registration

**Implementation Steps:**
1. Run `BatchApplyCodeFixes(diagnosticId: "CA1861", preview: true)` to review
2. Apply batch fix
3. Review generated static fields for naming consistency

**Acceptance Criteria:**
- [ ] CA1861 count = 0
- [ ] Static fields use consistent naming (e.g., `_toolParamX`)

---

### 6. Mark Static Members (CA1822) - 42 occurrences

**Impact:** Methods not using instance state should be static for clarity

**Implementation Steps:**
1. Run `BatchApplyCodeFixes(diagnosticId: "CA1822", preview: true)`
2. Review and apply fixes
3. Verify no breaking changes to public API

**Acceptance Criteria:**
- [ ] CA1822 count = 0
- [ ] No public API changes

---

### 7. Remove Unnecessary Usings (CS8019) - 41 occurrences

**Implementation Steps:**
1. Run `BatchApplyCodeFixes(diagnosticId: "CS8019")`

**Acceptance Criteria:**
- [ ] CS8019 count = 0
- [ ] Build succeeds

---

### 8. Cache JsonSerializerOptions (CA1869) - 4 occurrences

**Locations:**
- `src/RoslynTools.SetupHooks.cs:223`
- `RoslynMcpServer.Web/GraphApi.cs:91`

**Implementation Steps:**
1. Use existing `RoslynTools.JsonOptions` where accessible
2. Create shared static options in GraphApi
3. Update SetupHooksInProject to use shared instance

**Acceptance Criteria:**
- [ ] CA1869 count = 0
- [ ] JSON output unchanged

---

## Low Priority (P3)

### 9. Clean Up Dead Code (214 items)

**Distribution:**
- Models.CodeFix.cs: 39 items (mostly unused properties)
- Various other files

**Implementation Steps:**
1. Review dead code report by file
2. For properties: verify they're truly unused (not serialized)
3. Remove or mark as used via attribute

**Acceptance Criteria:**
- [ ] Dead code count < 50
- [ ] No functional regression

---

### 10. Reduce ExtractMethodAsync Complexity (39 → <26)

**Location:** `src/SolutionAnalyzerService.ExtractMethod.cs:15`

**Implementation Steps:**
1. Extract data flow analysis into helper
2. Extract syntax generation into helper
3. Simplify main method to orchestration

---

### 11. Reduce Tool Registration Complexity

**Affected Methods:**
- RegisterFindSymbolTool (30)
- RegisterAddTypeTool (29)
- QueryGraphAsync (27)
- RegisterKnowledgeAddTool (27)

**Implementation Steps:**
1. Create `ToolRegistrationBuilder` helper class
2. Extract parameter validation into reusable methods
3. Use fluent API for tool configuration

---

## Recommended Sequence

1. **Week 1 - Security & Quick Wins**
   - Fix path injection in GraphApi (P0)
   - Run batch fixes for CA1816, CS8019

2. **Week 2 - Performance**
   - Run batch fixes for CA1861, CA1822
   - Fix JsonSerializerOptions caching

3. **Week 3-4 - Refactoring**
   - Extract CodeFixService from SolutionAnalyzerService
   - Reduce BatchApplyCodeFixAsync complexity

4. **Ongoing**
   - Clean up dead code
   - Reduce remaining complexity warnings

---

## Knowledge Base Entries Created

| ID | Category | Title |
|----|----------|-------|
| 3 | architecture | Solution Structure Overview - Architecture Review Jan 2026 |
| 4 | gotcha | High coupling in core classes |
| 5 | security | SECURITY: File path injection in GraphApi |
| 6 | performance | Performance issues: Constant arrays and JsonSerializerOptions |
| 7 | workaround | Batch-fixable diagnostics available |

---

## Next Steps

1. Create GitHub issue for P0 security fix
2. Schedule batch fixes as separate PR
3. Plan refactoring sprints for coupling reduction
4. Set up CI to track warning count trend
