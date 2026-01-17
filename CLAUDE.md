# CLAUDE.md - Instructions for Claude Code

## Project Overview

This is a **Model Context Protocol (MCP) server** written in C# (.NET 10.0) that provides C# solution analysis capabilities using Microsoft's Roslyn compiler platform (Microsoft.CodeAnalysis).

The server enables AI assistants to analyze .NET solutions with deep semantic understanding - finding symbols, tracking references, understanding build order, and more.

## Current Status

**Phase 1 Complete**: Basic MCP server infrastructure is working.

- ✅ MCP protocol implementation (JSON-RPC 2.0 over stdio)
- ✅ Tool registration system
- ✅ Two proof-of-concept tools: `roslyn_echo`, `roslyn_get_server_info`
- ✅ `.mcp.json` configured for Claude Code integration

**Next Steps (Phase 2)**: Add Roslyn analysis capabilities.

## Tech Stack

- **.NET 10.0** - Target framework
- **System.Text.Json** - JSON serialization for MCP protocol
- *(Coming soon)* **Microsoft.CodeAnalysis.Workspaces.MSBuild** - Roslyn code analysis
- *(Coming soon)* **Microsoft.Build.Locator** - Finds MSBuild installations

## Project Structure

```
RoslynMcpServer/
├── CLAUDE.md                     # This file
├── .mcp.json                     # MCP server configuration for Claude Code
├── RoslynMcpServer.csproj        # Project file
├── RoslynMcpServer.slnx          # Solution file
├── Program.cs                    # Entry point
└── src/
    ├── McpServer.cs              # MCP protocol implementation (JSON-RPC over stdio)
    └── RoslynTools.cs            # Tool definitions and handlers
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

## Available Tools

| Tool | Description |
|------|-------------|
| `roslyn_echo` | Echo test - returns the message you send |
| `roslyn_get_server_info` | Returns server version and capabilities |

## Next Steps - Roslyn Integration

1. **Add NuGet packages** to `RoslynMcpServer.csproj`:
   ```xml
   <PackageReference Include="Microsoft.Build.Locator" Version="1.7.8" />
   <PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="4.12.0" />
   <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.12.0" />
   ```

2. **Create `src/SolutionAnalyzerService.cs`**:
   - Initialize MSBuild with `MSBuildLocator.RegisterDefaults()`
   - Create `MSBuildWorkspace` to load solutions
   - Implement `LoadSolutionAsync(string solutionPath)`
   - Add thread-safe solution state management

3. **Add core tools to `RoslynTools.cs`**:
   - `roslyn_load_solution` - Load a .sln file (required before other tools)
   - `roslyn_get_projects` - List all projects in solution
   - `roslyn_get_build_order` - Show project dependency order
   - `roslyn_find_symbols` - Search for types, methods, properties by name

4. **Update `Program.cs`** to create `SolutionAnalyzerService` and pass to `RoslynTools.RegisterAll()`

## Key Concepts

### MCP Protocol
- Uses **stdio transport** (stdin/stdout for communication)
- Messages are **JSON-RPC 2.0** format, one JSON object per line
- Server must not write to stdout except for MCP responses
- Use stderr for logging/diagnostics

### Tool Naming Convention
- All tools prefixed with `roslyn_` to avoid conflicts with other MCP servers
- Use snake_case: `roslyn_get_build_order`, `roslyn_find_symbols`

## Architecture Notes

### McpServer.cs
- Handles JSON-RPC protocol parsing
- Routes methods to handlers: `initialize`, `tools/list`, `tools/call`, etc.
- Tool registration via `RegisterTool(name, definition, handler)`
- Returns `{ content: [{ type: "text", text: "..." }], isError?: bool }`

### RoslynTools.cs
- Static class that registers all tools
- Each tool has: name, description, JSON schema, annotations, async handler

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

## Code Style

- Use C# 12 features (primary constructors, collection expressions)
- Prefer `required` properties over constructor parameters for DTOs
- Use `async/await` for all I/O operations
- Error messages should be actionable (tell user what to do)
- All logging goes to stderr (`Console.Error.WriteLine`)

## Future Improvements

- [ ] `roslyn_load_solution` - Load a .sln file
- [ ] `roslyn_get_projects` - List projects in solution
- [ ] `roslyn_get_build_order` - Project dependency order
- [ ] `roslyn_find_symbols` - Search for symbols by name
- [ ] `roslyn_get_document_symbols` - Symbols in a specific file
- [ ] `roslyn_get_references` - Find all references to a symbol
- [ ] `roslyn_get_call_hierarchy` - Who calls this method
- [ ] `roslyn_get_implementations` - Find interface implementations
- [ ] Caching for compilation results
- [ ] Progress reporting for large solutions
