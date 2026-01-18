# CLAUDE.md - C# Development with Roslyn MCP

## Tool Preferences

When working with C# files in .NET solutions, prefer Roslyn MCP tools over native tools:

| Task | Use This | Not This |
|------|----------|----------|
| Find type/method | `roslyn_find_symbol` | Grep |
| Read a method | `roslyn_get_method_body` | Read entire file |
| Edit a method | `roslyn_update_method` | Edit with text patterns |
| Find references | `roslyn_get_references` | Grep for text |
| Check errors | `roslyn_get_diagnostics` | dotnet build |

For full tool preferences: `roslyn_get_instructions(topic: "tools")`

## Workflows

- Before modifying C# code: `roslyn_get_instructions(topic: "code")`
- Before git operations: `roslyn_get_instructions(topic: "git")`
- Before creating PR: `roslyn_get_instructions(topic: "pre-pr")`
