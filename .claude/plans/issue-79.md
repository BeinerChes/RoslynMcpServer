# Plan: Make Read/Edit hooks use tokens for hard blocking

## Problem Statement
Read/Edit hooks for C# files are soft suggestions (exit 0). Need to make them hard blocks (exit 2) like git/plan hooks.

## Implementation

### Changes needed:
1. Rename hooks to reflect enforcement behavior
2. Update hook code to exit with code 2 (block) when no valid token
3. Update RoslynTools.SetupHooks.cs with new filenames
4. Update README.md hook enforcement table

### Token already works:
- `GetInstructions("tools")` already writes `roslyn-tools-token-{hash}`
- Hooks already check for this token
- Just need to change exit code from 0 to 2

## Completed Changes

1. Updated `Instructions/Hooks/suggest-roslyn-for-read.py`:
   - Changed from soft suggestion (exit 0) to hard block (exit 2)
   - Added HTTP validation like git hooks
   - Added local fallback validation

2. Updated `Instructions/Hooks/suggest-roslyn-for-csharp.py`:
   - Changed from soft suggestion (exit 0) to hard block (exit 2)
   - Added HTTP validation like git hooks
   - Removed bypass marker support (no longer needed)

3. Updated `README.md`:
   - Changed hook table from "Soft suggestion" to "BLOCKED - requires valid tools token"

4. Updated `src/RoslynTools.SetupHooks.cs`:
   - Updated instruction messages to reflect blocking behavior

## Test Results
- Build: SUCCESS
- Tests: 88/88 passed

## Current Status
COMPLETED - Ready for commit and PR
