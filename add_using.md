# AddUsing Tool Implementation Trace

Records every operation (tool call) made during implementation of the AddUsing MCP tool.

## Goal
Add a new `AddUsing` tool that adds a using directive to a C# file, identified by type name.

---

## Operations Log

### Phase 1: Understanding Existing Patterns

**Op 1: FindSymbol** — Find how other tools are registered to follow the pattern
- Tool: `FindSymbol(pattern: "RegisterAddMemberTool", symbolKind: "member")`
- Purpose: Understand registration pattern for new tools

**Op 2: GetMethodBody** — Read the AddMember registration to copy the pattern
- Tool: `GetMethodBody(typeName: "RoslynTools", methodName: "RegisterAddMemberTool")`
- Purpose: Get the tool registration boilerplate

**Op 3: FindSymbol** — Find existing using-manipulation code in Roslyn
- Tool: `FindSymbol(pattern: "UsingDirective", symbolKind: "all")`
- Purpose: See if there's existing code for manipulating usings

**Op 4: GetMethodBody** — Read RemoveUnnecessaryUsings to understand using manipulation
- Tool: `GetMethodBody(typeName: "RoslynTools", methodName: "RegisterRemoveUsingsTool")`
- Purpose: Understand how usings are currently handled

**Op 5: GetTypeMembers** — Check SolutionAnalyzerService for existing methods
- Tool: `GetTypeMembers(typeName: "SolutionAnalyzerService")`
- Purpose: See what service methods exist, find patterns to follow

**Op 6: GetMethodBody** — Read AddMember registration for boilerplate
- Tool: `GetMethodBody(typeName: "RoslynTools", methodName: "RegisterAddMemberTool")`
- Purpose: Copy tool registration pattern (InputSchema, args parsing, result serialization)

**Op 7: FindSymbol** — Find RemoveUsings registration (name was wrong first try)
- Tool: `FindSymbol(pattern: "RemoveUsings", symbolKind: "member")`
- Failed, then: `Grep(pattern: "RemoveUnnecessaryUsings", path: "src/", glob: "RoslynTools*.cs")`

**Op 8: GetMethodBody** — Read RemoveUsings registration
- Tool: `GetMethodBody(typeName: "RoslynTools", methodName: "RegisterRemoveUnnecessaryUsingsTool")`
- Purpose: Understand tool handler pattern for using-related tools

**Op 9: GetMethodBody** — Read RemoveUnnecessaryUsingsAsync service method
- Tool: `GetMethodBody(typeName: "SolutionAnalyzerService", methodName: "RemoveUnnecessaryUsingsAsync")`
- Purpose: Understand how to manipulate usings via Roslyn syntax tree

**Op 10: FindSymbol** — Find result type pattern
- Tool: `FindSymbol(pattern: "AddMemberResult", symbolKind: "type")`
- Purpose: Find where result types are defined

**Op 11: GetTypeMembers** — Read AddMemberResult structure
- Tool: `GetTypeMembers(typeName: "AddMemberResult")`
- Purpose: Copy result type pattern (Success, Error, FilePath, etc.)

**Op 12: Grep** — Find tool registration wiring
- Tool: `Grep(pattern: "Register.*Tool(server)", path: "src/RoslynTools.cs")`
- Purpose: Find where to add RegisterAddUsingTool(server) call

**Op 13: Grep** — Check SolutionAnalyzerService class modifiers
- Tool: `Grep(pattern: "class SolutionAnalyzerService", path: "src/SolutionAnalyzerService.cs")`
- Purpose: Check if static/partial to match my new partial file

**Op 14: Grep** — Find SymbolFinder using import
- Tool: `Grep(pattern: "^using", path: "src/SolutionAnalyzerService.AddMember.cs")`
- Purpose: Find required using for SymbolFinder

### Phase 2: Implementation

**Op 15: AddType** — Create AddUsingResult class
- Tool: `AddType(projectName: "RoslynMcpServer", typeName: "AddUsingResult")`
- Created: `AddUsingResult.cs`

**Op 16-20: AddMember x5** — Add properties to result type
- Tool: `AddMember(typeName: "AddUsingResult", memberCode: "public bool Success { get; set; }")` etc.
- Added: Success, Error, FilePath, UsingDirective, AlreadyExists properties

**Op 21: Bash(cat)** — Write SolutionAnalyzerService.AddUsing.cs
- Wrote the service method (145 lines) with:
  - Namespace normalization (strips "using " prefix, ";" suffix)
  - File lookup by type name (via SymbolFinder) or file path
  - Duplicate detection (returns AlreadyExists=true)
  - Sorted insertion among existing usings
  - File write-back

**Op 22: Bash(cat)** — Write RoslynTools.AddUsing.cs
- Wrote the tool registration (95 lines) with:
  - Parameters: typeName, usingDirective, filePath
  - Validation: usingDirective required, either typeName or filePath required
  - Idempotent annotation

**Op 23: Bash(sed)** — Wire up in RoslynTools.cs
- Added `RegisterAddUsingTool(server);` after RemoveUnnecessaryUsings registration

**Op 24: Bash(sed)** — Fix partial class declaration
- Changed `public static partial class` to `public partial class` (match existing)

**Op 25: Bash(sed)** — Add missing using
- Added `using Microsoft.CodeAnalysis.FindSymbols;`

**Op 26: Bash(dotnet build)** — Build succeeded

### Phase 3: Verification

**Op 27: AddUsing** — Test adding a new using by type name
- Tool: `AddUsing(typeName: "RoslynTools", usingDirective: "System.Text.RegularExpressions")`
- Result: success, added to RoslynTools.AddMember.cs

**Op 28: AddUsing** — Test idempotency (same using again)
- Tool: `AddUsing(typeName: "RoslynTools", usingDirective: "System.Text.RegularExpressions")`
- Result: success, alreadyExists=true

**Op 29: AddUsing** — Test "using" prefix stripping
- Tool: `AddUsing(typeName: "SharpOpsCompiler", usingDirective: "using System.Diagnostics;")`
- Result: success, correctly stripped "using " and ";"

**Op 30: git checkout** — Revert test changes

---

## Summary: All Operations

| # | Tool | Purpose | Could Model Help? |
|---|------|---------|-------------------|
| 1 | FindSymbol | Find registration pattern | No - research |
| 2 | GetMethodBody | Read AddMember registration | No - research |
| 3 | FindSymbol | Find UsingDirective code | No - research |
| 4 | GetMethodBody | Read RemoveUsings registration | No - research |
| 5 | GetTypeMembers | List SolutionAnalyzerService methods | No - research |
| 6 | GetMethodBody | Read AddMember registration boilerplate | No - research |
| 7 | FindSymbol+Grep | Find RemoveUsings registration name | No - research |
| 8 | GetMethodBody | Read RemoveUsings registration | No - research |
| 9 | GetMethodBody | Read RemoveUsingsAsync implementation | No - research |
| 10 | FindSymbol | Find result type location | No - research |
| 11 | GetTypeMembers | Read result type structure | No - research |
| 12 | Grep | Find registration wiring location | No - research |
| 13 | Grep | Check class modifiers | No - research |
| 14 | Grep | Find required usings | No - research |
| 15 | AddType | Create result type | Maybe - boilerplate |
| 16-20 | AddMember x5 | Add properties | Maybe - boilerplate |
| 21 | Write file | Service implementation | **Yes** - core logic |
| 22 | Write file | Tool registration | Maybe - boilerplate |
| 23 | sed | Wire registration | No - one-liner |
| 24 | sed | Fix class modifier | No - bug fix |
| 25 | sed | Add missing using | No - bug fix |
| 26 | dotnet build | Verify | No - build step |
