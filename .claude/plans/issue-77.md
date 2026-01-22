# Plan: Add roslyn_extract_method tool

## Problem Statement
Implement `roslyn_extract_method` tool that extracts a code block into a new method using Roslyn's data flow analysis APIs.

## Completed Fixes

### 1. Created SolutionAnalyzerService.ExtractMethod.cs
- `ExtractMethodAsync` method using Roslyn's data flow analysis
- Automatically detects input variables (parameters) and output variables (returns)
- Supports void, single return, and tuple returns
- Handles async methods
- Determines static/instance based on usage

### 2. Created RoslynTools.ExtractMethod.cs
- Tool registration with proper schema
- Parameters: solutionPath, filePath, startLine, endLine, methodName, accessibility

### 3. Created ExtractMethodTests.cs
- 8 unit tests covering:
  - Simple void extraction
  - Input variables → parameters
  - Output variables → return values
  - Error cases (outside method, invalid range, missing name, file not found)
  - Accessibility modifier

### 4. Updated documentation
- README.md: Added to Available Tools table, removed from Roadmap
- tools.md: Added to Code Modification section, added workflow example

## Test Results
All 88 tests pass (80 original + 8 new)

## Current Status
COMPLETED - Ready for commit and PR

## Next Steps
1. Commit changes
2. Create PR
3. Real-world testing before merge
