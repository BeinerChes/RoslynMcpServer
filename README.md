# Roslyn MCP Server

Give Claude Code the same understanding of C# that your IDE has.

```
You: Find all usages of UserService.Save()
Claude (without Roslyn): *searches text, finds 50 matches including comments*
Claude (with Roslyn): *finds exactly 3 call sites*
```

## Quick Start

### 1. Download

Download `roslyn-mcp-win-x64.zip` from [Releases](https://github.com/BeinerChes/RoslynMcpServer/releases)

### 2. Extract to Your Solution

```
YourSolution/
├── .roslyn-mcp/          ← Extract here
│   ├── roslyn-mcp.exe
│   └── ...
├── YourProject/
└── YourSolution.sln
```

### 3. Initialize

```bash
cd C:\path\to\YourSolution
.roslyn-mcp\roslyn-mcp.exe --init
```

### 4. Verify

```bash
claude
# Type: /mcp
# You should see "roslyn" connected
```

**That's it.** Claude will now use Roslyn tools for C# code.

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

## Optional Features

### Enable Hooks

Hooks suggest using Roslyn tools instead of grep for C# files:

```bash
.roslyn-mcp\roslyn-mcp.exe --enable-hooks
```

### Enable Skills

Skills add custom commands like `/architect`:

```bash
.roslyn-mcp\roslyn-mcp.exe --enable-skills
```

| Command | What it does |
|---------|--------------|
| `/architect` | Deep code analysis with actionable improvement tasks |
| `/update-docs` | Update documentation after code changes |

---

## Benchmark: MCP vs Native Tools

Tested on a real enterprise codebase (36 projects, 155-member class across 13 partial files):

|                    | MCP Tools | Native Tools |
|--------------------|-----------|--------------|
| Tool Calls         | 11        | 38           |
| Context Tokens     | 123K      | 233K         |
| Accuracy           | 100%      | 80%          |

### Where MCP Makes a Difference

| Task | MCP | Native |
|------|-----|--------|
| Find callers | 18 exact results | 155 matches (false positives) |
| Impact analysis | 44 symbols traced | Failed (can't build call graph) |
| Dead code detection | 50 unused symbols | Not feasible |

### Where It Doesn't Matter

Code modification (add/update/delete methods) performed similarly with both approaches. Native Edit works fine when files are in context.

### Honest Assessment

- **3 of 11 steps** showed clear MCP advantage (semantic queries)
- **7 steps** were roughly equivalent
- **1 step** native was more efficient (simple text search)

**MCP solves problems native tools cannot solve.** For everything else, either works.

[Full benchmark with methodology](docs/benchmark/results.md)

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

## CLI Reference

```
roslyn-mcp.exe [command]

Commands:
  (none)           Run as MCP server (default)
  --init           Initialize project (.mcp.json + CLAUDE.md)
  --enable-hooks   Enable Roslyn tool suggestions
  --enable-skills  Enable /architect skill
  --version        Show version
  --help           Show help
```

---

## Prerequisites

| Requirement | For |
|-------------|-----|
| Windows x64 | Required |
| [Claude Code CLI](https://claude.ai/download) | Required |
| Python 3.x | Only if using hooks (optional) |
| Visual Studio 2022 or Build Tools | MSBuild discovery |

---

## Troubleshooting

<details>
<summary><strong>Server not responding</strong></summary>

The server may be locked. Kill and reconnect:
```powershell
taskkill /F /IM roslyn-mcp.exe
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

## Building from Source

```bash
git clone https://github.com/BeinerChes/RoslynMcpServer.git
cd RoslynMcpServer
dotnet build

# Create release package
powershell -ExecutionPolicy Bypass -File build-release.ps1
```

Requires .NET 10.0 SDK.

---

## Architecture

```
RoslynMcpServer/
├── src/                      # MCP server (JSON-RPC over stdio)
├── Instructions/             # Topic instructions, templates, hooks
├── RoslynMcpServer.Graph/    # Call graph + knowledge database
├── RoslynMcpServer.Web/      # 3D visualization (experimental)
└── RoslynMcpServer.Tests/    # Unit tests
```

Uses: MSBuildWorkspace, Roslyn APIs, SQLite, SmartComponents embeddings

---

## License

MIT

---

## Contributing

Issues and PRs welcome. See [CLAUDE.md](CLAUDE.md) for development guidelines.
