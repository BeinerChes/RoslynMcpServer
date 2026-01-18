# CLAUDE.md - C# Development with Roslyn MCP (Team Workflow)

> **What is this file?** This is a CLAUDE.md file - instructions that Claude reads automatically. You don't run anything here.

## Solution Path

<!-- UPDATE THIS to your solution file path -->
**Solution file:** `C:\path\to\YourProject.sln`

Use this path for all `roslyn_*` tool calls.

## MANDATORY: Git Workflow for ALL Code Changes

**You MUST follow this workflow for ANY code change. No exceptions.**

### Before writing ANY code:
1. **Create GitHub issue:** `gh issue create --title "Type: description" --label "bug|enhancement"`
2. **Create branch:** `git checkout -b issues/N`

### Development (with TDD):
3. **Write FAILING tests first**
4. **Implement code** to make tests pass
5. **Check for errors:** `roslyn_get_diagnostics(solutionPath, severityFilter: "error")`
6. **Run tests:** `dotnet test` - all must pass

### After changes complete:
7. **Commit:** `git add -A && git commit -m "Type: description - Fixes #N"`
8. **Push:** `git push -u origin issues/N`
9. **Create PR:** `gh pr create --base main --title "Type: description" --body "Fixes #N"`
10. **Merge:** `gh pr merge --squash --delete-branch`
11. **Update local:** `git checkout main && git pull`

**For detailed git instructions:** Call `roslyn_get_instructions` with topic "git"

## MANDATORY: Test-Driven Development

**You MUST follow TDD for ALL code changes.**

1. Write FAILING test(s) first
2. Run tests - verify they FAIL
3. Write minimum code to make tests PASS
4. Refactor if needed (tests must still pass)

**For detailed TDD instructions:** Call `roslyn_get_instructions` with topic "tdd"

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
| Fix warnings | `roslyn_apply_code_fix` |
| Rename symbol | `roslyn_rename_symbol` |

**For full tool reference:** Call `roslyn_get_instructions` with topic "tools"
