# CLAUDE.md - C# Development with Roslyn MCP (TDD)

## Test-Driven Development

**YOU MUST follow TDD for ALL code changes. No exceptions.**

1. Write FAILING test(s) first
2. Run tests - verify they FAIL
3. Write minimum code to make tests PASS
4. Refactor if needed (tests must still pass)

For full TDD workflow: `roslyn_get_instructions(topic: "tdd")`

## Tool Preferences

When working with C# files, prefer Roslyn MCP tools:

| Task | Use This | Not This |
|------|----------|----------|
| Find type/method | `roslyn_find_symbol` | Grep |
| Read a method | `roslyn_get_method_body` | Read entire file |
| Edit a method | `roslyn_update_method` | Edit with text patterns |
| Check errors | `roslyn_get_diagnostics` | dotnet build |

For full tool preferences: `roslyn_get_instructions(topic: "tools")`

## Workflows

- Before modifying C# code: `roslyn_get_instructions(topic: "code")`
- Before git operations: `roslyn_get_instructions(topic: "git")`
- Before creating PR: `roslyn_get_instructions(topic: "pre-pr")`
