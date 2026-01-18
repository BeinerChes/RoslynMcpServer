# CLAUDE.md - C# Development with Roslyn MCP

> **What is this file?** This is a CLAUDE.md file - instructions that Claude reads automatically when you start a conversation. You don't need to run anything here. Claude will follow these instructions when working with your code.

## Instructions for Claude

When working with C# code in this project, you have access to the Roslyn MCP server. Use these tools instead of native tools (Grep, Read, Edit) for C# files:

| Task | Use This Tool |
|------|---------------|
| Find a type or method | `roslyn_find_symbol` |
| Read a method's code | `roslyn_get_method_body` |
| Edit a method | `roslyn_update_method` |
| Find all references | `roslyn_get_references` |
| Check for errors | `roslyn_get_diagnostics` |

For detailed guidance on any topic, call the instruction tools:
- Code modification best practices: call `roslyn_get_instructions` with topic "code"
- Git workflow: call `roslyn_get_instructions` with topic "git"
- Before creating a PR: call `roslyn_get_instructions` with topic "pre-pr"
- Full tool reference: call `roslyn_get_instructions` with topic "tools"
