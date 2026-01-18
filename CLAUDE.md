# CLAUDE.md - Instructions for Claude Code

## Solution Path

**Solution file:** `C:\Users\beine\source\repos\RoslynMcpServer\RoslynMcpServer.slnx`

Use this path for all `roslyn_*` tool calls.

## MANDATORY: Git Workflow for ALL Code Changes

**You MUST follow this workflow for ANY code change. No exceptions.**

### Before writing ANY code:
1. **Create GitHub issue:** `gh issue create --title "Type: description" --label "bug|enhancement|documentation"`
2. **Create branch:** `git checkout -b issues/N` (where N is issue number)

### After making changes:
3. **Check for errors:** `roslyn_get_diagnostics(solutionPath, severityFilter: "error")`
4. **Run tests:** `dotnet test` - all must pass
5. **Commit:** `git add -A && git commit -m "Type: description\n\nFixes #N\n\nCo-Authored-By: Claude <noreply@anthropic.com>"`
6. **Push and create PR:** `git push -u origin issues/N && gh pr create --base rc/1.0.1 --title "Type: description" --body "Fixes #N"`
7. **Merge and cleanup:** `gh pr merge --squash --delete-branch && git checkout rc/1.0.1 && git pull`

**For detailed instructions:** Call `roslyn_get_instructions` with topic "git"

## Tool Preferences

Use Roslyn MCP tools for C# files:

| Task | Use This |
|------|----------|
| Find type/method | `roslyn_find_symbol` |
| See class structure | `roslyn_get_type_members` |
| Read a method | `roslyn_get_method_body` |
| Edit a method | `roslyn_update_method` |
| Add new member | `roslyn_add_member` |
| Find references | `roslyn_get_references` |
| Find callers | `roslyn_get_callers` |
| Check errors | `roslyn_get_diagnostics` |
| Fix warnings | `roslyn_apply_code_fix` or `roslyn_batch_apply_code_fixes` |
| Rename symbol | `roslyn_rename_symbol` |

**For full tool reference:** Call `roslyn_get_instructions` with topic "tools"

## Project Overview

This is a **Model Context Protocol (MCP) server** written in C# (.NET 10.0) that provides C# solution analysis capabilities using Microsoft's Roslyn compiler platform.

## Project-Specific Rules

### Maintain Instruction Files

When adding new tools, **YOU MUST update `Instructions/Topics/tools.md`** and `CLAUDE_TEMPLATE.md`.

### Code Guidelines

- Keep .cs files under 300 lines
- Use C# 12 features (primary constructors, collection expressions)
- Use `async/await` for all I/O operations
- All logging goes to stderr (`Console.Error.WriteLine`)

### Test Requirements

- Every code change MUST have unit tests
- Follow TDD: write failing tests BEFORE implementation

## Build & Test

```bash
dotnet build           # Build
dotnet test            # Run tests
```

### Rebuilding After Code Changes

The MCP server runs as a background process. To rebuild:
1. Kill running process: `taskkill //F //PID <pid>`
2. Rebuild: `dotnet build`
3. Reconnect: `/mcp` → reconnect roslyn

## Project Structure

```
RoslynMcpServer/
├── RoslynMcpServer.slnx      # Solution file (use this path!)
├── CLAUDE.md                 # This file
├── Instructions/             # Instruction files (copied to output)
│   ├── Templates/            # CLAUDE.md templates for users
│   └── Topics/               # Topic-specific instructions
├── src/                      # Source code
├── RoslynMcpServer.Graph/    # Call graph database project
└── RoslynMcpServer.Tests/    # xUnit tests
```
