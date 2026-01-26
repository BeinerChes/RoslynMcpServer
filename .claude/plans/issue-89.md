# Plan: Reduce SolutionAnalyzerService Coupling

## Problem Statement
`SolutionAnalyzerService` is coupled to 224 types (recommended max: 95). The class is a partial class spread across 17 files with 74 members, handling too many responsibilities.

## Progress Summary

| Phase | Status | Members Reduced |
|-------|--------|-----------------|
| Phase 1: CodeFixService | ✅ COMPLETE | 74 → 69 (-5) |
| Phase 2: DiagnosticsService | ✅ COMPLETE | 69 → 60 (-9) |
| Phase 3: SymbolSearchService | ✅ COMPLETE | 60 → 55 (-5) |
| Phase 4: Facade Pattern | 🔄 PENDING | - |

**Total Reduction: 74 → 55 members (19 methods extracted)**

## Completed Fixes

### Phase 1: Extract CodeFixService ✅
- Created `Services/CodeFixService.cs`
- Moved: `ApplyCodeFixAsync`, `BatchApplyCodeFixAsync`, `GetCodeFixProviders`, `GetFixableDiagnosticIds`, `LoadCodeFixProvidersFromAssembly`, `RunAnalyzersForCodeFixAsync`, `GetAnalyzerDiagnosticsForBatchAsync`
- Updated `SolutionAnalyzerService.CodeFix.cs` and `SolutionAnalyzerService.BatchCodeFix.cs` to delegate
- All 109 tests pass

### Phase 2: Extract DiagnosticsService ✅
- Created `Services/DiagnosticsService.cs`
- Moved: `GetDiagnosticsAsync`, `RunAnalyzersAsync`, `CreateDiagnosticEntry`, `FilterBySeverity`, `GetSuppressionInfo`, `IsGeneratedFile`, `GetMethodName`, `GetTypeName`, `IsMethodDeclaration`, `IsTypeDeclaration`
- Updated `SolutionAnalyzerService.Diagnostics.cs` to delegate
- All 109 tests pass

### Phase 3: Extract SymbolSearchService ✅
- Created `Services/SymbolSearchService.cs`
- Moved: `SearchSymbolsAsync`, `GetSymbolQualifiedNameAsync`, `ConvertToSymbolFilter`, `GetSymbolKind`, `GetSignature`
- Made `GetSymbolKind` and `GetSignature` public static for use by References.cs
- Updated `SolutionAnalyzerService.Symbols.cs` to delegate
- All 109 tests pass

## Remaining Work

### Phase 4: Keep Facade
`SolutionAnalyzerService` becomes a thin facade that:
- Owns solution/workspace management
- Delegates to extracted services
- Maintains backward compatibility for RoslynTools

This phase is essentially complete - the service already acts as a facade pattern with delegation to the extracted services.

## Current Status
Phase 3 complete. All 3 service extractions done. Consider task #4 as a review/confirmation step.

## Files Created
- `Services/CodeFixService.cs`
- `Services/DiagnosticsService.cs`
- `Services/SymbolSearchService.cs`

## Workflow Reminder (MANDATORY)
After each fix:
1. Run `roslyn_get_diagnostics` to check for errors
2. **MANDATORY: Re-read and follow CLAUDE.md**
3. **Update this plan**
4. Keep working until issue is resolved
