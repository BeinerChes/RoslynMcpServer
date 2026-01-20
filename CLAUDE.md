# CLAUDE.md - Instructions for Claude Code

## MANDATORY: Start Every Session with Plan Instructions

**Before doing anything else**, call `roslyn_get_instructions("plan")` and follow those instructions.

## MANDATORY: Get Instructions Before Operations

**Call `roslyn_get_instructions` with the appropriate topic before each operation:**

| Before doing this... | Call with topic |
|---------------------|-----------------|
| Starting or resuming a task | `"plan"` |
| Using any Roslyn tool | `"tools"` |
| Making any code change | `"git"` |
| Modifying C# code | `"code"` |
| Writing or running tests | `"tdd"` |
| Creating a pull request | `"pre-pr"` |

## MANDATORY: Documentation Updates

**When adding or modifying tools/features, you MUST update these files:**

| Change Type | Files to Update |
|-------------|-----------------|
| New tool added | `Instructions/Topics/tools.md`, `README.md` (Available Tools table) |
| Tool modified | `Instructions/Topics/tools.md`, `README.md` if signature changed |
| New instruction topic | `Instructions/Topics/`, `Instructions.cs` (Available array) |
| Workflow changed | `Instructions/Topics/git.md` or relevant topic file |
| Hook added/modified | `Instructions/Hooks/`, `RoslynTools.SetupHooks.cs` |

**Note:** Templates (`CLAUDE_TEMPLATE.md`, `Instructions/Templates/*.md`) fetch instructions dynamically via `roslyn_get_instructions` - no updates needed for tool/workflow changes.

## MANDATORY: Real-World Testing for Tools

**When adding NEW tools or MODIFYING existing tools:**

1. Unit tests passing is NOT sufficient
2. **DO NOT push/merge** until real-world testing is complete
3. Work with code owner to test on actual solutions
4. Only after code owner confirms it works → push, PR, merge

## Project Overview

This is a **Model Context Protocol (MCP) server** written in C# (.NET 10.0) that provides C# solution analysis capabilities using Microsoft's Roslyn compiler platform.

## Build & Test

```bash
dotnet build           # Build
dotnet test            # Run tests
```

### Rebuilding After Code Changes

The MCP server runs as a background process and locks the exe. **Before rebuilding, kill it automatically:**

```bash
taskkill //F //IM RoslynMcpServer.exe
dotnet build
```

After successful build, tell the user: "Reconnect MCP with `/mcp` → reconnect roslyn"

**Do NOT ask the user to kill processes** - just do it.
