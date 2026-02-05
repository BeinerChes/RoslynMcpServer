# Architecture Review: RoslynMcpServer
**Date:** 2026-01-21
**Reviewed by:** Principal Software Engineer (Claude)

## Executive Summary

RoslynMcpServer is a well-architected MCP server with a clean 2-layer structure. The codebase is in excellent health with zero compilation errors and good separation of concerns. The main class (RoslynTools) has 56 members but uses partial classes effectively to organize 30+ tools by functionality.

Key strengths:
- Clean layered architecture (Graph → Application → Tests)
- Comprehensive tool set with consistent patterns
- Extensive use of partial classes for organization
- Good test coverage infrastructure

Areas for improvement:
- 80 constant array allocations (CA1861) - easy performance fix
- 38 unnecessary using directives (CS8019)
- Some dead code in Web project (GraphApi methods)
- 4 IDisposable implementations missing SuppressFinalize

Overall: **Healthy codebase** with minor cleanup opportunities.

## Key Metrics

| Metric | Value |
|--------|-------|
| Projects | 4 (3 production, 1 test) |
| Source Files | 79 analyzed |
| Symbols in Graph | 1,628 |
| Call Graph Edges | 3,580 |
| Compilation Errors | 0 |
| Warnings | 2,055 (mostly suppressed) |
| Dead Code Items | 196 (mostly false positives) |
| Knowledge Entries Created | 3 (total now: 6) |

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Layer 2: Application                      │
│  RoslynMcpServer (main) ← RoslynMcpServer.Web               │
│  30+ MCP tools           Graph visualization API             │
│  RoslynMcpServer.Tests                                       │
├─────────────────────────────────────────────────────────────┤
│                    Layer 1: Core                             │
│  RoslynMcpServer.Graph                                       │
│  GraphDatabase (SQLite) ← KnowledgeDatabase (embeddings)    │
└─────────────────────────────────────────────────────────────┘
```

## Critical Findings (P0)

**None** - No security vulnerabilities, no compilation errors, no critical issues.

## High Priority (P1)

### 1. Fix CA1861: Constant Arrays as Arguments (80 instances)
**Location:** Various files
**Issue:** Creating new arrays repeatedly in method calls
**Fix:** `BatchApplyCodeFixes(solutionPath, "CA1861")`
**Effort:** Auto-fixable

### 2. Fix CA1816: Dispose Pattern (4 instances)
**Location:** GraphDatabase.cs, KnowledgeDatabase.cs
**Issue:** Dispose() should call GC.SuppressFinalize(this)
**Fix:** Add SuppressFinalize to Dispose methods
**Effort:** 15 minutes

### 3. Fix CA1869: Cache JsonSerializerOptions (4 instances)
**Location:** RoslynTools.cs:11
**Issue:** JsonSerializerOptions created repeatedly
**Fix:** Already cached as static field - verify all usages
**Effort:** 15 minutes

## Medium Priority (P2)

### 4. Remove Unnecessary Usings (CS8019: 38 instances)
**Fix:** `BatchApplyCodeFixes(solutionPath, "CS8019")`
**Effort:** Auto-fixable

### 5. Mark Members as Static (CA1822: 38 instances)
**Fix:** `BatchApplyCodeFixes(solutionPath, "CA1822")`
**Effort:** Auto-fixable

### 6. Review Web Project Usage
**Location:** `RoslynMcpServer.Web/GraphApi.cs`
**Issue:** 6 dead code methods (MapGraphApi, GetLastSymbol, etc.)
**Question:** Is web visualization feature deployed/used?
**Fix:** Remove if unused, or document if planned

### 7. Review LastSymbolTracker Usage
**Location:** `src/LastSymbolTracker.cs`
**Issue:** GetLast() and Clear() methods never called
**Fix:** Remove if unused, or implement if planned feature

## Low Priority (P3)

### 8. Global Using Cleanup (CS8933: 8 instances)
**Issue:** Redundant usings that duplicate global usings
**Fix:** Remove duplicates from individual files
**Effort:** 10 minutes

### 9. Dead Code in GraphDatabase
**Location:** `GraphDatabase.Files.cs:108,128`
**Methods:** CleanupDeletedFilesAsync, CleanupSymbolsFromDeletedFilesAsync
**Fix:** Verify if used in incremental analysis, remove if not

### 10. Use Char Overload (CA1866: 1 instance)
**Issue:** String method used where char would be more efficient
**Effort:** 2 minutes

## Recommended Sequence

### Phase 1: Quick Wins (30 minutes)
1. Run batch fix for CA1861 (constant arrays)
2. Run batch fix for CS8019 (unnecessary usings)
3. Run batch fix for CA1822 (static members)
4. Fix CA1816 (Dispose pattern) manually

### Phase 2: Code Review (1 hour)
5. Review Web project - decide keep/remove
6. Review LastSymbolTracker - decide keep/remove
7. Fix CA1869 (JsonSerializerOptions caching)

### Phase 3: Ongoing
8. Continue suppression review as code changes
9. Monitor for new warnings in CI

## Knowledge Base Entries Created

| ID | Category | Title |
|----|----------|-------|
| 4 | architecture | Solution Structure: 4 projects in 2-layer architecture |
| 5 | gotcha | Dead code detection: 196 items are mostly false positives |
| 6 | pattern | Diagnostic patterns: 2,055 warnings (mostly suppressed) |

**Total entries in knowledge base:** 6

## Notes

- Most dead code is false positives (DTO properties for JSON serialization)
- Most suppressions are intentional (CA2007/ConfigureAwait not needed in MCP context)
- No security issues found (no hardcoded credentials, no SQL injection)
- Architecture is sound - no God Classes despite large member counts (partial classes used well)
