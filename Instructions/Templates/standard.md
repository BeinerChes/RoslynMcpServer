# CLAUDE.md - C# Development with Roslyn MCP

> **What is this file?** This is a CLAUDE.md file - instructions that Claude reads automatically when you start a conversation. You don't need to run anything here. Claude will follow these instructions when working with your code.

## Instructions for Claude

### Tool Preferences

When working with C# files in this project, use Roslyn MCP tools instead of native tools:

| Task | Use This Tool | Instead Of |
|------|---------------|------------|
| Find a type or method | `roslyn_find_symbol` | Grep |
| See class structure | `roslyn_get_type_members` | Read entire file |
| Read a method's code | `roslyn_get_method_body` | Read entire file |
| Edit a method | `roslyn_update_method` | Edit with text patterns |
| Add a new member | `roslyn_add_member` | Edit to insert code |
| Find all references | `roslyn_get_references` | Grep for text |
| Find who calls a method | `roslyn_get_callers` | roslyn_get_references |
| Check for errors | `roslyn_get_diagnostics` | dotnet build |
| Fix a warning | `roslyn_apply_code_fix` | Manual edit |
| Rename a symbol | `roslyn_rename_symbol` | Find/replace |

For the complete tool reference, call `roslyn_get_instructions` with topic "tools".

### Workflows

Before performing these tasks, get the detailed instructions:

- **Modifying C# code**: Call `roslyn_get_instructions` with topic "code"
- **Git operations**: Call `roslyn_get_instructions` with topic "git"
- **Creating a PR**: Call `roslyn_get_instructions` with topic "pre-pr"
