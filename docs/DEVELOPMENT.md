# Development

This project is self-referential — it uses itself as its own MCP server during development.

## Build and Deploy

After making code changes:

```bash
taskkill //F //IM RoslynMcpServer.exe
dotnet publish RoslynMcpServer.csproj -c Debug -o .roslyn-mcp
```

Then reconnect MCP in Claude Code: `/mcp` → reconnect roslyn.

You **must** use `dotnet publish` (not `dotnet build` + copy).

## Solution Structure

```
RoslynMcpServer.slnx
├── RoslynMcpServer/          # MCP server (entry point, tool handlers)
├── RoslynMcpServer.Graph/    # SQLite graph database, knowledge base
├── RoslynMcpServer.Tests/    # Tests
├── Instructions/
│   ├── Topics/               # GetInstructions content (tools, git, plan)
│   ├── Hooks/                # Hook scripts shipped to users via --enable-hooks
│   ├── Skills/               # Skills shipped to users via --enable-skills
│   └── Templates/            # CLAUDE.md template for --init
├── .claude/hooks/            # Active hooks for this project
├── .claude/skills/           # Active skills for this project
└── docs/tools/               # Per-tool documentation
```

## Running Tests

```bash
dotnet test RoslynMcpServer.Tests
```
