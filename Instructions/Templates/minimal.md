# CLAUDE.md - C# Development with Roslyn MCP

> **What is this file?** This is a CLAUDE.md file - instructions that Claude reads automatically. You don't run anything here.

## Solution Path

<!-- UPDATE THIS to your solution file path -->
**Solution file:** `C:\path\to\YourProject.sln`

Use this path for all `roslyn_*` tool calls.

## Tool Preferences

Use Roslyn MCP tools for C# files:

| Task | Use This |
|------|----------|
| Find type/method | `roslyn_find_symbol` |
| Read a method | `roslyn_get_method_body` |
| Edit a method | `roslyn_update_method` |
| Find references | `roslyn_get_references` |
| Check errors | `roslyn_get_diagnostics` |

**For full tool reference:** Call `roslyn_get_instructions` with topic "tools"

**For code modification guidelines:** Call `roslyn_get_instructions` with topic "code"
