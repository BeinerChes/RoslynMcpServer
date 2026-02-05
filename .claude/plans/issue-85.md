# Plan: Add AddType tool for creating new classes/files

GitHub Issue: https://github.com/BeinerChes/RoslynMcpServer/issues/85

## Problem Statement

Add a new `AddType` tool that creates new C# types (class, interface, struct, record, enum) with:
- Proper file placement based on project structure
- Namespace inference from project/folder
- Roslyn-based code generation for consistency

## Completed Fixes

### 1. Added `AddTypeResult` model
**File:** `src/Models.Methods.cs`

### 2. Created implementation
**File:** `src/SolutionAnalyzerService.AddType.cs`
- `AddTypeAsync` method - main implementation
- `GenerateTypeDeclaration` - creates type syntax using SyntaxFactory
- `CreateEnumDeclaration` - handles enum specifics
- Returns `BaseTypeDeclarationSyntax?` to support both TypeDeclaration and EnumDeclaration

### 3. Created tool registration
**File:** `src/RoslynTools.AddType.cs`
- Registered `AddType` tool with full parameter schema

### 4. Registered tool
**File:** `src/RoslynTools.cs`
- Added `RegisterAddTypeTool(server);` in `RegisterAll` method

### 5. Updated documentation
**Files:** `Instructions/Topics/tools.md`, `README.md`
- Added tool to Code Modification table
- Added to Tool Selection Guide
- Added "Creating a new type" workflow
- Updated "When to Use Native Tools" section

## Test Results

```
dotnet build - Build succeeded (0 errors, 0 warnings)
dotnet test  - Passed! Failed: 0, Passed: 93, Skipped: 0
```

## Current Status

**Phase: Ready for real-world testing**

Implementation complete. Per CLAUDE.md: "Unit tests passing is NOT sufficient. DO NOT push/merge until real-world testing is complete."

## Next Steps

1. [x] Add `AddTypeResult` model
2. [x] Create `SolutionAnalyzerService.AddType.cs`
3. [x] Create `RoslynTools.AddType.cs`
4. [x] Register tool in `RoslynTools.cs`
5. [x] Test build
6. [x] Update documentation (`Instructions/Topics/tools.md`, `README.md`)
7. [ ] Real-world testing with user
8. [ ] Push, PR, merge (after real-world testing)

## Parameters

| Parameter | Required | Description |
|-----------|----------|-------------|
| `solutionPath` | Yes | Path to solution file |
| `projectName` | Yes | Target project name |
| `typeName` | Yes | Name of the type to create |
| `typeKind` | No | `class`, `interface`, `struct`, `record`, `enum` (default: `class`) |
| `namespace` | No | Namespace (inferred from project + folder if not specified) |
| `folder` | No | Subfolder within project (e.g., `Services/Auth`) |
| `accessibility` | No | `public`, `internal`, `private`, `protected` (default: `public`) |
| `baseTypes` | No | Comma-separated base class/interfaces |
| `isPartial` | No | Create as partial type (default: `false`) |
| `isSealed` | No | Create as sealed (default: `false`) |
| `isStatic` | No | Create as static class (default: `false`) |

## Learnings Captured

Knowledge ID 2: "Roslyn type declaration hierarchy: EnumDeclarationSyntax vs TypeDeclarationSyntax"
- EnumDeclarationSyntax doesn't inherit from TypeDeclarationSyntax
- RecordDeclaration requires 3 parameters (kind, keyword token, identifier)
