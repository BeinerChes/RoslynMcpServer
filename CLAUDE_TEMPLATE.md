# CLAUDE.md Template for C# Projects

> **Copy this file to your project root as `CLAUDE.md`** and customize it for your codebase.
> Delete this intro section after copying.

---

# CLAUDE.md - Instructions for Claude Code

## Project Overview

<!-- Customize this section for your project -->
Brief description of what this project does and its purpose.

## Tool Preferences for C# Code

When working with C# files in .NET solutions, **PREFER Roslyn MCP tools over native tools**:

| Task | Use This | NOT This |
|------|----------|----------|
| Find a type/method | `roslyn_find_symbol` | `Grep` or `Glob` |
| Understand a class | `roslyn_get_type_members` | `Read` the whole file |
| Read a method | `roslyn_get_method_body` | `Read` the whole file |
| Edit a method | `roslyn_update_method` | `Edit` with text patterns |
| Add a member | `roslyn_add_member` | `Edit` to insert code |
| Find all references | `roslyn_get_references` | `Grep` for text |
| Find callers only | `roslyn_get_callers` | `roslyn_get_references` (includes non-calls) |
| Find implementations | `roslyn_get_implementations` | `Grep` for class names |
| Check for errors | `roslyn_get_diagnostics` | `Bash` dotnet build |
| Fix one warning | `roslyn_apply_code_fix` | Manual `Edit` |
| Fix many warnings | `roslyn_batch_apply_code_fixes` | Loop of single fixes |
| Rename symbol | `roslyn_rename_symbol` | Manual find/replace |

**Why Roslyn tools?**
- **Semantic understanding** - Knows the difference between `class User` and variable `user`
- **Precise edits** - Updates exactly one method without pattern ambiguity
- **Type-aware** - Finds all implementations of an interface, not just text matches
- **Overload handling** - Distinguishes `Save()` from `Save(bool force)`

**Only use native tools for:**
- Non-C# files (JSON, XML, markdown, .csproj)
- Creating brand new .cs files (use `Write`, then `roslyn_add_member` to populate)
- Very small files (< 100 lines) where `Read`/`Edit` is simpler
- When Roslyn MCP server is not connected

## Code Guidelines

### File Size
- **Keep .cs files under 300 lines.** Large files are hard to maintain and slow to analyze.
- Before splitting, think about proper refactoring:
  1. Extract helper classes or utilities (prefer composition)
  2. Apply SOLID principles (Single Responsibility, etc.)
  3. Use partial classes only as a last resort when logic truly belongs together

### C# Best Practices
- Use modern C# features (primary constructors, collection expressions, pattern matching)
- Prefer `required` properties over constructor parameters for DTOs
- Use `async/await` for all I/O operations
- Error messages should be actionable (tell the user what to do)

### Avoid Over-Engineering
- Only make changes that are directly requested or clearly necessary
- Don't add features, refactor code, or make "improvements" beyond what was asked
- A bug fix doesn't need surrounding code cleaned up
- Don't add error handling for scenarios that can't happen
- Three similar lines of code is better than a premature abstraction

## Test Driven Development (Recommended)

Follow TDD for reliable code changes:

### TDD Workflow
```
1. Write FAILING test(s) first
2. Run tests - verify they FAIL
3. Write minimum code to make tests PASS
4. Refactor if needed (tests must still pass)
5. Commit
```

### Test Naming Convention
```csharp
// Format: MethodName_Scenario_ExpectedResult
[Fact]
public void CalculateTotal_WithEmptyCart_ReturnsZero()

[Fact]
public async Task SaveAsync_WhenDatabaseUnavailable_ThrowsException()
```

### Running Tests
```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~MyServiceTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Common Workflows

### Understanding a Large Class
```
1. roslyn_get_type_members(typeName: "MyClass") → see all members
2. roslyn_get_method_body(typeName: "MyClass", methodName: "DoWork") → read specific method
3. Make targeted changes with roslyn_update_method
```

### Impact Analysis Before Refactoring
```
1. roslyn_find_symbol(pattern: "MyMethod") → find the method
2. roslyn_get_callers(filePath, line, column) → see who CALLS this method
3. Assess impact - these callers need updating if you change the signature
```

**Note:** Use `roslyn_get_callers` for call sites only. Use `roslyn_get_references` when you need ALL mentions (including declarations, docs, interface definitions).

### Fixing Compiler Warnings
```
1. roslyn_get_diagnostics(severityFilter: "warning") → see all warnings
2. roslyn_get_diagnostics(diagnosticId: "CS8618") → get details for specific warning
3. roslyn_batch_apply_code_fixes(diagnosticId: "CS8618") → auto-fix all instances
```

### Safe Rename
```
1. roslyn_find_symbol(pattern: "OldName") → locate the symbol
2. roslyn_rename_symbol(newName: "NewName") → rename everywhere
```

## Project Structure

<!-- Customize this section for your project -->
```
YourProject/
├── CLAUDE.md                 # This file
├── YourProject.sln           # Solution file
├── src/
│   └── YourProject/          # Main project
└── tests/
    └── YourProject.Tests/    # Test project
```

## Build Commands

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run the application
dotnet run --project src/YourProject
```

<!-- Add any project-specific commands here -->
