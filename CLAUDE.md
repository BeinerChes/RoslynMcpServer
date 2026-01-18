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

## MANDATORY: Documentation Updates

**When adding or modifying tools/features, you MUST update these files:**

| Change Type | Files to Update |
|-------------|-----------------|
| New tool added | `Instructions/Topics/tools.md`, `README.md` (Available Tools), `CLAUDE_TEMPLATE.md` |
| Tool modified | `Instructions/Topics/tools.md`, `README.md` if signature changed |
| New instruction topic | `Instructions/Topics/`, `Instructions.cs` (Available array) |
| Workflow changed | `Instructions/Topics/git.md` or `pre-pr.md`, templates if affected |

**Check before PR:** Are all relevant docs updated?

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
| Find callers (cached) | `roslyn_query_graph` (auto-refreshes stale files) |
| Impact analysis | `roslyn_graph_impact` (auto-refreshes stale files) |
| Find dead code | `roslyn_find_dead_code` (may have false positives*) |
| Check errors | `roslyn_get_diagnostics` |
| Fix warnings | `roslyn_apply_code_fix` or `roslyn_batch_apply_code_fixes` |
| Rename symbol | `roslyn_rename_symbol` |

*Dead code detection may flag DTO properties used via JSON serialization (reflection-based).

**For full tool reference:** Call `roslyn_get_instructions` with topic "tools"

## Project Overview

This is a **Model Context Protocol (MCP) server** written in C# (.NET 10.0) that provides C# solution analysis capabilities using Microsoft's Roslyn compiler platform.

## Code Guidelines

- Keep .cs files under 300 lines
- Use C# 12 features (primary constructors, collection expressions)
- Use `async/await` for all I/O operations
- All logging goes to stderr (`Console.Error.WriteLine`)
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
