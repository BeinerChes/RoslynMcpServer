# CLAUDE.md - Instructions for Claude Code

> **Note:** This file is for developing RoslynMcpServer itself. Other projects use the template at `Instructions/Templates/standard.md`.

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

## IMPORTANT: Self-Referential Project

This project **uses itself** for development. There are TWO copies of hooks and skills:

| Location | Purpose | When to Edit |
|----------|---------|--------------|
| `.claude/hooks/` | **ACTIVE** - Used when developing this project | Edit here FIRST |
| `.claude/skills/` | **ACTIVE** - Used when developing this project | Edit here FIRST |
| `Instructions/Hooks/` | **TEMPLATE** - Copied to other projects via setup.ps1 | Sync FROM .claude/ |
| `Instructions/Skills/` | **TEMPLATE** - Copied to other projects via setup.ps1 | Sync FROM .claude/ |

**Workflow for modifying hooks/skills:**
1. Edit in `.claude/hooks/` or `.claude/skills/` (these are active)
2. Test the changes in this project
3. **SYNC to Instructions/** before committing:
   ```bash
   cp .claude/hooks/*.py Instructions/Hooks/
   cp -r .claude/skills/* Instructions/Skills/
   ```

## MANDATORY: Documentation Updates

**When adding or modifying tools/features, you MUST update these files:**

| Change Type | Files to Update |
|-------------|-----------------|
| New tool added | `Instructions/Topics/tools.md`, `README.md` (Available Tools table) |
| Tool modified | `Instructions/Topics/tools.md`, `README.md` if signature changed |
| New instruction topic | `Instructions/Topics/`, `Instructions.cs` (Available array) |
| Workflow changed | `Instructions/Topics/git.md` or relevant topic file |
| Hook added/modified | `.claude/hooks/` (edit), then sync to `Instructions/Hooks/` |
| Skill added/modified | `.claude/skills/` (edit), then sync to `Instructions/Skills/` |

**Note:** Templates (`CLAUDE_TEMPLATE.md`, `Instructions/Templates/*.md`) fetch instructions dynamically via `roslyn_get_instructions` - no updates needed for tool/workflow changes.

## MANDATORY: Pre-Commit Sync Check

**Before committing changes to hooks or skills, verify sync status:**

```bash
# Check if hooks are in sync
diff .claude/hooks/enforce-git-instructions.py Instructions/Hooks/enforce-git-instructions.py
diff .claude/hooks/enforce-plan-instructions.py Instructions/Hooks/enforce-plan-instructions.py
diff .claude/hooks/suggest-roslyn-for-csharp.py Instructions/Hooks/suggest-roslyn-for-csharp.py
diff .claude/hooks/suggest-roslyn-for-read.py Instructions/Hooks/suggest-roslyn-for-read.py

# If any diff shows output, sync is needed:
cp .claude/hooks/*.py Instructions/Hooks/
```

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
