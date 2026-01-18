# Roslyn MCP Server

A **Model Context Protocol (MCP) server** that provides deep C# code analysis capabilities using Microsoft's Roslyn compiler platform. Enables AI assistants like Claude to understand, navigate, and modify .NET codebases with semantic precision.

## Why Use This?

**For C# developers:** This server gives Claude "IDE superpowers" for your .NET code. Instead of treating code as text, Claude can:

| Without Roslyn MCP | With Roslyn MCP |
|-------------------|-----------------|
| `grep "Save"` finds 50 matches | `roslyn_find_symbol` finds the exact `UserService.Save()` method |
| Reading a 2000-line file to find one method | `roslyn_get_method_body` returns just that method |
| `Edit` pattern matches wrong code | `roslyn_update_method` targets exactly one method |
| Manual find/replace breaks code | `roslyn_rename_symbol` updates all references correctly |

**The key insight:** Roslyn (the C# compiler) understands your code semantically. It knows `User` the class is different from `user` the variable. This MCP server exposes that understanding to Claude.

### Real-World Benefits

- **Large codebases** - Navigate 100+ file solutions without reading everything
- **Safe refactoring** - Rename symbols, find all usages, understand impact
- **Precise edits** - Modify one method in a 2000-line file without pattern ambiguity
- **Code health** - Find and auto-fix warnings (CS* and CA* rules) across the solution

## Prerequisites

- **.NET 10.0 SDK** or later
- **Visual Studio 2022** or **Build Tools** (for MSBuild)
- **Claude Code** CLI

## Features

- **Semantic Code Analysis** - Uses Roslyn compiler for accurate symbol resolution
- **Built-in .NET Analyzers** - Includes Microsoft.CodeAnalysis.NetAnalyzers for CA* rules (CA1806, CA2000, etc.)
- **Self-contained** - All analyzer DLLs are bundled with the server on build

## Quick Start

### 1. Clone and Build

```bash
git clone https://github.com/BeinerChes/RoslynMcpServer.git
cd RoslynMcpServer
dotnet build
```

### 2. Configure Claude Code

Add the server to your Claude Code MCP configuration. You have two options:

#### Option A: Project-level config (recommended)

Create `.mcp.json` in your project root:

```json
{
  "mcpServers": {
    "roslyn": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\RoslynMcpServer"]
    }
  }
}
```

#### Option B: Global config

Add to your global Claude Code settings (`~/.claude/settings.json`):

```json
{
  "mcpServers": {
    "roslyn": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\RoslynMcpServer"]
    }
  }
}
```

### 3. Connect in Claude Code

```bash
# Start Claude Code in your .NET project directory
claude

# The roslyn server should connect automatically
# Verify with:
/mcp
```

If you need to reconnect:
```
/mcp reconnect roslyn
```

### 4. Configure CLAUDE.md (Important!)

Add a `CLAUDE.md` file to your project root to instruct Claude to use Roslyn tools instead of native file operations.

**Quick start:** Copy the template from this repository:
```bash
# From your project directory
curl -o CLAUDE.md https://raw.githubusercontent.com/BeinerChes/RoslynMcpServer/rc/1.0.0/CLAUDE_TEMPLATE.md
```

Or manually copy `CLAUDE_TEMPLATE.md` from this repository and customize it for your project.

**Why is this important?** Without `CLAUDE.md`, Claude may default to native `Read`/`Edit` tools which:
- Use text patterns that can match the wrong code
- Read entire files when you only need one method
- Miss overloads and type context

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
| `roslyn_get_diagnostics` | Compile and get warnings/errors (CS* and CA* rules) |
| `roslyn_apply_code_fix` | Apply Roslyn's suggested fix for a single diagnostic |
| `roslyn_batch_apply_code_fixes` | Batch apply fixes for all diagnostics of a specific type |
| `roslyn_rename_symbol` | Rename a symbol across the entire solution with all references |
| `roslyn_get_projects_in_build_order` | Get solution structure and dependencies |

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

### Find Code Health Issues

```
What warnings does the MyApp.Core project have?
```

Claude will use `roslyn_get_diagnostics` to compile and summarize issues.

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

## Tool Details

### roslyn_find_symbol

```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "pattern": "FeatureLayer",
  "symbolKind": "type",
  "matchType": "contains",
  "maxResults": 50
}
```

**symbolKind**: `all`, `type`, `member`, `namespace`, `typeAndMember`
**matchType**: `exact`, `exactIgnoreCase`, `contains`, `prefix`, `suffix`

### roslyn_get_method_body

```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "typeName": "FeatureLayer",
  "methodName": "BuildCachedData",
  "parameterTypes": "CancellationToken, bool"
}
```

Use `parameterTypes` to select specific overloads.

### roslyn_update_method

```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "typeName": "FeatureLayer",
  "methodName": "BuildCachedData",
  "parameterTypes": "CancellationToken, bool",
  "newSourceCode": "private void BuildCachedData(...) { /* new impl */ }"
}
```

### roslyn_add_member

```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "typeName": "CacheManager",
  "memberCode": "public void Dispose() { _cache.Clear(); }",
  "insertionPoint": "end"
}
```

**insertionPoint**: `start`, `end`, `after-fields`, `after-constructors`, `after-properties`, `before-methods`
Default: smart placement based on member type.

### roslyn_get_diagnostics

```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "severityFilter": "warning",
  "projectFilter": "MyApp.Core"
}
```

Without `diagnosticId`: returns summary with counts per diagnostic code.
With `diagnosticId`: returns detailed entries with file/line/method info.

### roslyn_apply_code_fix

```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "filePath": "C:\\path\\to\\MyClass.cs",
  "line": 42,
  "column": 13,
  "diagnosticId": "CS0168",
  "fixIndex": 0,
  "preview": false
}
```

**Workflow:**
1. Use `roslyn_get_diagnostics` to find issues
2. Use `roslyn_apply_code_fix` with `preview: true` to see what would change
3. Apply the fix with `preview: false`

If multiple fixes are available, the tool returns the list. Specify `fixIndex` to select one.

### roslyn_batch_apply_code_fixes

```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "diagnosticId": "CS0168",
  "projectFilter": "MyProject",
  "maxFixes": 100,
  "preview": false
}
```

**Workflow:**
1. Use `roslyn_get_diagnostics` to find issues (note `fixAvailable: true` in summary)
2. Use `roslyn_batch_apply_code_fixes` with `preview: true` to see what would change
3. Apply fixes with `preview: false`
4. Verify with `roslyn_get_diagnostics` again

**When to use:**
- **Batch**: Fix all occurrences of a warning type (e.g., all CS0168 unused variables)
- **Single**: Fix one diagnostic, or choose between multiple fix options

### roslyn_rename_symbol

```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "filePath": "C:\\path\\to\\DataService.cs",
  "line": 15,
  "column": 22,
  "newName": "FetchDataAsync"
}
```

**Workflow:**
1. Use `roslyn_find_symbol` to locate the symbol you want to rename
2. Call `roslyn_rename_symbol` with the file path, line, column, and new name
3. The tool applies the rename immediately and returns a list of affected files

The tool updates all references across the entire solution automatically.

## Known Limitations

### WPF/XAML Projects

**Note:** As of version 1.0.0, WPF/XAML support has been improved.

The server automatically scans for generated `*.g.cs` files in each project's `obj/` folder and includes them in the compilation. This prevents false positive errors like:
- `CS0103: The name 'InitializeComponent' does not exist in the current context`
- `CS0103: The name 'uxMap' does not exist in the current context`

**Requirement:** The WPF project must have been built at least once (via `dotnet build` or Visual Studio) so the generated files exist in the `obj/` folder.

See [GitHub issue #11](https://github.com/BeinerChes/RoslynMcpServer/issues/11) for details

## Troubleshooting

### "Solution file not found"

Ensure you're using an absolute path to the `.sln` or `.slnx` file.

### Server not responding

The server may be locked during rebuild. Kill it and reconnect:

```bash
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
├── Program.cs                    # Entry point
├── src/
│   ├── McpServer.cs              # MCP protocol (JSON-RPC over stdio)
│   ├── Models.cs                 # DTOs
│   ├── AnalyzerLoader.cs         # Loads bundled .NET analyzers
│   ├── RoslynTools.*.cs          # Tool registrations (partial class)
│   └── SolutionAnalyzerService.*.cs  # Roslyn logic (partial class)
```

After build:
```
bin/Debug/net10.0/
├── RoslynMcpServer.exe
├── analyzers/                    # Bundled .NET analyzers (copied on build)
│   ├── cs/
│   │   ├── Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll
│   │   └── Microsoft.CodeAnalysis.NetAnalyzers.dll
│   └── vb/
│       └── ...
```

The server uses:
- **MSBuildWorkspace** to load solutions
- **Roslyn Compiler APIs** for semantic analysis
- **Roslyn Formatter** for code formatting
- **Bundled .NET Analyzers** for CA* diagnostic rules
- **JSON-RPC 2.0** over stdio for MCP communication

## Roadmap

### Planned Tools (High Priority)

| Tool | Description | Use Case |
|------|-------------|----------|
| ~~`roslyn_get_callers`~~ | ~~Find callers of a method~~ | ✓ Implemented |
| `roslyn_extract_method` | Extract code block into new method | Refactoring large methods |
| `roslyn_change_signature` | Add/remove/reorder parameters | API changes with auto-fix callers |
| `roslyn_get_document_symbols` | All symbols in a specific file | Quick file overview |
| `roslyn_organize_usings` | Sort + remove unused usings | Code cleanup |
| `roslyn_format_document` | Apply .editorconfig formatting | Consistent style |
| `roslyn_extract_interface` | Extract interface from class | Design patterns |
| `roslyn_inline` | Replace variable/method usages with actual code | Simplify code |

### Planned Tools (Medium Priority)

| Tool | Description |
|------|-------------|
| `roslyn_move_type_to_file` | Move class to its own .cs file |
| `roslyn_encapsulate_field` | Convert field to property with backing field |
| `roslyn_generate_constructor` | Create constructor from fields/properties |
| `roslyn_generate_equals_hashcode` | Override Equals/GetHashCode for value equality |
| `roslyn_pull_members_up` | Move members to base class |
| `roslyn_push_members_down` | Move members to derived classes |
| `roslyn_find_unused_code` | Find dead methods/classes |
| `roslyn_get_dependency_graph` | Analyze assembly/type dependencies |

## License

MIT

## Files in This Repository

| File | Purpose |
|------|---------|
| `CLAUDE.md` | Development guidelines for this repository |
| `CLAUDE_TEMPLATE.md` | **Copy this to your projects** - Template for using Roslyn MCP |
| `README.md` | This file |

## Contributing

Issues and PRs welcome! See `CLAUDE.md` for development guidelines.
