# Roslyn MCP Tool Preferences

## Context

You have access to Roslyn MCP tools for C# code analysis. These tools provide semantic understanding of code, unlike text-based search.

## All Available Tools

### Code Navigation & Understanding

| Tool | Purpose |
|------|---------|
| `roslyn_find_symbol` | Find types, methods, properties by name pattern |
| `roslyn_get_type_members` | See all members of a class (methods, properties, fields) |
| `roslyn_get_method_body` | Read full source code of a specific method |
| `roslyn_get_references` | Find all references to a symbol |
| `roslyn_get_callers` | Find who calls a method (call sites only) |
| `roslyn_get_implementations` | Find implementations of interface or derived classes |

### Code Modification

| Tool | Purpose |
|------|---------|
| `roslyn_update_method` | Replace a method's implementation |
| `roslyn_add_member` | Add new method/property/field to a type |
| `roslyn_rename_symbol` | Rename across entire solution |

### Diagnostics & Fixes

| Tool | Purpose |
|------|---------|
| `roslyn_get_diagnostics` | Get compiler errors and warnings |
| `roslyn_apply_code_fix` | Apply Roslyn's suggested fix for one diagnostic |
| `roslyn_batch_apply_code_fixes` | Fix all diagnostics of a specific type |

### Call Graph (Cached Analysis)

| Tool | Purpose |
|------|---------|
| `roslyn_graph_status` | Check if call graph exists for solution |
| `roslyn_graph_analyze` | Build/update call graph database |
| `roslyn_query_graph` | Query callers/callees with recursive depth |

### Solution & Project

| Tool | Purpose |
|------|---------|
| `roslyn_get_projects_in_build_order` | List projects in dependency order |
| `roslyn_get_server_info` | Server version and capabilities |
| `roslyn_echo` | Test connectivity |

### Instructions

| Tool | Purpose |
|------|---------|
| `roslyn_get_template` | Get CLAUDE.md template for projects |
| `roslyn_get_instructions` | Get topic-specific development instructions |

## Tool Selection Guide

| Task | Roslyn Tool | Why NOT native tool |
|------|-------------|---------------------|
| Find type/method | `roslyn_find_symbol` | Grep finds text, not symbols |
| See class structure | `roslyn_get_type_members` | Read shows raw text, not structure |
| Read a method | `roslyn_get_method_body` | Read requires knowing line numbers |
| Edit a method | `roslyn_update_method` | Edit can break code with text patterns |
| Add new member | `roslyn_add_member` | Edit doesn't format or place correctly |
| Find usages | `roslyn_get_references` | Grep finds text matches, not usages |
| Find callers | `roslyn_get_callers` | References includes non-calls |
| Find callers (large codebase) | `roslyn_query_graph` | Faster with cached graph |
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

### Impact analysis (large codebase)
1. `roslyn_graph_analyze(solutionPath)` → build/update call graph (first time)
2. `roslyn_query_graph(symbolName, direction: "callers", maxDepth: 3)` → find all callers recursively
3. Much faster than `roslyn_get_callers` for repeated queries

### Fixing compiler warnings
1. `roslyn_get_diagnostics(severityFilter: "warning")` → see all warnings
2. `roslyn_get_diagnostics(diagnosticId: "CS8618")` → get details
3. `roslyn_batch_apply_code_fixes(diagnosticId: "CS8618")` → auto-fix
