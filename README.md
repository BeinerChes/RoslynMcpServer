# Roslyn MCP Server

An MCP server that gives Claude Code semantic understanding of C# code. Instead of treating `.cs` files as text, it uses the Roslyn compiler to parse, navigate, and modify code the way an IDE does — finding symbols by meaning, renaming across a solution, extracting methods with automatic parameter detection, and applying code fixes from diagnostics.

## What It Does

**Without Roslyn MCP**, Claude Code reads `.cs` files as text and edits them with string replacement. It searches for symbols with `grep`, which can't distinguish a method call from a comment. Renaming a method means finding every file that mentions it and hoping you don't miss one.

**With Roslyn MCP**, Claude Code works with the compiler's understanding of your code. It sees types, methods, references, and diagnostics — the same information an IDE uses. A rename updates every reference across the entire solution in one call. A method extraction automatically detects which variables flow in and out. Dead code detection finds methods with zero callers, not just methods whose names don't appear in grep results.

## Getting Started

### Prerequisites

- .NET 10 SDK
- Claude Code CLI
- Windows

### Build from Source

```bash
git clone https://github.com/BeinerChes/RoslynMcpServer
cd RoslynMcpServer
dotnet publish src/RoslynMcpServer/RoslynMcpServer.csproj -c Debug -o .roslyn-mcp
```

### Deploy to Your Solution

The server runs **per-solution** — each solution you work on gets its own `.roslyn-mcp` folder containing the server binaries.

```bash
# 1. Copy the published server into your solution's root
xcopy /E /I .roslyn-mcp  D:\path\to\YourSolution\.roslyn-mcp

# 2. From your solution directory, run init
cd D:\path\to\YourSolution
.roslyn-mcp\RoslynMcpServer.exe --init
```

`--init` does the following:
- Creates `.mcp.json` pointing to the server
- Creates `CLAUDE.md` from a template (tells Claude to use Roslyn tools)
- Adds `.roslyn-mcp/` to `.gitignore`
- Optionally enables hooks and skills (interactive prompts)

You can also enable hooks and skills separately:

```bash
.roslyn-mcp\RoslynMcpServer.exe --enable-hooks    # Suggests Roslyn tools for .cs files
.roslyn-mcp\RoslynMcpServer.exe --enable-skills    # Installs available skills
```

### Verify

Start Claude Code in your solution directory and run:

```
GetServerInfo()
```

Returns version and capabilities. If this works, the server is running and your solution is loaded.

### What Lives in .roslyn-mcp

```
YourSolution/
├── .roslyn-mcp/
│   ├── RoslynMcpServer.dll          # Server binary
│   ├── hooks/                       # RAG memory hooks + server
│   │   ├── .venv/                   # Python venv (auto-created)
│   │   ├── rag_server.py            # FastAPI server (embedding + search)
│   │   ├── rag_mcp.py              # MCP server (tools for Claude)
│   │   ├── rag.py                   # UserPromptSubmit hook
│   │   ├── rag_stop.py             # Stop hook (saves Claude responses)
│   │   ├── rag_post_tool.py        # PostToolUse hook (saves edit diffs)
│   │   └── rag.db                   # RAG memory database (SQLite)
│   ├── logs/                        # Tool call logs
│   └── reports/                     # Usage reports
├── YourSolution.slnx
├── .mcp.json
└── ...
```

Everything is local to the solution.

## Tool Highlights

The server provides **24 tools** across 6 categories. Here are the ones that matter most:

### Reading Code

**GetMethodBody** — Read a method's source by type and method name. No need to find which file it's in, no need to read the whole file.

```
GetMethodBody(typeName: "UserService", methodName: "GetById")
```

**FindSymbol** — Find any symbol (type, method, property) by name. Semantic search, not text matching.

```
FindSymbol(pattern: "Authenticate", symbolKind: "member")
```

### Modifying Code

**UpdateMethod** — Replace or edit a method's implementation. Targeted edits within a method using `oldText`/`newText`, or full replacement with `newSourceCode`.

```
UpdateMethod(typeName: "Calculator", methodName: "Add", oldText: "a + b", newText: "checked(a + b)")
```

**AddMember** — Add a method, property, or field to a class.

```
AddMember(typeName: "TaskBoard", memberCode: "public TaskItem GetTask(int id)")
```

**RenameSymbol** — Rename anything across the entire solution. All references updated atomically.

### Analysis

**GetDiagnostics** — Compile the solution and get errors/warnings. `BatchApplyCodeFixes` can auto-fix entire categories at once.

**FindDeadCode** — Two-phase dead code detection: fast graph database scan, then Roslyn validation to eliminate false positives.

For the complete tool reference, see [docs/tools/](docs/tools/).

## Hooks and Enforcement

The project includes Claude Code [hooks](https://docs.anthropic.com/en/docs/claude-code/hooks) that enforce Roslyn tool usage over native text operations. Hooks are Python scripts in `.claude/hooks/` that run before tool calls and block them if conditions aren't met.

### Why Hooks?

Without enforcement, Claude Code defaults to what it knows — `Read`, `Edit`, `Write`. These work, but they treat C# as text. The hooks ensure Claude uses the semantic tools instead, which produces better results.

### Included Hooks

| Hook | Blocks | Unless |
|------|--------|--------|
| `suggest-roslyn-for-read.py` | `Read` on `.cs` files | Valid tools token exists |
| `suggest-roslyn-for-csharp.py` | `Edit`/`Write` on `.cs` files | Valid tools token exists |
| `enforce-git-instructions.py` | `git commit`/`push` | Valid git token exists |
| `enforce-plan-instructions.py` | `gh issue`/`pr create` | Valid plan token exists |
| `enforce-branch-naming.py` | `git commit` | Branch matches `issues/*`, `rc/*`, `docs/*`, or `main` |

### Token System

The `.cs` file hooks use a token bypass system. When Claude genuinely needs native file access (syntax errors, comments, non-code edits), it calls `GetInstructions(topic: "tools")` which generates a time-limited token (1 minute). The hook validates the token before allowing the operation.

The git and plan hooks work the same way with their respective topics. This ensures Claude reads the project's git workflow or planning instructions before committing or creating issues.

Configure which hooks are active in `.claude/settings.json`:

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Read",
        "hooks": [{ "type": "command", "command": "python .claude/hooks/suggest-roslyn-for-read.py" }]
      },
      {
        "matcher": "Edit",
        "hooks": [{ "type": "command", "command": "python .claude/hooks/suggest-roslyn-for-csharp.py" }]
      },
      {
        "matcher": "Write",
        "hooks": [{ "type": "command", "command": "python .claude/hooks/suggest-roslyn-for-csharp.py" }]
      }
    ]
  }
}
```

### Adapting Hooks to Your Project

The hooks here enforce this project's workflow. For your own project, you might:

- Use `--enable-hooks` as-is (installs the `.cs` file hooks that enforce Roslyn tool usage)
- Add the git/plan/branch hooks manually if you want workflow enforcement
- Modify the branch naming pattern in `enforce-branch-naming.py`
- Write your own hooks for other conventions

## RAG Memory (Cross-Session Context)

Claude Code has no memory between sessions. RAG Memory fixes this — it automatically saves every prompt, response, and code edit, then surfaces relevant past work when you start a new session.

### How It Works

1. **You type a prompt** → hook saves it to `rag.db`, searches for similar past entries, injects matches into Claude's context
2. **Claude responds** → hook saves the response
3. **Claude edits code** → hook saves the diff
4. **Next session** → relevant past work automatically appears before Claude processes your prompt

Uses [all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2) for embeddings, hybrid search (vector + FTS5 with Reciprocal Rank Fusion), and sentence-boundary chunking for long texts.

### Setup

```bash
# 1. Install uv (if not already installed)
pip install uv

# 2. Create venv and install dependencies
cd .roslyn-mcp/hooks
uv venv .venv
uv pip install --python .venv/Scripts/python.exe sentence-transformers fastapi uvicorn numpy mcp anyio
```

The server starts automatically on first prompt (cold start ~5s, then ~20ms per call). A separate console window shows live logs.

### MCP Tools

RAG also exposes MCP tools so Claude can fetch full context from memory:

| Tool | Description |
|------|-------------|
| `rag_get(entry_id, max_tokens)` | Fetch full content of a memory entry |
| `rag_search(query, top_k)` | Search memory manually |
| `rag_add(content, role)` | Save a note to memory |

Register in `.mcp.json`:

```json
{
  "rag": {
    "type": "stdio",
    "command": ".roslyn-mcp/hooks/.venv/Scripts/python.exe",
    "args": [".roslyn-mcp/hooks/rag_mcp.py"]
  }
}
```

### Hook Configuration

Add to `.claude/settings.local.json`:

```json
{
  "hooks": {
    "UserPromptSubmit": [
      {
        "matcher": "",
        "hooks": [{ "type": "command", "command": ".roslyn-mcp/hooks/.venv/Scripts/python.exe .roslyn-mcp/hooks/rag.py" }]
      }
    ],
    "Stop": [
      {
        "matcher": "",
        "hooks": [{ "type": "command", "command": ".roslyn-mcp/hooks/.venv/Scripts/python.exe .roslyn-mcp/hooks/rag_stop.py" }]
      }
    ],
    "PostToolUse": [
      {
        "matcher": "Edit",
        "hooks": [{ "type": "command", "command": ".roslyn-mcp/hooks/.venv/Scripts/python.exe .roslyn-mcp/hooks/rag_post_tool.py" }]
      }
    ]
  }
}
```

## Skills

[Skills](https://docs.anthropic.com/en/docs/claude-code/skills) are reusable prompts stored in `.claude/skills/` that Claude Code can invoke with slash commands. They combine multiple tools into higher-level workflows.

### Included Skills

| Skill | Invoke | What It Does |
|-------|--------|-------------|
| `/doc-tool` | `/doc-tool UpdateMethod` | Documents a Roslyn tool: tests native vs Roslyn approach, writes comparison doc, updates README. |
| `/usage-report` | `/usage-report [hours]` | Generates tool usage analytics — call counts, success rates, performance metrics. |

### Writing Your Own Skills

A skill is a markdown file at `.claude/skills/<name>/SKILL.md` with YAML frontmatter:

```yaml
---
name: my-skill
description: What it does (shown in skill list)
user-invocable: true
arguments: "<arg description>"
---

# Skill instructions here
Use $ARGUMENTS to access arguments passed by the user.
```

## GetInstructions and Custom Best Practices

`GetInstructions(topic)` returns topic-specific instructions to Claude and generates a hook bypass token. The topics and their content live in `Instructions/Topics/` as markdown files.

### Built-in Topics

| Topic | File | Purpose |
|-------|------|---------|
| `tools` | `Instructions/Topics/tools.md` | Complete tool reference — when to use each Roslyn tool, gotchas, auto-generation workflow |
| `git` | `Instructions/Topics/git.md` | Git workflow — branch naming, commit types, PR process |
| `plan` | `Instructions/Topics/plan.md` | Session management — when to create issues, use plan files, write to knowledge base |

### Example: Git Workflow (tools topic: `git`)

The included `git.md` enforces a specific workflow: create a GitHub issue first, branch as `issues/<number>`, use conventional commit types (`Fix`, `Feature`, `Refactor`, `Docs`, `Test`), squash-merge PRs. The `enforce-branch-naming.py` hook blocks commits on branches that don't match the pattern.

This is one team's convention. Your project might use `feature/` branches and regular merges — just edit the markdown file.

### Example: Session Management (topic: `plan`)

The included `plan.md` defines when Claude should create GitHub issues and write plan files. It includes triggers like "tried the same fix twice? stop and write it down" and decision trees for choosing the right persistence tool.

### Customizing for Your Project

1. Edit the markdown files in `Instructions/Topics/` with your conventions
2. The hook + token system ensures Claude reads your instructions before acting
3. Add new topics by creating new `.md` files and registering them in the server

## Watson's Chronicles

The project keeps a [development blog](docs/blog/) written by Watson (a Haiku-class AI) in the style of Dr. Watson documenting Sherlock Holmes' cases. Each post chronicles a debugging session or feature development from the perspective of a faithful assistant observing Detective Claude Code at work. Entirely optional, entirely fun.

## Development

This project is self-referential — it uses itself as its own MCP server during development. See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for build instructions, deployment, and contributing guidelines.
