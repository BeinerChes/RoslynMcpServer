# CLAUDE.md - Instructions for Claude Code

## Project Overview

This is a **Model Context Protocol (MCP) server** written in C# (.NET 10.0) that provides C# solution analysis capabilities using Microsoft's Roslyn compiler platform (Microsoft.CodeAnalysis).

The server enables AI assistants to analyze .NET solutions with deep semantic understanding - finding symbols, tracking references, understanding build order, and more.

## Tool Preferences for C# Code

When working with C# files in .NET solutions, **PREFER Roslyn MCP tools over native tools**:

| Task | Use This | NOT This |
|------|----------|----------|
| Find a type/method | `roslyn_find_symbol` | `Grep` or `Glob` |
| Understand a class | `roslyn_get_type_members` | `Read` the whole file |
| Read a method | `roslyn_get_method_body` | `Read` the whole file |
| Edit a method | `roslyn_update_method` | `Edit` with text patterns |
| Add a member | `roslyn_add_member` | `Edit` to insert code |
| Find usages | `roslyn_get_references` | `Grep` for text |
| Find implementations | `roslyn_get_implementations` | `Grep` for class names |
| Check for errors | `roslyn_get_diagnostics` | `Bash` dotnet build |
| Fix one warning | `roslyn_apply_code_fix` | Manual `Edit` |
| Fix many warnings | `roslyn_batch_apply_code_fixes` | Loop of single fixes |
| Rename symbol | `roslyn_rename_symbol` | Manual find/replace |
| Write XML docs | `roslyn_get_type_members` + `roslyn_get_method_body` | `Read` file |

**Only use native tools for:**
- Non-C# files (JSON, XML, markdown, .csproj)
- Creating brand new .cs files (use `Write`, then `roslyn_add_member` to populate)
- Very small files (< 100 lines) where `Read`/`Edit` is simpler
- When Roslyn MCP server is not connected

**Example: Writing XML docs for a class**
```
1. roslyn_get_type_members(typeName: "MyClass") → see all members, their signatures, accessibility
2. roslyn_get_method_body(typeName: "MyClass", methodName: "DoWork") → read implementation
3. Write meaningful XML doc based on understanding
4. roslyn_update_method() or Edit to add the docs
```

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
    ├── AnalyzerLoader.cs                         # .NET analyzers loader for CA* rules
    ├── McpServer.cs                              # MCP protocol implementation
    ├── Models.cs                                 # DTOs and result types
    ├── RoslynTools.cs                            # Core tool registration
    ├── RoslynTools.FindSymbol.cs                 # Symbol search tool
    ├── RoslynTools.References.cs                 # References tool
    ├── RoslynTools.Implementations.cs            # Implementations tool
    ├── RoslynTools.TypeMembers.cs                # Type members tool
    ├── RoslynTools.MethodBody.cs                 # Get method body tool
    ├── RoslynTools.UpdateMethod.cs               # Update method tool
    ├── RoslynTools.Diagnostics.cs                # Diagnostics tool
    ├── RoslynTools.AddMember.cs                  # Add member tool
    ├── RoslynTools.CodeFix.cs                    # Single code fix tool
    ├── RoslynTools.BatchCodeFix.cs               # Batch code fix tool
    ├── RoslynTools.Rename.cs                     # Rename symbol tool
    ├── SolutionAnalyzerService.cs                # Core service + projects
    ├── SolutionAnalyzerService.Symbols.cs        # Symbol search logic
    ├── SolutionAnalyzerService.References.cs     # References logic
    ├── SolutionAnalyzerService.Implementations.cs # Implementations logic
    ├── SolutionAnalyzerService.TypeMembers.cs    # Type members logic
    ├── SolutionAnalyzerService.MethodBody.cs     # Get method body logic
    ├── SolutionAnalyzerService.UpdateMethod.cs   # Update method logic
    ├── SolutionAnalyzerService.Diagnostics.cs    # Diagnostics logic
    ├── SolutionAnalyzerService.AddMember.cs      # Add member logic
    ├── SolutionAnalyzerService.CodeFix.cs        # Single code fix logic
    ├── SolutionAnalyzerService.BatchCodeFix.cs   # Batch code fix logic
    └── SolutionAnalyzerService.Rename.cs         # Rename symbol logic
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

## Git Workflow

### Branch Structure

```
rc/X.X.X          ← Default branch (release candidate), PRs target here
├── issues/N      ← Feature/fix branches linked to GitHub issues
└── issues/M

release/X.X.X     ← Stable releases (owner merges RC here when ready)
master            ← Historical, not used for development
```

### Development Workflow (for Claude)

**IMPORTANT: Always create a GitHub issue BEFORE making code changes.**

1. **Create GitHub Issue First**
   ```bash
   gh issue create --title "Brief description" --body "Detailed description" --label "bug"
   ```
   Note the issue number (e.g., `#42`)

   **Required labels** (use `--label`):
   - `bug` - Something isn't working
   - `enhancement` - New feature or request
   - `documentation` - Docs improvements
   - `refactor` - Code restructuring (create this label if missing)

2. **Create Feature Branch**
   ```bash
   git checkout rc/X.X.X
   git pull
   git checkout -b issues/42
   ```

3. **Make Changes and Commit**
   ```bash
   git add .
   git commit -m "Fix: description of change

   Closes #42

   Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
   ```

4. **Push and Create PR**
   ```bash
   git push -u origin issues/42
   gh pr create --base rc/X.X.X --title "Fix: description" --body "Closes #42"
   ```

5. **Merge PR** (after review if needed)
   ```bash
   gh pr merge --squash --delete-branch
   ```

6. **Switch Back to RC**
   ```bash
   git checkout rc/X.X.X
   git pull
   ```

### Version Management

**Version is defined in ONE place:** `RoslynMcpServer.csproj`

```xml
<Version>1.0.0-rc</Version>
```

Both `McpServer.cs` and `RoslynTools.cs` read this at runtime via assembly attributes.

**Version Format:**
- Release Candidate: `X.X.X-rc` (e.g., `1.0.0-rc`)
- Release: `X.X.X` (e.g., `1.0.0`)

**The version in MCP server responses MUST match the branch:**
- On `rc/1.0.0` branch → version should be `1.0.0-rc`
- On `release/1.0.0` branch → version should be `1.0.0`

### Release Process (Owner Only)

1. Review cumulative changes in RC branch
2. Merge `rc/X.X.X` → `release/X.X.X`
3. Update version in `.csproj` to remove `-rc` suffix
4. Tag the release
5. Bump RC version for next cycle (e.g., `1.0.1-rc`)

### GitHub CLI Commands Reference

```bash
# Check current default branch
gh repo view --json defaultBranchRef

# Create issue
gh issue create --title "Title" --body "Description"

# Create PR
gh pr create --base rc/X.X.X --title "Title" --body "Closes #N"

# Merge PR
gh pr merge --squash --delete-branch

# View PR status
gh pr status
```

## Available Tools

| Tool | Description |
|------|-------------|
| `roslyn_echo` | Echo test - returns the message you send |
| `roslyn_get_server_info` | Returns server version and capabilities |
| `roslyn_get_projects_in_build_order` | Loads a solution and returns projects in build order (dependencies first) |
| `roslyn_find_symbol` | Semantic search for types, methods, properties by name pattern |
| `roslyn_get_references` | Find all references to a symbol at a given position |
| `roslyn_get_implementations` | Find all implementations of an interface or derived classes |
| `roslyn_get_type_members` | Get all members (methods, properties, fields, events) of a type |
| `roslyn_get_method_body` | Get full source code of a specific method |
| `roslyn_update_method` | Replace a method's implementation with new source code |
| `roslyn_get_diagnostics` | Compile solution and get warnings/errors with counts and details |
| `roslyn_add_member` | Add a new method/property/field to a type with auto-formatting |
| `roslyn_apply_code_fix` | Apply Roslyn's suggested fix for a single diagnostic |
| `roslyn_batch_apply_code_fixes` | Batch apply fixes for all diagnostics of a specific type |
| `roslyn_rename_symbol` | Rename a symbol across the entire solution with all references |

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

### roslyn_get_type_members

Gets all members of a type including methods, properties, fields, events, and constructors. Essential for understanding the structure of large classes without reading the entire file.

**Input:**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "typeName": "FeatureLayer",
  "memberKind": "methods",
  "includeInherited": false,
  "compact": true
}
```

**Parameters:**
- `solutionPath` (required) - Absolute path to .sln file
- `typeName` (required) - Name of the type (class, interface, struct)
- `memberKind` - `all`, `methods`, `properties`, `fields`, `events`, `constructors` (default: `all`)
- `includeInherited` - Include members from base classes (default: false)
- `compact` - Return minimal fields only (default: true)

**Output:**
```json
{
  "success": true,
  "type": { "name": "FeatureLayer", "kind": "Class" },
  "totalMembers": 45,
  "members": [
    { "name": "BuildCachedData", "kind": "Method", "signature": "void BuildCachedData(CancellationToken, bool)" },
    { "name": "Invalidate", "kind": "Method", "signature": "void Invalidate()" }
  ]
}
```

### roslyn_get_method_body

Gets the full source code of a method including its implementation. Returns the complete method text that can be edited and applied back. Essential for working with large classes without reading the entire file.

**Input:**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "typeName": "FeatureLayer",
  "methodName": "BuildCachedData",
  "parameterTypes": "CancellationToken, bool"
}
```

**Parameters:**
- `solutionPath` (required) - Absolute path to .sln file
- `typeName` (required) - Name of the type containing the method
- `methodName` (required) - Name of the method (use `.ctor` for constructors)
- `parameterTypes` - Parameter types to identify overload (e.g., `string, int`)

**Output:**
```json
{
  "success": true,
  "typeName": "FeatureLayer",
  "methodName": "BuildCachedData",
  "filePath": "C:\\path\\to\\FeatureLayer.cs",
  "startLine": 780,
  "endLine": 920,
  "signature": "private void BuildCachedData(CancellationToken cancellation, bool reuseExistingData)",
  "sourceCode": "private void BuildCachedData(...) { ... }"
}
```

### roslyn_update_method

Replaces a method's implementation with new source code. Uses Roslyn to precisely locate and replace the method while preserving surrounding code. Essential for making targeted changes to large classes.

**Input:**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "typeName": "FeatureLayer",
  "methodName": "BuildCachedData",
  "parameterTypes": "CancellationToken, bool",
  "newSourceCode": "private void BuildCachedData(...) { /* new implementation */ }"
}
```

**Parameters:**
- `solutionPath` (required) - Absolute path to .sln file
- `typeName` (required) - Name of the type containing the method
- `methodName` (required) - Name of the method to update
- `newSourceCode` (required) - Complete new method source code
- `parameterTypes` - Parameter types to identify overload

### roslyn_get_diagnostics

Compiles a .NET solution and returns diagnostics (errors, warnings). Without `diagnosticId`, returns summary (counts by diagnostic code). With `diagnosticId`, returns detailed entries with file/line/method info for targeted fixing.

**Input (summary mode):**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "severityFilter": "warning",
  "projectFilter": "Atlas.Controls"
}
```

**Input (detail mode):**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "diagnosticId": "CS8618",
  "maxResults": 10,
  "offset": 0
}
```

**Parameters:**
- `solutionPath` (required) - Absolute path to .sln file
- `diagnosticId` - Specific diagnostic ID to get details for (e.g., `CA2000`, `CS0618`)
- `severityFilter` - `error`, `warning`, `info`, or `all` (default: `all`)
- `projectFilter` - Filter by project name (partial match)
- `maxResults` - Max entries in detail mode (default: 100)
- `offset` - Pagination offset (default: 0)

**Output (summary):**
```json
{
  "success": true,
  "totalErrors": 0,
  "totalWarnings": 1707,
  "summary": [
    { "id": "CS8618", "severity": "Warning", "title": "Non-nullable field...", "count": 601 },
    { "id": "CS8602", "severity": "Warning", "title": "Dereference of possibly null...", "count": 193 }
  ]
}
```

**Output (detail):**
```json
{
  "success": true,
  "entries": [
    {
      "id": "CS8618",
      "message": "Non-nullable field '_cts' must contain...",
      "filePath": "C:\\path\\to\\File.cs",
      "line": 21,
      "column": 16,
      "containingType": "MyClass",
      "containingMethod": "MyMethod"
    }
  ],
  "totalMatchingEntries": 601
}
```

### roslyn_add_member

Adds a new member (method, property, field, constructor, event) to a type. Uses Roslyn Formatter for proper indentation. Smart insertion places members with their peers.

**Input:**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "typeName": "CacheManager",
  "memberCode": "public void Dispose() { _cache.Clear(); }",
  "insertionPoint": "end"
}
```

**Parameters:**
- `solutionPath` (required) - Absolute path to .sln file
- `typeName` (required) - Name of the type to add the member to
- `memberCode` (required) - Complete source code of the member to add
- `insertionPoint` - Where to insert: `start`, `end`, `after-fields`, `after-constructors`, `after-properties`, `before-methods`. Default: smart placement based on member type

**Output:**
```json
{
  "success": true,
  "filePath": "C:\\path\\to\\CacheManager.cs",
  "typeName": "MyNamespace.CacheManager",
  "memberName": "Dispose",
  "memberKind": "Method",
  "insertedAtLine": 45,
  "signature": "void Dispose()"
}
```

### roslyn_apply_code_fix

Applies a Roslyn code fix for a diagnostic at a specific location. First use `roslyn_get_diagnostics` to find issues, then use this tool to automatically fix them.

**Input:**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "filePath": "C:\\path\\to\\MyClass.cs",
  "line": 42,
  "column": 13,
  "diagnosticId": "CS0168",
  "fixIndex": 0,
  "preview": true
}
```

**Parameters:**
- `solutionPath` (required) - Absolute path to .sln file
- `filePath` (required) - Absolute path to the source file
- `line` (required) - Line number (1-based)
- `column` (required) - Column number (1-based)
- `diagnosticId` - Specific diagnostic ID to fix (e.g., `CS0168`)
- `fixIndex` - Index of fix to apply when multiple are available
- `preview` - If true, shows what would change without applying (default: false)

**Output:**
```json
{
  "success": true,
  "filePath": "C:\\path\\to\\MyClass.cs",
  "diagnosticId": "CS0168",
  "diagnosticMessage": "The variable 'ex' is declared but never used",
  "appliedFixTitle": "Remove unused variable",
  "availableFixes": [
    { "index": 0, "title": "Remove unused variable" }
  ],
  "filesChanged": 1,
  "isPreview": false
}
```

**Workflow for single fix:**
1. `roslyn_get_diagnostics` → find CS0168 at line 42
2. `roslyn_apply_code_fix(preview=true)` → see what would change
3. `roslyn_apply_code_fix(preview=false)` → apply the fix

### roslyn_batch_apply_code_fixes

Batch applies Roslyn code fixes for all diagnostics of a specific type. Much faster than applying fixes one by one - loads solution once, applies all fixes in memory, then writes changes to disk.

**Input:**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "diagnosticId": "CS0168",
  "projectFilter": "Atlas.Controls",
  "fileFilter": "*Service.cs",
  "maxFixes": 100,
  "preview": false
}
```

**Parameters:**
- `solutionPath` (required) - Absolute path to .sln file
- `diagnosticId` (required) - Diagnostic ID to fix (e.g., `CS0168`, `CS8618`)
- `projectFilter` - Filter by project name (partial match)
- `fileFilter` - Filter by file name or path (partial match)
- `maxFixes` - Maximum fixes to apply (default: 100, max: 1000)
- `preview` - If true, shows what would change without applying (default: false)

**Output:**
```json
{
  "success": true,
  "solutionPath": "C:\\path\\to\\solution.sln",
  "diagnosticId": "CS0168",
  "totalDiagnosticsFound": 30,
  "diagnosticsWithFixes": 28,
  "fixesApplied": 28,
  "fixesFailed": 2,
  "filesModified": 15,
  "modifiedFiles": ["File1.cs", "File2.cs", "..."],
  "details": [
    {
      "filePath": "C:\\path\\to\\MyClass.cs",
      "line": 42,
      "column": 13,
      "diagnosticMessage": "The variable 'ex' is declared but never used",
      "fixTitle": "Remove unused variable",
      "applied": true
    }
  ],
  "isPreview": false
}
```

**Workflow for batch fixes:**
1. `roslyn_get_diagnostics` → see summary with `fixAvailable: true` flags
2. `roslyn_batch_apply_code_fixes(preview=true)` → see what would change
3. `roslyn_batch_apply_code_fixes(preview=false)` → apply all fixes
4. `roslyn_get_diagnostics` → verify remaining issues

**When to use batch vs single:**
- **Batch (`roslyn_batch_apply_code_fixes`)**: Fixing all occurrences of a specific warning type (e.g., all CS0168)
- **Single (`roslyn_apply_code_fix`)**: Fixing one specific diagnostic, or when you need to choose between multiple fix options

### roslyn_rename_symbol

Renames a symbol (type, method, property, field, parameter, variable) at a specific file position across the entire solution. Updates all references automatically. Essential for safe refactoring without breaking code.

**Input:**
```json
{
  "solutionPath": "C:\\path\\to\\solution.sln",
  "filePath": "C:\\path\\to\\MyClass.cs",
  "line": 15,
  "column": 22,
  "newName": "FetchDataAsync"
}
```

**Parameters:**
- `solutionPath` (required) - Absolute path to .sln file
- `filePath` (required) - Absolute path to the source file containing the symbol
- `line` (required) - Line number (1-based) where the symbol is located
- `column` (required) - Column number (1-based) where the symbol is located
- `newName` (required) - The new name for the symbol

**Output:**
```json
{
  "success": true,
  "originalName": "GetData",
  "newName": "FetchDataAsync",
  "symbolKind": "Method",
  "containingType": "MyNamespace.DataService",
  "totalFilesAffected": 12,
  "totalChanges": 47,
  "affectedFiles": [
    "C:\\path\\to\\DataService.cs",
    "C:\\path\\to\\DataController.cs",
    "C:\\path\\to\\Tests\\DataServiceTests.cs"
  ]
}
```

**Workflow:**
1. Use `roslyn_find_symbol` to locate the symbol you want to rename
2. Call `roslyn_rename_symbol` with the file path, line, column, and new name
3. The tool applies the rename immediately and returns affected files

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

## Roadmap

### High Priority Tools
- [ ] `roslyn_get_call_hierarchy` - Find callers of a method + what it calls (impact analysis)
- [ ] `roslyn_extract_method` - Extract code block into new method
- [ ] `roslyn_change_signature` - Add/remove/reorder parameters with auto-fix callers
- [ ] `roslyn_get_document_symbols` - All symbols in a specific file
- [ ] `roslyn_organize_usings` - Sort + remove unused usings
- [ ] `roslyn_format_document` - Apply .editorconfig formatting
- [ ] `roslyn_extract_interface` - Extract interface from class
- [ ] `roslyn_inline` - Replace variable/method usages with actual code

### Medium Priority Tools
- [ ] `roslyn_move_type_to_file` - Move class to its own .cs file
- [ ] `roslyn_encapsulate_field` - Convert field to property with backing field
- [ ] `roslyn_generate_constructor` - Create constructor from fields/properties
- [ ] `roslyn_generate_equals_hashcode` - Override Equals/GetHashCode
- [ ] `roslyn_pull_members_up` - Move members to base class
- [ ] `roslyn_push_members_down` - Move members to derived classes
- [ ] `roslyn_find_unused_code` - Find dead methods/classes
- [ ] `roslyn_get_dependency_graph` - Analyze assembly/type dependencies

### Infrastructure
- [ ] Caching for compilation results
- [ ] Progress reporting for large solutions
