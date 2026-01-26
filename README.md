# Roslyn MCP Server

Give Claude Code the same understanding of C# that your IDE has.

```
You: Find all usages of UserService.Save()
Claude (without Roslyn): *searches text, finds 50 matches including comments*
Claude (with Roslyn): *finds exactly 3 call sites*
```

## Quick Start

### 1. Install

**From Release (Recommended):**
```powershell
# Download latest from https://github.com/BeinerChes/RoslynMcpServer/releases
# Extract and run:
powershell -ExecutionPolicy Bypass -File install.ps1
```

**From Source:**
```bash
git clone https://github.com/BeinerChes/RoslynMcpServer.git
cd RoslynMcpServer
dotnet build
```

### 2. Set Up Your Project

```powershell
cd C:\path\to\your\solution
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\RoslynMcpServer\setup.ps1"
```

### 3. Verify

```bash
claude
# Type: /mcp
# You should see "roslyn" listed
```

**That's it.** Claude will now use Roslyn tools automatically.

---

## What Can It Do?

| Instead of... | Claude now uses... | Result |
|---------------|---------------------|--------|
| `grep "Save"` | `roslyn_find_symbol` | Finds exact methods, not text |
| Reading 2000-line files | `roslyn_get_method_body` | Gets just the method you need |
| Find/replace refactoring | `roslyn_rename_symbol` | Updates all references correctly |

### Example Prompts

```
Find all types containing "Controller"
→ Uses roslyn_find_symbol

What methods does UserService have?
→ Uses roslyn_get_type_members

Fix the null reference in BuildCache method
→ Uses roslyn_get_method_body + roslyn_update_method

Rename GetData to FetchDataAsync
→ Uses roslyn_rename_symbol (updates all call sites)
```

---

## Benchmark: MCP vs Native Tools

Tested on a real enterprise codebase (36 projects, 155-member class across 13 partial files):

```
                        MCP Tools    Native Tools
                        ─────────    ────────────
Tool Calls                   11            38
Context Tokens            123 K         233 K
Accuracy                   100%           80%
```

### Where MCP Makes a Difference

```
Task                    MCP                         Native
────────────────────────────────────────────────────────────────
Find callers            18 exact results            155 matches (false positives)
Impact analysis         44 symbols traced           Failed (can't build call graph)
Dead code detection     50 unused symbols           Not feasible
```

### Where It Doesn't Matter

Code modification (add/update/delete methods) performed similarly with both approaches. Native Edit works fine when files are in context.

### Honest Assessment

- **3 of 11 steps** showed clear MCP advantage (semantic queries)
- **7 steps** were roughly equivalent
- **1 step** native was more efficient (simple text search)

MCP solves problems native tools cannot solve. For everything else, either works.

**[Full benchmark with methodology](docs/benchmark/results.md)**

---

## Skills

Custom commands for common tasks:

| Command | What it does |
|---------|--------------|
| `/architect` | Deep code analysis with actionable improvement tasks |
| `/blog` | Generate a session retrospective (Watson-style) |
| `/update-docs` | Update documentation after code changes |

> `/blog` and `/update-docs` use Haiku model (~20x cheaper than Opus)

---

## How It Works

Setup creates these files in your project:

```
YourSolution/
├── CLAUDE.md          # Instructions Claude reads automatically
├── .mcp.json          # MCP server configuration
└── .claude/
    ├── hooks/         # Enforce best practices (git workflow, Roslyn usage)
    ├── skills/        # Custom commands (/architect, /blog, /update-docs)
    └── plans/         # Track work across sessions
```

**Hooks** ensure Claude:
- Uses Roslyn tools instead of grep/cat for C# files
- Follows git workflow (branches, commits, PRs)
- Reads instructions before making changes

---

## Available Tools

<details>
<summary><strong>Code Navigation</strong> (click to expand)</summary>

| Tool | Purpose |
|------|---------|
| `roslyn_find_symbol` | Search for types, methods, properties by name |
| `roslyn_get_references` | Find all usages of a symbol |
| `roslyn_get_callers` | Find all call sites of a method |
| `roslyn_get_implementations` | Find interface implementations |
| `roslyn_get_type_members` | List all members of a class |
| `roslyn_get_method_body` | Get source code of a method |

</details>

<details>
<summary><strong>Code Modification</strong></summary>

| Tool | Purpose |
|------|---------|
| `roslyn_update_method` | Replace a method's implementation |
| `roslyn_add_member` | Add method/property/field to a type |
| `roslyn_add_type` | Create new class/interface/struct |
| `roslyn_delete_member` | Remove a member from a type |
| `roslyn_rename_symbol` | Rename across entire solution |
| `roslyn_extract_method` | Extract code into new method |

</details>

<details>
<summary><strong>Diagnostics & Fixes</strong></summary>

| Tool | Purpose |
|------|---------|
| `roslyn_get_diagnostics` | Get compiler errors and warnings |
| `roslyn_apply_code_fix` | Apply Roslyn's suggested fix |
| `roslyn_batch_apply_code_fixes` | Fix all issues of a specific type |
| `roslyn_remove_unnecessary_usings` | Clean up unused usings |

</details>

<details>
<summary><strong>Call Graph Analysis</strong></summary>

| Tool | Purpose |
|------|---------|
| `roslyn_graph_analyze` | Build call graph database |
| `roslyn_query_graph` | Query callers/callees recursively |
| `roslyn_graph_impact` | Analyze blast radius of changes |
| `roslyn_find_dead_code` | Find unused methods/properties |

</details>

<details>
<summary><strong>Knowledge Base</strong></summary>

| Tool | Purpose |
|------|---------|
| `roslyn_knowledge_add` | Save gotchas, patterns, insights |
| `roslyn_knowledge_search` | Semantic search for past learnings |
| `roslyn_knowledge_for_symbol` | Get knowledge linked to a symbol |

</details>

---

## 3D Visualization

Explore your codebase in 3D:

![3D Visualization](docs/visualization-preview.png)

```bash
# First, build the call graph
Ask Claude: "Analyze the call graph for this solution"

# Then start the web server
dotnet run --project RoslynMcpServer.Web
# Open http://localhost:5000
```

- **Galaxies** = Namespaces
- **Stars** = Types
- **Planets** = Methods (sized by caller count)
- **Meditation Mode** = Auto-follows Claude's activity

---

## Prerequisites

| Requirement | For |
|-------------|-----|
| Windows x64 | Required |
| Python 3.x | Workflow hooks |
| [Claude Code CLI](https://claude.ai/download) | Required |
| .NET 10.0 SDK | Building from source only |
| Visual Studio 2022 | Building from source only |

---

## Troubleshooting

<details>
<summary><strong>Server not responding</strong></summary>

The server may be locked during rebuild:
```powershell
tasklist | findstr RoslynMcpServer
taskkill /F /PID <pid>
# In Claude Code: /mcp reconnect roslyn
```

</details>

<details>
<summary><strong>MSBuild errors</strong></summary>

Ensure Visual Studio 2022 or Build Tools are installed. The server uses MSBuildLocator to find MSBuild.

</details>

<details>
<summary><strong>Slow first load</strong></summary>

Normal - the first operation loads the entire solution into memory. Subsequent operations are faster.

</details>

---

## Architecture

```
RoslynMcpServer/
├── src/                      # MCP server (JSON-RPC over stdio)
├── Instructions/             # Topic instructions, templates, hooks
├── RoslynMcpServer.Graph/    # Call graph + knowledge database
├── RoslynMcpServer.Web/      # 3D visualization
└── RoslynMcpServer.Tests/    # Unit tests
```

Uses: MSBuildWorkspace, Roslyn APIs, SQLite, Three.js, SmartComponents embeddings

---

## Watson's Chronicles

*"The game is afoot!"*

My friend Watson (a Haiku-class AI with a flair for the dramatic) documents our coding adventures in the style of Dr. Watson chronicling Sherlock Holmes. If you enjoy tales of dead code hunts, epic bug squashing, and the occasional existential crisis about false positives...

**[Read Watson's Blog](docs/blog/)** - *Warning: May contain excessive admiration for Claude's "characteristic precision"*

---

## License

MIT

---

## Contributing

Issues and PRs welcome. See [CLAUDE.md](CLAUDE.md) for development guidelines.
