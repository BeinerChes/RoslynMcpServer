# CLAUDE.md - C# Development with Roslyn MCP (TDD)

> **What is this file?** This is a CLAUDE.md file - instructions that Claude reads automatically when you start a conversation. You don't need to run anything here. Claude will follow these instructions when working with your code.

## Instructions for Claude

### Test-Driven Development (REQUIRED)

**You MUST follow TDD for ALL code changes in this project. No exceptions.**

1. Write FAILING test(s) first
2. Run tests - verify they FAIL
3. Write minimum code to make tests PASS
4. Refactor if needed (tests must still pass)

For the complete TDD workflow, call `roslyn_get_instructions` with topic "tdd".

### Tool Preferences

When working with C# files, use Roslyn MCP tools instead of native tools:

| Task | Use This Tool | Instead Of |
|------|---------------|------------|
| Find a type or method | `roslyn_find_symbol` | Grep |
| See class structure | `roslyn_get_type_members` | Read entire file |
| Read a method's code | `roslyn_get_method_body` | Read entire file |
| Edit a method | `roslyn_update_method` | Edit with text patterns |
| Check for errors | `roslyn_get_diagnostics` | dotnet build |

For the complete tool reference, call `roslyn_get_instructions` with topic "tools".

### Workflows

Before performing these tasks, get the detailed instructions:

- **Modifying C# code**: Call `roslyn_get_instructions` with topic "code"
- **Git operations**: Call `roslyn_get_instructions` with topic "git"
- **Creating a PR**: Call `roslyn_get_instructions` with topic "pre-pr"
