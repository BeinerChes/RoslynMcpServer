# CLAUDE.md - C# Development with Roslyn MCP

> **What is this file?** Instructions that Claude reads automatically when you start a conversation.

## MANDATORY: Get Instructions Before Operations

**Call `roslyn_get_instructions` with the appropriate topic before each operation:**

| Before doing this... | Call with topic |
|---------------------|-----------------|
| Using any Roslyn tool | `"tools"` |
| Making any code change | `"git"` |
| Modifying C# code | `"code"` |
| Creating a pull request | `"pre-pr"` |

## Build Commands

```bash
dotnet build        # Build
dotnet test         # Run tests
```
