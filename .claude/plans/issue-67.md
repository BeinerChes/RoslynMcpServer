# Plan: Fix Write and Read hook errors

## Problem Statement
The hooks for Read and Write operations on .cs files were using exit code 2 (hard block), which caused Claude Code to show "PreToolUse:Read hook error" and "PreToolUse:Write hook error" messages every time these tools were used on C# files without a valid token.

This was confusing because "error" implies something is broken, when actually the hooks were working as designed (just blocking aggressively).

## Root Cause
- `suggest-roslyn-for-read.py` used `sys.exit(2)` to block Read on .cs files
- `suggest-roslyn-for-csharp.py` used `sys.exit(2)` to block Edit/Write on .cs files
- Exit code 2 in Claude Code hooks means "hard block" which is presented as a "hook error"

## Completed Fixes

### 1. Updated suggest-roslyn-for-read.py
- Changed from `sys.exit(2)` (hard block) to `sys.exit(0)` (allow with suggestion)
- Changed output from stderr to stdout
- Updated docstring to reflect new behavior
- File: `.claude/hooks/suggest-roslyn-for-read.py`

### 2. Updated suggest-roslyn-for-csharp.py
- Changed from `sys.exit(2)` (hard block) to `sys.exit(0)` (allow with suggestion)
- Changed output from stderr to stdout
- Updated docstring to reflect new behavior
- File: `.claude/hooks/suggest-roslyn-for-csharp.py`

## Test Results
All tests pass:
- .cs files: Shows suggestion, exits 0, operation allowed
- Non-.cs files: Silent exit 0, no message
- Bypass marker: Recognized and accepted, exits 0
- With valid token: Silent exit 0

## Current Status
✅ Fix complete - hooks now show suggestions instead of blocking with errors

## Workflow Reminder (MANDATORY)
After each fix:
1. Run `GetDiagnostics` to check for errors
2. **MANDATORY: Re-read and follow CLAUDE.md**
3. **Update this plan**
4. Keep working until issue is resolved
