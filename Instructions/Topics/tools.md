# Roslyn MCP Tool Preferences

## Context

You have access to Roslyn MCP tools for C# code analysis. These tools provide semantic understanding of code, unlike text-based search.

## Tool Selection Guide

Use this guide to select the right tool:

| Task | Roslyn Tool | Why NOT native tool |
|------|-------------|---------------------|
| Find type/method | `roslyn_find_symbol` | Grep finds text, not symbols |
| See class structure | `roslyn_get_type_members` | Read shows raw text, not structure |
| Read a method | `roslyn_get_method_body` | Read requires knowing line numbers |
| Edit a method | `roslyn_update_method` | Edit can break code with text patterns |
| Add new member | `roslyn_add_member` | Edit doesn't format or place correctly |
| Find usages | `roslyn_get_references` | Grep finds text matches, not usages |
| Find callers | `roslyn_get_callers` | References includes non-calls |
| Find implementations | `roslyn_get_implementations` | Grep can't follow inheritance |
| Check errors | `roslyn_get_diagnostics` | dotnet build output is harder to parse |
| Fix warning | `roslyn_apply_code_fix` | Manual edit may introduce errors |
| Fix many warnings | `roslyn_batch_apply_code_fixes` | One-by-one is slow |
| Rename | `roslyn_rename_symbol` | Find/replace misses some references |

## When to Use Native Tools

Use native tools (Read, Edit, Grep, Glob) only for:
- Non-C# files: JSON, XML, YAML, markdown, .csproj
- New files: Use Write to create, then `roslyn_add_member` to populate
- Very small files: < 100 lines where Read/Edit is simpler
- When MCP server is not connected

## Common Workflows

### Understanding a large class
1. `roslyn_get_type_members(typeName)` → see all members
2. `roslyn_get_method_body(typeName, methodName)` → read specific method
3. `roslyn_update_method(...)` → make targeted change

### Impact analysis before refactoring
1. `roslyn_find_symbol(pattern)` → locate the symbol
2. `roslyn_get_callers(filePath, line, column)` → see who calls it
3. Assess impact before changing signature

### Fixing compiler warnings
1. `roslyn_get_diagnostics(severityFilter: "warning")` → see all warnings
2. `roslyn_get_diagnostics(diagnosticId: "CS8618")` → get details
3. `roslyn_batch_apply_code_fixes(diagnosticId: "CS8618")` → auto-fix
