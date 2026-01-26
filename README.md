# Roslyn MCP Server

A server that gives Claude deep understanding of your C# code using Microsoft's Roslyn compiler.

## What Is This?

When you use **Claude Code** (Anthropic's AI coding assistant for the terminal), it normally treats your code as text. This works, but it can:
- Find the wrong `Save` method when you search for "Save"
- Read entire 2000-line files just to see one method
- Break code when doing find/replace refactoring

**Roslyn MCP Server** gives Claude the same understanding of C# that your IDE has. It knows `User` the class is different from `user` the variable.

| Without This Server | With This Server |
|---------------------|------------------|
| `grep "Save"` finds 50 text matches | Finds the exact `UserService.Save()` method |
| Reads whole file to find one method | Returns just that method |
| Find/replace can match wrong code | Targets exactly the right symbol |

## Prerequisites

**For Quick Install (pre-built release):**
1. **Windows x64**
2. **Python 3.x** - for workflow hooks
3. **Claude Code CLI** - Install in PowerShell with:
   ```powershell
   irm https://claude.ai/install.ps1 | iex
   ```
   Then authenticate by running `claude` and follow the prompts.

**Additional requirements for building from source:**
4. **.NET 10.0 SDK** - [Download from Microsoft](https://dotnet.microsoft.com/download)
5. **Visual Studio 2022** (any edition) or **Build Tools for Visual Studio** - needed for MSBuild

## Quick Start

### Option 1: Quick Install (Recommended)

1. **Download** the latest release from [GitHub Releases](https://github.com/BeinerChes/RoslynMcpServer/releases)
2. **Extract** `RoslynMcpServer-v1.0.2-win-x64.zip` to a temporary folder
3. **Run the installer:**
   ```powershell
   powershell -ExecutionPolicy Bypass -File install.ps1
   ```
   This installs to `%LOCALAPPDATA%\RoslynMcpServer`

4. **Set up your C# project:**
   ```powershell
   cd C:\path\to\your\solution
   powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\RoslynMcpServer\setup.ps1"
   ```

5. **Start Claude Code and verify:**
   ```bash
   claude
   ```
   Run `/mcp` - you should see `roslyn` listed.

### Option 2: Build from Source

1. **Clone and build:**
   ```bash
   git clone https://github.com/BeinerChes/RoslynMcpServer.git
   cd RoslynMcpServer
   dotnet build
   ```

2. **Set up your C# project:**
   ```powershell
   cd C:\path\to\your\solution
   powershell -ExecutionPolicy Bypass -File C:\path\to\RoslynMcpServer\setup.ps1
   ```

3. **Start Claude Code and verify:**
   ```bash
   claude
   ```
   Run `/mcp` - you should see `roslyn` listed.

### What Setup Creates

```
YourSolution/
├── .mcp.json                          # MCP server configuration
├── CLAUDE.md                          # Instructions for Claude
└── .claude/
    ├── hooks/                         # Workflow enforcement hooks
    │   ├── enforce-git-instructions.py
    │   ├── enforce-plan-instructions.py
    │   ├── suggest-roslyn-for-csharp.py
    │   └── suggest-roslyn-for-read.py
    ├── skills/                        # Custom skills (cost-optimized)
    │   └── update-docs/SKILL.md       # Uses Haiku model (~20x cheaper)
    ├── plans/                         # Plan files (per-solution)
    └── settings.json                  # Hook configuration
```

### Test It

```
Find all types containing "Controller" in this solution
```

If Claude uses `roslyn_find_symbol`, everything is working.

## Available Topics

The template tells Claude to fetch topic-specific instructions via `roslyn_get_instructions`:

| Topic | Description | solutionPath |
|-------|-------------|--------------|
| `plan` | Session planning, issue tracking, plan file management | **Required** |
| `tools` | Roslyn tool preferences and usage | **Required** |
| `git` | Git workflow, branches, commits, **unit testing** | **Required** |
| `code` | C# best practices | Optional |
| `tdd` | Test-driven development | Optional |
| `pre-pr` | Checklist before creating pull request | Optional |

**Important:** The `solutionPath` parameter is **required** for `plan`, `git`, and `tools` topics to generate per-solution tokens for hook validation. Find the solution file first with `glob pattern "*.sln*"`.

## Why CLAUDE.md Matters

Without `CLAUDE.md`, Claude uses basic file operations:
- Reads entire files with `cat` or `Read`
- Searches with `grep` (finds text, not symbols)
- Edits with pattern matching (can match wrong code)

With `CLAUDE.md` pointing to Roslyn tools:
- Reads just the method you need
- Searches semantically (finds the right `Save()` method)
- Edits precisely (modifies exactly one method)

The CLAUDE.md file is instructions **for Claude to read**, not commands for you to run. You create it once and Claude follows it automatically.

## Hook Enforcement System

The server includes hooks that enforce Claude to read instructions before performing operations. This ensures consistent workflow and prevents Claude from skipping important steps.

### What Hooks Enforce

| Operation | Behavior | Token |
|-----------|----------|-------|
| `git commit` | BLOCKED - requires `issues/*` branch name | N/A |
| `git commit`, `git push` | BLOCKED - requires valid git token | 1 minute |
| `gh issue create/close/edit` | BLOCKED - requires valid plan token | 1 minute |
| `gh pr create/merge` | BLOCKED - requires valid plan token | 1 minute |
| `Read` on `.cs` files | BLOCKED - requires valid tools token | 1 minute |
| `Edit`/`Write` on `.cs` files | BLOCKED - requires valid tools token | 1 minute |

Tokens are **per-solution** (hash-based filenames) and expire after **1 minute**. Global tokens are not supported - Claude must always provide the `solutionPath` parameter when calling `roslyn_get_instructions` for plan/git/tools topics.

### Plan Files

Plans are stored **per-solution** in `.claude/plans/`:
- Tracks work across sessions
- Links to GitHub issues
- Prevents context loss during long sessions

**Note:** Hook tokens are also per-solution. Each solution gets its own token files (e.g., `~/.claude/roslyn-git-token-{hash}`), expiring after 1 minute.

## Skills

The setup includes custom skills for specialized tasks:

| Skill | Description |
|-------|-------------|
| `/architect` | Deep solution analysis with detailed task generation |
| `/update-docs` | Update documentation after code changes (uses Haiku - 20x cheaper) |

### Architect Skill

The `/architect` skill performs comprehensive code analysis like a principal software engineer:

**Interactive Mode** - Run without arguments to choose scope:
```
/architect
```

Options:
1. **Entire solution** - Full 7-phase architecture review
2. **Specific project** - Focused .csproj analysis
3. **Specific class** - Deep dive (members, callers, impact)
4. **Specific method** - Detailed analysis (body, call graph, tests)

**Direct Mode** - Provide scope as argument:
```
/architect C:\path\solution.slnx
/architect C:\path\solution.slnx MyNamespace.MyClass
/architect C:\path\solution.slnx MyNamespace.MyClass.MyMethod
```

**Deep Task Generation** - Every finding becomes an actionable task with:
- Problem statement (location, severity, metrics)
- Root cause analysis
- Implementation steps with code examples
- Best practices to follow
- Unit tests required (checkboxes)
- Acceptance criteria (measurable outcomes)

### Update-Docs Skill

After making code changes:
```
/update-docs
```

Uses Haiku model (~20x cheaper than Opus) for straightforward documentation updates.

## Available Tools

| Tool | Description |
|------|-------------|
| `roslyn_find_symbol` | Search for types, methods, properties by name pattern |
| `roslyn_get_references` | Find all usages of a symbol across the solution |
| `roslyn_get_callers` | Find all callers of a method (only call sites, not declarations) |
| `roslyn_get_implementations` | Find all implementations of an interface or derived classes |
| `roslyn_get_type_members` | List all members (methods, properties, fields) of a type |
| `roslyn_get_method_body` | Get the full source code of a specific method |
| `roslyn_update_method` | Replace a method's implementation |
| `roslyn_add_member` | Add a new method/property/field to a type |
| `roslyn_add_type` | Create a new class/interface/struct/record/enum in a project |
| `roslyn_delete_member` | Delete a method/property/field from a type (includes attributes, XML docs) |
| `roslyn_get_diagnostics` | Compile and get warnings/errors (CS* and CA* rules) |
| `roslyn_apply_code_fix` | Apply Roslyn's suggested fix for a single diagnostic |
| `roslyn_batch_apply_code_fixes` | Batch apply fixes for all diagnostics of a specific type |
| `roslyn_remove_unnecessary_usings` | Remove unnecessary using directives (CS8019) from files |
| `roslyn_rename_symbol` | Rename a symbol across the entire solution with all references |
| `roslyn_extract_method` | Extract code block into new method with automatic parameter/return detection |
| `roslyn_get_projects_in_build_order` | Get solution structure and dependencies |
| `roslyn_graph_status` | Check if a call graph database exists for a solution |
| `roslyn_graph_analyze` | Build or update the call graph database |
| `roslyn_query_graph` | Query callers/callees with recursive depth (auto-refreshes stale files) |
| `roslyn_graph_impact` | Analyze blast radius - what breaks if you change a symbol (auto-refreshes) |
| `roslyn_find_dead_code` | Find methods/properties with no callers (see limitations below) |
| `roslyn_get_template` | Get CLAUDE.md template for your project |
| `roslyn_get_instructions` | Get topic-specific instructions (tools, git, code, plan, tdd, pre-pr) |
| `roslyn_setup_hooks` | Set up Claude Code hooks to enforce instruction reading before git/GitHub operations |
| `roslyn_knowledge_add` | Add knowledge entries (gotchas, patterns, insights) linked to symbols |
| `roslyn_knowledge_search` | Semantic search (returns compact results: id, title, category, tags) |
| `roslyn_knowledge_list` | List knowledge entries (returns compact results: id, title, category, tags) |
| `roslyn_knowledge_get` | Get full content of a single entry by ID |
| `roslyn_knowledge_delete` | Delete a knowledge entry by ID |
| `roslyn_knowledge_for_symbol` | Get all knowledge linked to a specific symbol (full content) |

### Dead Code Detection Limitations

`roslyn_find_dead_code` uses static analysis and may produce **false positives** for:
- **DTO properties** used via JSON serialization (reflection-based access cannot be tracked)
- **Properties without attributes** in model classes

The tool automatically excludes:
- Entry points (`Main`, `RunAsync`, event handlers)
- Properties with any attributes (likely used for serialization)
- External/BCL symbols
- Test files (optional)

Review results carefully - some flagged code may be used via reflection.

## Usage Examples

### Find a Symbol

```
Find all types containing "Layer" in the solution
```

Claude will use `roslyn_find_symbol` to semantically search for types.

### Understand a Large Class

```
What methods does FeatureLayer have?
```

Claude will use `roslyn_get_type_members` to list all members without reading the entire file.

### Safe Method Editing

```
Fix the null reference bug in FeatureLayer.BuildCachedData
```

Claude will:
1. Use `roslyn_get_method_body` to get just that method
2. Apply the fix
3. Use `roslyn_update_method` to replace it precisely

### Add New Functionality

```
Add a Dispose method to the CacheManager class
```

Claude will use `roslyn_add_member` with proper formatting.

### Create a New Class

```
Create a new UserService class in the MyApp.Core project under the Services folder
```

Claude will use `roslyn_add_type` to create `MyApp.Core/Services/UserService.cs` with:
- Proper namespace (`MyApp.Core.Services`)
- File-scoped namespace declaration
- Correct file placement

### Find Code Health Issues

```
What warnings does the MyApp.Core project have?
```

Claude will use `roslyn_get_diagnostics` to compile and summarize issues.

## 3D Call Graph Visualization

The server includes a web-based 3D visualization of your codebase's call graph:

![3D Visualization](docs/visualization-preview.png)

### Features
- **Galaxies** = Namespaces (sphere clusters, most connected in center)
- **Stars** = Types (white glowing points with bloom effect)
- **Planets** = Methods/Properties (orbiting stars, sized by callers)
- **Meditation Mode** = Auto-follows Claude's activity (enabled by default)
- Click to select and focus, dimming other objects
- Controls: Left mouse = pan, Middle mouse = rotate, Scroll = zoom

### Meditation Mode

When Claude Code uses MCP tools like `roslyn_get_type_members` or `roslyn_get_method_body`, the visualization automatically flies to the symbol being analyzed. This creates a relaxing "meditation" experience where you can watch Claude explore your codebase in 3D.

- Enabled by default when the visualization loads
- Polls every 10 seconds for new symbol activity
- Toggle with the "Meditation" checkbox in the UI

### Running the Visualization

1. First, build the call graph:
   ```
   Ask Claude: "Analyze the call graph for this solution"
   ```
   Claude will use `roslyn_graph_analyze` to build the database.

2. Start the web server:
   ```bash
   dotnet run --project RoslynMcpServer.Web
   ```

3. Open http://localhost:5000 and enter your solution path.

### Find .NET Analyzer Warnings (CA* rules)

```
Find all CA1806 warnings in the solution
```

The server includes Microsoft.CodeAnalysis.NetAnalyzers, so it detects the same CA* rules as Visual Studio:
- **CA1806** - Do not ignore method results
- **CA2000** - Dispose objects before losing scope
- **CA1062** - Validate arguments of public methods
- And 200+ more rules...

### Auto-Fix Warnings

```
Fix the CS0168 warning at line 42 in VectorTileLayer.cs
```

Claude will use `roslyn_apply_code_fix` to automatically apply Roslyn's suggested fix.

### Rename a Symbol

```
Rename the GetData method in DataService to FetchDataAsync
```

Claude will use `roslyn_rename_symbol` to safely rename the method and update all references across the solution.

## Troubleshooting

### "Solution file not found"

Ensure you're using an absolute path to the `.sln` or `.slnx` file.

### Server not responding

The server may be locked during rebuild. Kill it and reconnect:

```powershell
# Find the process
tasklist | findstr RoslynMcpServer

# Kill it
taskkill /F /PID <pid>

# Reconnect in Claude Code
/mcp reconnect roslyn
```

### MSBuild errors

Ensure Visual Studio 2022 or Build Tools are installed. The server uses `MSBuildLocator` to find MSBuild.

### Slow first load

The first operation loads the entire solution into memory. Subsequent operations are faster.

## Architecture

```
RoslynMcpServer/
├── src/                          # Main MCP server source
│   ├── McpServer.cs              # MCP protocol (JSON-RPC over stdio)
│   ├── RoslynTools.*.cs          # Tool registrations (partial class)
│   ├── SolutionAnalyzerService.*.cs  # Roslyn logic (partial class)
│   ├── Instructions.cs           # Dynamic instruction loading
│   └── HookTokenService.cs       # Token generation for hook validation
├── Instructions/                 # Markdown instruction files
│   ├── Topics/                   # Topic instructions (code, git, plan, tdd, pre-pr, tools)
│   ├── Templates/                # CLAUDE.md templates (minimal, standard, tdd, team)
│   └── Hooks/                    # Python hook scripts for Claude Code
├── RoslynMcpServer.Graph/        # Call graph and knowledge database
│   ├── GraphDatabase.cs          # SQLite-based call graph storage
│   ├── GraphAnalyzer.cs          # Builds call graph from Roslyn
│   ├── KnowledgeDatabase.cs      # SQLite-based knowledge storage with FTS5
│   ├── IEmbeddingProvider.cs     # Interface for embedding providers
│   ├── SmartComponentsEmbeddingProvider.cs  # Default embeddings (all-MiniLM-L6-v2)
│   └── Models.Knowledge.cs       # Knowledge entry data models
├── RoslynMcpServer.Web/          # 3D visualization web app
│   ├── Program.cs                # ASP.NET minimal API
│   ├── GraphApi.cs               # REST endpoints for visualization
│   └── wwwroot/index.html        # Three.js 3D visualization
└── RoslynMcpServer.Tests/        # Unit tests
```

The server uses:
- **MSBuildWorkspace** to load solutions
- **Roslyn Compiler APIs** for semantic analysis
- **Roslyn Formatter** for code formatting
- **Bundled .NET Analyzers** for CA* diagnostic rules
- **SQLite** for call graph and knowledge persistence
- **SmartComponents.LocalEmbeddings** for semantic search (all-MiniLM-L6-v2)
- **Three.js** for 3D visualization
- **JSON-RPC 2.0** over stdio for MCP communication
- **Python hooks** for Claude Code workflow enforcement

## Roadmap

### Planned Tools (High Priority)

| Tool | Description | Use Case |
|------|-------------|----------|
| `roslyn_change_signature` | Add/remove/reorder parameters | API changes with auto-fix callers |
| `roslyn_get_document_symbols` | All symbols in a specific file | Quick file overview |
| `roslyn_organize_usings` | Sort + remove unused usings | Code cleanup |
| `roslyn_format_document` | Apply .editorconfig formatting | Consistent style |

### Planned Tools (Medium Priority)

| Tool | Description |
|------|-------------|
| `roslyn_extract_interface` | Extract interface from class |
| `roslyn_move_type_to_file` | Move class to its own .cs file |
| `roslyn_encapsulate_field` | Convert field to property with backing field |
| `roslyn_generate_constructor` | Create constructor from fields/properties |
| `roslyn_pull_members_up` | Move members to base class |
| `roslyn_push_members_down` | Move members to derived classes |

## License

MIT

## Files in This Repository

| File | Purpose |
|------|---------|
| `README.md` | This file - how to install and use the server |
| `setup.ps1` | **Run this in your projects** to set up CLAUDE.md, hooks, and MCP config |
| `CLAUDE.md` | Instructions for developing this repository itself (not for end users) |
| `.mcp.json` | Example MCP configuration for Claude Code |
| `Instructions/Topics/` | Topic instructions fetched by `roslyn_get_instructions` |
| `Instructions/Templates/` | CLAUDE.md templates fetched by `roslyn_get_template` |
| `Instructions/Hooks/` | Python hook scripts installed by `setup.ps1` |

## Contributing

Issues and PRs welcome. See `CLAUDE.md` for development guidelines for this repository.
