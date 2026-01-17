# Roslyn MCP Server

A **Model Context Protocol (MCP) server** that provides deep C# code analysis capabilities using Microsoft's Roslyn compiler platform. Enables AI assistants like Claude to understand, navigate, and modify .NET codebases with semantic precision.

## Why Use This?

When working with large C# codebases, text-based tools struggle with:
- Finding the right overload among many
- Understanding inheritance hierarchies
- Making targeted edits in 2000+ line files
- Avoiding pattern ambiguity in Edit operations

Roslyn MCP treats **code as structured data**, enabling:
- Semantic symbol search (not just text matching)
- Precise method-level edits (no pattern ambiguity)
- Type-aware navigation (find implementations, references)
- Compilation diagnostics (warnings/errors with locations)

## Prerequisites

- **.NET 10.0 SDK** or later
- **Visual Studio 2022** or **Build Tools** (for MSBuild)
- **Claude Code** CLI

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

## Available Tools

| Tool | Description |
|------|-------------|
| `roslyn_find_symbol` | Search for types, methods, properties by name pattern |
| `roslyn_get_references` | Find all usages of a symbol across the solution |
| `roslyn_get_implementations` | Find all implementations of an interface or derived classes |
| `roslyn_get_type_members` | List all members (methods, properties, fields) of a type |
| `roslyn_get_method_body` | Get the full source code of a specific method |
| `roslyn_update_method` | Replace a method's implementation |
| `roslyn_add_member` | Add a new method/property/field to a type |
| `roslyn_get_diagnostics` | Compile and get warnings/errors with counts |
| `roslyn_apply_code_fix` | Apply Roslyn's suggested fix for a single diagnostic |
| `roslyn_batch_apply_code_fixes` | Batch apply fixes for all diagnostics of a specific type |
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
What warnings does the Atlas.Controls project have?
```

Claude will use `roslyn_get_diagnostics` to compile and summarize issues.

### Auto-Fix Warnings

```
Fix the CS0168 warning at line 42 in VectorTileLayer.cs
```

Claude will use `roslyn_apply_code_fix` to automatically apply Roslyn's suggested fix.

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
  "projectFilter": "Atlas.Controls"
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
│   ├── RoslynTools.*.cs          # Tool registrations (partial class)
│   └── SolutionAnalyzerService.*.cs  # Roslyn logic (partial class)
```

The server uses:
- **MSBuildWorkspace** to load solutions
- **Roslyn Compiler APIs** for semantic analysis
- **Roslyn Formatter** for code formatting
- **JSON-RPC 2.0** over stdio for MCP communication

## License

MIT

## Contributing

Issues and PRs welcome! See `CLAUDE.md` for development guidelines.
