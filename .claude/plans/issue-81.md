# Plan: Add .claude and .roslyn-mcp to .gitignore automatically

## Problem Statement
When users install RoslynMcpServer in a new solution, the `.claude` and `.roslyn-mcp` folders should be automatically added to the project's `.gitignore`. Currently, `SetupHooks` creates these folders but doesn't update `.gitignore`, potentially causing users to accidentally commit local Claude configuration.

## Completed Fixes

### 1. Added `UpdateGitignore` helper method
**File:** `src/RoslynTools.AddMember.cs` (added via AddMember)

New private static method that:
- Creates `.gitignore` with entries if it doesn't exist
- Appends entries if file exists but entries are missing
- Skips if entries already present (with or without trailing slash)

### 2. Updated `SetupHooksInProject` method
**File:** `src/RoslynTools.SetupHooks.cs`

- Added call to `UpdateGitignore` after creating directories
- Added `gitignore` property to return object with action and path
- Updated `filesCreated` list to include gitignore when created/updated

### 3. Added unit tests
**File:** `RoslynMcpServer.Tests/Services/SetupHooksTests.cs`

5 tests covering:
- Creating new .gitignore
- Appending to existing .gitignore
- Skipping when entries already present
- Adding only missing entries
- Recognizing entries without trailing slash

## Test Results
All 93 tests pass (88 original + 5 new)

## Current Status
Implementation complete. Ready for commit.
