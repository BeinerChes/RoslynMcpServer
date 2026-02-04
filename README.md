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
│   ├── RoslynMcpServer.exe
│   └── ...
├── YourProject/
└── YourSolution.sln
```

### 3. Initialize

```bash
cd C:\path\to\YourSolution
.roslyn-mcp\RoslynMcpServer.exe --init
```

### 4. Verify

```bash
claude
# Type: /mcp
# You should see "roslyn" connected
```

**That's it.** Claude will now use Roslyn tools for C# code.

---

## Benchmark

MCP vs Native tools on enterprise codebase: 47% fewer tokens, 100% accuracy vs 80%.

[Full benchmark with methodology](docs/benchmark/results.md)

---

## Available Tools

<details>
<summary><strong>Code Navigation</strong> (click to expand)</summary>

| Tool | Purpose |
|------|---------|
| `FindSymbol` | Search for types, methods, properties by name |
| `GetReferences` | Find all usages of a symbol |
| `GetCallers` | Find all call sites of a method |
| `GetImplementations` | Find interface implementations |
| `GetTypeMembers` | List all members of a class |
| `GetMethodBody` | Get source code of a method |

</details>

<details>
<summary><strong>Code Modification</strong></summary>

| Tool | Purpose |
|------|---------|
| `UpdateMethod` | Replace a method's implementation |
| `AddMember` | Add method/property/field to a type |
| `AddType` | Create new class/interface/struct |
| `DeleteMember` | Remove a member from a type |
| `RenameSymbol` | Rename across entire solution |
| `ExtractMethod` | Extract code into new method |

</details>

<details>
<summary><strong>Diagnostics & Fixes</strong></summary>

| Tool | Purpose |
|------|---------|
| `GetDiagnostics` | Get compiler errors and warnings |
| `ApplyCodeFix` | Apply Roslyn's suggested fix |
| `BatchApplyCodeFixes` | Fix all issues of a specific type |
| `RemoveUnnecessaryUsings` | Clean up unused usings |

</details>

<details>
<summary><strong>Call Graph Analysis</strong></summary>

| Tool | Purpose |
|------|---------|
| `GraphAnalyze` | Build call graph database |
| `QueryGraph` | Query callers/callees recursively |
| `GraphImpact` | Analyze blast radius of changes |
| `FindDeadCode` | Find unused methods/properties |

</details>

<details>
<summary><strong>Knowledge Base</strong></summary>

| Tool | Purpose |
|------|---------|
| `KnowledgeAdd` | Save gotchas, patterns, insights |
| `KnowledgeSearch` | Semantic search for past learnings |
| `KnowledgeForSymbol` | Get knowledge linked to a symbol |

</details>

---

## CLI Reference

```
RoslynMcpServer.exe [command]

Commands:
  (none)           Run as MCP server (default)
  --init           Initialize project (creates .mcp.json, CLAUDE.md, optional hooks/skills)
  --version        Show version
  --help           Show help
```

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| Windows x64 | Required |
| [Claude Code CLI](https://claude.ai/download) | Required |
| Visual Studio 2022 or Build Tools | Required (MSBuild) |
| Python 3.x | Optional (for hooks) |

---

## Troubleshooting

<details>
<summary><strong>Server not responding</strong></summary>

The server may be locked. Kill and reconnect:
```powershell
taskkill /F /IM RoslynMcpServer.exe
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

## License

MIT

---

## Contributing

Issues and PRs welcome. See [CLAUDE.md](CLAUDE.md) for development guidelines.
