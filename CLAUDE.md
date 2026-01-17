# CLAUDE.md - Instructions for Claude Code

## Project Overview

This is a **Model Context Protocol (MCP) server** written in C# (.NET 10.0) that provides C# solution analysis capabilities using Microsoft's Roslyn compiler platform (Microsoft.CodeAnalysis).

The server enables AI assistants to analyze .NET solutions with deep semantic understanding - finding symbols, tracking references, understanding build order, and more.

## Code Guidelines

- **YOU MUST keep .cs files under 300 lines.** Before splitting, THINK about proper refactoring:
  1. Extract helper classes or utilities (prefer composition)
  2. Apply SOLID principles (Single Responsibility, etc.)
  3. Use partial classes only as a last resort when logic truly belongs together
- Use C# 12 features (primary constructors, collection expressions)
- Prefer `required` properties over constructor parameters for DTOs
- Use `async/await` for all I/O operations
- Error messages should be actionable (tell the user what to do)
- All logging goes to stderr (`Console.Error.WriteLine`)

## Current Status

**Phase 2 Complete**: Roslyn analysis capabilities added.

- MCP protocol implementation (JSON-RPC 2.0 over stdio)
- Tool registration system
- Roslyn integration with MSBuildWorkspace
- Solution loading and project dependency analysis
- Symbol search with semantic filtering
- Find all references to symbols

## Tech Stack

- **.NET 10.0** - Target framework
- **System.Text.Json** - JSON serialization for MCP protocol
- **Microsoft.CodeAnalysis.Workspaces.MSBuild** - Roslyn code analysis
- **Microsoft.CodeAnalysis.CSharp.Workspaces** - C# language support
- **Microsoft.Build.Locator** - Finds MSBuild installations

## Project Structure

```
RoslynMcpServer/
├── CLAUDE.md                     # This file
├── .mcp.json                     # MCP server configuration for Claude Code
├── RoslynMcpServer.csproj        # Project file
├── RoslynMcpServer.slnx          # Solution file
├── Program.cs                    # Entry point
└── src/
    ├── McpServer.cs                              # MCP protocol implementation
    ├── Models.cs                                 # DTOs and result types
    ├── RoslynTools.cs                            # Core tool registration
    ├── RoslynTools.FindSymbol.cs                 # Symbol search tool
    ├── RoslynTools.References.cs                 # References tool
    ├── RoslynTools.Implementations.cs            # Implementations tool
    ├── SolutionAnalyzerService.cs                # Core service + projects
    ├── SolutionAnalyzerService.Symbols.cs        # Symbol search logic
    ├── SolutionAnalyzerService.References.cs     # References logic
    └── SolutionAnalyzerService.Implementations.cs # Implementations logic
```

## Build Commands

```bash
# Build
dotnet build

# Run directly
dotnet run

# Test with JSON-RPC message
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' | dotnet run
```

## Development Workflow

### Rebuilding after code changes

The MCP server runs as a background process. To apply code changes:

1. **Find and kill the running process:**
   ```bash
   # Find the process
   tasklist | findstr -i RoslynMcpServer

   # Kill by PID (replace 12345 with actual PID)
   taskkill /F /PID 12345
   ```

2. **Rebuild:**
   ```bash
   dotnet build
   ```

3. **Reconnect in Claude Code:**
   - Type `/mcp` to open MCP menu
   - Select the roslyn server and reconnect
   - Or use: `/mcp reconnect roslyn`

## Available Tools

| Tool | Description |
|------|-------------|
| `roslyn_echo` | Echo test - returns the message you send |
| `roslyn_get_server_info` | Returns server version and capabilities |
| `roslyn_get_projects_in_build_order` | Loads a solution and returns projects in build order (dependencies first) |
| `roslyn_find_symbol` | Semantic search for types, methods, properties by name pattern |
| `roslyn_get_references` | Find all references to a symbol at a given position |
| `roslyn_get_implementations` | Find all implementations of an interface or derived classes |

### roslyn_find_symbol

Searches for symbols in a solution by name pattern. Much faster and more accurate than text search.

**Input:**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "pattern": "Layer",
  "symbolKind": "type",
  "matchType": "contains",
  "maxResults": 100
}
```

**Parameters:**
- `solutionPath` (required) - Absolute path to .sln file
- `pattern` (required) - Symbol name to search for
- `symbolKind` - `all`, `type`, `member`, `namespace`, `typeAndMember` (default: `all`)
- `matchType` - `exact`, `exactIgnoreCase`, `contains`, `prefix`, `suffix` (default: `contains`)
- `maxResults` - Limit results 1-1000 (default: 100)

**Output:**
```json
{
  "success": true,
  "pattern": "Layer",
  "totalFound": 15,
  "symbols": [
    {
      "name": "LayerCollection",
      "fullyQualifiedName": "Atlas.Data.LayerCollection",
      "kind": "Class",
      "filePath": "C:\\path\\to\\LayerCollection.cs",
      "line": 12,
      "accessibility": "Public",
      "signature": "class LayerCollection"
    }
  ]
}
```

### roslyn_get_references

Finds all references to a symbol at a given file position. Essential for impact analysis and refactoring. Supports pagination and filtering.

**Input:**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "filePath": "C:\\path\\to\\MyClass.cs",
  "line": 15,
  "column": 22,
  "maxResults": 100,
  "projectFilter": "Atlas.*",
  "fileFilter": "*Controller.cs"
}
```

**Parameters:**
- `solutionPath` (required) - Absolute path to .sln file
- `filePath` (required) - Absolute path to the source file containing the symbol
- `line` (required) - Line number (1-based) where the symbol is located
- `column` (required) - Column number (1-based) where the symbol is located
- `maxResults` - Maximum references to return (default: 100, max: 10000)
- `projectFilter` - Filter by project name, supports wildcards (`*`). Example: `Atlas.*`
- `fileFilter` - Filter by file path, supports wildcards (`*`). Example: `*Service.cs`

**Output:**
```json
{
  "success": true,
  "symbol": {
    "name": "MyMethod",
    "fullyQualifiedName": "MyNamespace.MyClass.MyMethod()",
    "kind": "Method",
    "signature": "void MyMethod()"
  },
  "totalFound": 387,
  "returnedCount": 100,
  "references": [
    {
      "filePath": "C:\\path\\to\\Caller.cs",
      "line": 42,
      "column": 12,
      "projectName": "MyProject",
      "preview": "instance.MyMethod();"
    }
  ]
}
```

### roslyn_get_implementations

Finds all implementations of an interface or all classes derived from a base class. Essential for understanding inheritance hierarchies and finding all variants of a pattern.

**Input:**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "typeName": "IOperation",
  "includeBaseType": false,
  "maxResults": 100
}
```

**Parameters:**
- `solutionPath` (required) - Absolute path to .sln file
- `typeName` (required) - Name of the interface or base class
- `includeBaseType` - Include the base type itself in results (default: false)
- `maxResults` - Maximum implementations to return (default: 100)

**Output:**
```json
{
  "success": true,
  "baseType": {
    "name": "IOperation",
    "fullyQualifiedName": "Atlas.Operations.IOperation",
    "kind": "Interface"
  },
  "totalFound": 15,
  "implementations": [
    {
      "name": "OperateSplit",
      "fullyQualifiedName": "Atlas.Operations.OperateSplit",
      "kind": "Class",
      "filePath": "C:\\path\\to\\OperateSplit.cs",
      "line": 12,
      "isAbstract": false,
      "baseTypes": ["BaseOperation"],
      "interfaces": ["Atlas.Operations.IOperation"]
    }
  ]
}
```

### roslyn_get_projects_in_build_order

Loads a .NET solution file and returns all projects in topological build order (dependencies first).

**Input:**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln"
}
```

**Output:**
```json
{
  "success": true,
  "solutionPath": "C:\\path\\to\\solution.sln",
  "projects": [
    {
      "name": "MyLibrary",
      "filePath": "C:\\path\\to\\MyLibrary.csproj",
      "language": "C#",
      "dependencies": []
    },
    {
      "name": "MyApp",
      "filePath": "C:\\path\\to\\MyApp.csproj",
      "language": "C#",
      "dependencies": ["MyLibrary"]
    }
  ]
}
```

## Key Concepts

### MCP Protocol
- Uses **stdio transport** (stdin/stdout for communication)
- Messages are **JSON-RPC 2.0** format, one JSON object per line
- Server must not write to stdout except for MCP responses
- Use stderr for logging/diagnostics

### Tool Naming Convention
- All tools prefixed with `roslyn_` to avoid conflicts with other MCP servers
- Use snake_case: `roslyn_get_projects_in_build_order`, `roslyn_find_symbols`

## Architecture Notes

### McpServer.cs
- Handles JSON-RPC protocol parsing
- Routes methods to handlers: `initialize`, `tools/list`, `tools/call`, etc.
- Tool registration via `RegisterTool(name, definition, handler)`
- Returns `{ content: [{ type: "text", text: "..." }], isError?: bool }`

### RoslynTools (partial class, 3 files)
- **RoslynTools.cs** - Core registration, echo, server info, projects tool
- **RoslynTools.FindSymbol.cs** - Symbol search tool
- **RoslynTools.References.cs** - Find references tool
- Each tool has: name, description, JSON schema, annotations, async handler

### SolutionAnalyzerService (partial class, 4 files)
- **SolutionAnalyzerService.cs** - MSBuild init, `GetProjectsInBuildOrderAsync()`
- **SolutionAnalyzerService.Symbols.cs** - `FindSymbolsAsync()` + helpers
- **SolutionAnalyzerService.References.cs** - `FindReferencesAsync()` + filtering
- **SolutionAnalyzerService.Implementations.cs** - `FindImplementationsAsync()`

### Models.cs
- All DTOs: `ProjectBuildOrderResult`, `FindSymbolResult`, `FindReferencesResult`, etc.

## Adding a New Tool

1. Add the tool registration in `RoslynTools.cs`:
```csharp
private static void RegisterYourTool(McpServer server)
{
    server.RegisterTool(
        "roslyn_your_tool",
        new ToolDefinition
        {
            Description = "...",
            InputSchema = new { type = "object", properties = new { ... } },
            Annotations = new ToolAnnotations { ReadOnlyHint = true }
        },
        async args =>
        {
            // Your logic here
            return new { content = new[] { new { type = "text", text = result } } };
        });
}
```

2. Call `RegisterYourTool(server)` in `RegisterAll()`.

## Future Improvements

- [ ] `roslyn_get_document_symbols` - Symbols in a specific file
- [ ] `roslyn_get_call_hierarchy` - Who calls this method
- [ ] `roslyn_get_type_members` - All members of a type with signatures
- [ ] Caching for compilation results
- [ ] Progress reporting for large solutions
