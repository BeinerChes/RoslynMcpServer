# CLAUDE.md - C# Development with Roslyn MCP

> **What is this file?** Instructions that Claude reads automatically when you start a conversation.

## MANDATORY: Start Every Session with Plan Instructions

**Before doing anything else:**
1. Find the solution file: `glob pattern "*.sln*"`
2. Call `roslyn_get_instructions(topic: "plan")` and follow those instructions.

**Note:** The `solutionPath` parameter is REQUIRED for `plan`, `git`, and `tools` topics to generate per-solution tokens for hook validation.

## MANDATORY: Get Instructions Before Operations

**Call `roslyn_get_instructions` with the appropriate topic before each operation:**

| Before doing this... | Call with topic |
|---------------------|-----------------|
| Starting or resuming a task | `"plan"` |
| Using any Roslyn tool | `"tools"` |
| Making any code change | `"git"` |
| Modifying C# code | `"code"` |
| Writing or running tests | `"tdd"` |
| Creating a pull request | `"pre-pr"` |

## Plan Files

Plans are stored **per-solution** in `.claude/plans/`:
- Create plan files for non-trivial tasks
- Links to GitHub issues: `.claude/plans/issue-<number>.md`
- Tracks work across sessions, prevents context loss

## Build Commands

```bash
dotnet build        # Build
dotnet test         # Run tests
```
