# Plan: Add ExtractMethod tool

## Problem Statement
Implement `ExtractMethod` tool that extracts a code block into a new method using Roslyn's data flow analysis APIs.

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
COMPLETED - Formatting fix applied

## Fixes Applied
1. Added `Formatter.Format()` call before writing file to ensure proper code formatting
2. All 8 tests pass

## Next Steps
1. Amend commit with formatting fix
2. Update PR
3. Real-world testing (reconnect MCP with `/mcp` → reconnect roslyn)
