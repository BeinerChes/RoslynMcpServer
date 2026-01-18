# CLAUDE.md - C# Development with Roslyn MCP

> **What is this file?** This is a CLAUDE.md file - instructions that Claude reads automatically when you start a conversation. You don't need to run anything here. Claude will follow these instructions when working with your code.
>
> **Setup:** Copy this file to your project root as `CLAUDE.md` and update the solution path below.

## Solution Path

<!-- UPDATE THIS to your solution file path -->
**Solution file:** `C:\path\to\YourProject.sln`

Use this path for all `roslyn_*` tool calls.

## MANDATORY: Git Workflow for Code Changes

**You MUST follow this workflow for ANY code change. No exceptions.**

1. **Create GitHub issue:** `gh issue create --title "Type: description" --label "bug|enhancement"`
2. **Create branch:** `git checkout -b issues/N`
3. **Make changes** using Roslyn tools
4. **Check for errors:** `roslyn_get_diagnostics(solutionPath, severityFilter: "error")`
5. **Run tests:** `dotnet test`
6. **Commit, push, create PR, merge:**
   ```bash
   git add -A && git commit -m "Type: description - Fixes #N"
   git push -u origin issues/N
   gh pr create --base main --title "Type: description" --body "Fixes #N"
   gh pr merge --squash --delete-branch
   git checkout main && git pull
   ```

**For detailed git instructions:** Call `roslyn_get_instructions` with topic "git"

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
| Fix warnings | `roslyn_apply_code_fix` |
| Rename symbol | `roslyn_rename_symbol` |

**For full tool reference:** Call `roslyn_get_instructions` with topic "tools"

*Dead code detection may flag DTO properties used via JSON serialization (reflection-based). Properties with attributes are automatically excluded.

## Project Structure

<!-- Customize this section for your project -->
```
YourProject/
├── CLAUDE.md                 # This file
├── YourProject.sln           # Solution file
├── src/                      # Source code
└── tests/                    # Test projects
```

## Build Commands

```bash
dotnet build        # Build
dotnet test         # Run tests
```
