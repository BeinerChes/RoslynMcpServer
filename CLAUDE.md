# CLAUDE.md - Instructions for Claude Code

## Project Overview

This is a **Model Context Protocol (MCP) server** written in C# (.NET 10.0) that provides C# solution analysis capabilities using Microsoft's Roslyn compiler platform.

The server enables AI assistants to analyze .NET solutions with deep semantic understanding - finding symbols, tracking references, understanding call graphs, and more.

## Development Guidelines

For C# code modifications: `roslyn_get_instructions(topic: "code")`
For git workflow: `roslyn_get_instructions(topic: "git")`
For TDD workflow: `roslyn_get_instructions(topic: "tdd")`
Before creating PR: `roslyn_get_instructions(topic: "pre-pr")`
For all available tools: `roslyn_get_instructions(topic: "tools")`

## Project-Specific Rules

### IMPORTANT: Maintain Instruction Files

When adding new tools, **YOU MUST update `Instructions/Topics/tools.md`**.

Also update `CLAUDE_TEMPLATE.md` if the tool is user-facing.

### Code Guidelines

- Keep .cs files under 300 lines (extract helper classes if needed)
- Use C# 12 features (primary constructors, collection expressions)
- Use `async/await` for all I/O operations
- All logging goes to stderr (`Console.Error.WriteLine`)

### Test Requirements

- Every code change MUST have an associated GitHub issue
- Every feature/fix MUST have unit tests with issue reference comment
- Follow TDD: write failing tests BEFORE implementation

## Tech Stack

- **.NET 10.0** - Target framework
- **Microsoft.CodeAnalysis** - Roslyn code analysis
- **Microsoft.Build.Locator** - MSBuild discovery
- **xUnit** - Unit testing
- **SQLite** (via RoslynMcpServer.Graph) - Call graph caching

## Project Structure

```
RoslynMcpServer/
├── CLAUDE.md                 # This file
├── Instructions/             # External instruction files (copied to output)
│   ├── Templates/            # CLAUDE.md templates for users
│   └── Topics/               # Topic-specific instructions
├── src/
│   ├── McpServer.cs          # MCP protocol implementation
│   ├── RoslynTools*.cs       # Tool registrations (partial classes)
│   ├── SolutionAnalyzerService*.cs  # Roslyn analysis (partial classes)
│   ├── Instructions.cs       # Reads instruction files
│   └── Models*.cs            # DTOs
├── RoslynMcpServer.Graph/    # Call graph database project
└── RoslynMcpServer.Tests/    # xUnit tests
```

## Build & Test

```bash
dotnet build           # Build
dotnet test            # Run tests
dotnet run             # Run MCP server
```

### Rebuilding After Code Changes

The MCP server runs as a background process:

1. Kill running process: `taskkill //F //PID <pid>`
2. Rebuild: `dotnet build`
3. Reconnect in Claude Code: `/mcp` → reconnect roslyn

## Git Workflow

**Default branch:** `rc/1.0.1` (all PRs target here)

For full workflow details: `roslyn_get_instructions(topic: "git")`

Quick reference:
1. Create issue: `gh issue create --title "..." --label "enhancement"`
2. Create branch: `git checkout -b issues/N`
3. Make changes with TDD
4. Verify: `roslyn_get_diagnostics(severityFilter: "error")`
5. Commit, push, create PR

## Architecture Notes

### MCP Protocol
- Uses stdio transport (stdin/stdout)
- JSON-RPC 2.0 format
- All tools prefixed with `roslyn_`

### Adding a New Tool

1. Create `RoslynTools.YourTool.cs` with `RegisterYourToolTool(McpServer server)`
2. Call it from `RegisterAll()` in `RoslynTools.cs`
3. Add service method to `SolutionAnalyzerService.YourTool.cs` if needed
4. Update `Instructions/Topics/tools.md`
5. Add tests in `RoslynMcpServer.Tests/`

## Roadmap

### Implemented
- Symbol search, references, callers, implementations
- Type members, method body read/update
- Diagnostics with code fixes (single and batch)
- Rename symbol
- Call graph database with file change detection
- Instruction templates and dynamic instructions

### Planned
- `roslyn_extract_method` - Extract code block into new method
- `roslyn_change_signature` - Add/remove/reorder parameters
- `roslyn_organize_usings` - Sort + remove unused
- `roslyn_format_document` - Apply .editorconfig formatting
