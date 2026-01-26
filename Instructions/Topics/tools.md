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
| `roslyn_get_references` | Find all references to any symbol (types, methods, etc.) |
| `roslyn_get_callers` | Find call sites of a method/property/event (NOT types - use references) |
| `roslyn_get_implementations` | Find implementations of interface or derived classes |

### Code Modification

| Tool | Purpose |
|------|---------|
| `roslyn_update_method` | Replace a method's implementation |
| `roslyn_add_member` | Add new method/property/field to a type |
| `roslyn_add_type` | Create new class/interface/struct/record/enum in a project |
| `roslyn_delete_member` | Delete a method/property/field from a type |
| `roslyn_rename_symbol` | Rename across entire solution |
| `roslyn_extract_method` | Extract code block into new method with data flow analysis |

### Diagnostics & Fixes

| Tool | Purpose |
|------|---------|
| `roslyn_get_diagnostics` | Get compiler errors and warnings |
| `roslyn_apply_code_fix` | Apply Roslyn's suggested fix for one diagnostic |
| `roslyn_batch_apply_code_fixes` | Fix all diagnostics of a specific type |
| `roslyn_remove_unnecessary_usings` | Remove CS8019 (unnecessary using) directives from files |

### Call Graph (Cached Analysis)

| Tool | Purpose |
|------|---------|
| `roslyn_graph_status` | Check if call graph exists for solution |
| `roslyn_graph_analyze` | Build/update call graph database |
| `roslyn_query_graph` | Query callers/callees with recursive depth (auto-refreshes stale files) |
| `roslyn_graph_impact` | Analyze blast radius if a symbol changes (auto-refreshes stale files) |
| `roslyn_find_dead_code` | Find methods/properties with no callers (may have false positives*) |

*Dead code detection: By default, excludes properties on pure model/DTO classes (detected by structure: only auto-properties, no methods). Optional `excludeTypePatterns` and `excludeFilePatterns` parameters available for additional filtering.

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
| `roslyn_get_instructions` | Get topic-specific development instructions (see note below) |

**Important:** When calling `roslyn_get_instructions` with `plan`, `git`, or `tools` topics, the `solutionPath` parameter is **required** to generate per-solution tokens for hook validation. Find the solution file first with glob pattern `*.sln*`.

### Knowledge Base (Semantic Search)

| Tool | Purpose |
|------|---------|
| `roslyn_knowledge_add` | Add gotchas, patterns, or insights linked to symbols |
| `roslyn_knowledge_search` | Semantic search (returns compact results: id, title, category, tags) |
| `roslyn_knowledge_list` | List entries (returns compact results: id, title, category, tags) |
| `roslyn_knowledge_get` | Get full content of a single entry by ID |
| `roslyn_knowledge_delete` | Delete an entry by ID |
| `roslyn_knowledge_for_symbol` | Get all knowledge linked to a specific symbol (full content) |

Knowledge entries support:
- **Code-specific categories**: gotcha, pattern, architecture, debugging, performance, security, testing, workaround
- **Session learning categories**: lesson, error-resolution, convention, instruction
- **Symbol links**: Associate knowledge with specific methods, classes, or namespaces
- **Tags**: Free-form tags for additional categorization
- **Confidence levels**: 0.0-1.0 for uncertain vs verified learnings
- **Semantic search**: Uses embeddings (all-MiniLM-L6-v2) for "what did we learn about caching?" style queries

**When to add session learnings:**
- `lesson`: "Oh, that's how it works" - non-obvious codebase behaviors
- `error-resolution`: After troubleshooting an error (2+ attempts to fix)
- `convention`: When user corrects your approach ("no, we do it this way")
- `instruction`: General workflow guidance that would help future sessions

## Tool Selection Guide

| Task | Roslyn Tool | Why NOT native tool |
|------|-------------|---------------------|
| Find type/method | `roslyn_find_symbol` | Grep finds text, not symbols |
| See class structure | `roslyn_get_type_members` | Read shows raw text, not structure |
| Read a method | `roslyn_get_method_body` | Read requires knowing line numbers |
| Edit a method | `roslyn_update_method` | Edit can break code with text patterns |
| Add new member | `roslyn_add_member` | Edit doesn't format or place correctly |
| Create new type | `roslyn_add_type` | Write doesn't know project namespace/folder conventions |
| Delete member | `roslyn_delete_member` | Edit may miss attributes, XML docs, trivia |
| Find usages (any symbol) | `roslyn_get_references` | Grep finds text matches, not usages |
| Find callers (methods/props) | `roslyn_get_callers` | References includes non-calls |
| Find callers (large codebase) | `roslyn_query_graph` | Faster with cached graph |
| Impact analysis | `roslyn_graph_impact` | Shows transitive callers grouped by file |
| Find dead code | `roslyn_find_dead_code` | Automated detection of unused methods |
| Find implementations | `roslyn_get_implementations` | Grep can't follow inheritance |
| Check errors | `roslyn_get_diagnostics` | dotnet build output is harder to parse |
| Fix warning | `roslyn_apply_code_fix` | Manual edit may introduce errors |
| Fix many warnings | `roslyn_batch_apply_code_fixes` | One-by-one is slow |
| Remove unused usings | `roslyn_remove_unnecessary_usings` | CS8019 has no batch code fix provider |
| Rename | `roslyn_rename_symbol` | Find/replace misses some references |
| Extract method | `roslyn_extract_method` | Manual extraction misses params/returns |
| Document a gotcha | `roslyn_knowledge_add` | Comments get lost, knowledge persists |
| Find past learnings | `roslyn_knowledge_search` | Semantic search finds related concepts |

## When to Use Native Tools

Use native tools (Read, Edit, Grep, Glob) only for:
- Non-C# files: JSON, XML, YAML, markdown, .csproj
- Very small files: < 100 lines where Read/Edit is simpler
- When MCP server is not connected

**Note:** For new C# files, prefer `roslyn_add_type` which handles namespace inference and proper file placement.

## Common Workflows

### Creating a new type
1. `roslyn_add_type(projectName, typeName, typeKind, folder)` → creates file with proper namespace
2. `roslyn_add_member(typeName, memberCode)` → add methods/properties as needed

Example: Create a new service class
```
roslyn_add_type(projectName: "MyApp.Core", typeName: "UserService", folder: "Services", baseTypes: "IUserService")
```
Creates `MyApp.Core/Services/UserService.cs` with namespace `MyApp.Core.Services`.

### Understanding a large class
1. `roslyn_get_type_members(typeName)` → see all members
2. `roslyn_get_method_body(typeName, methodName)` → read specific method
3. `roslyn_update_method(...)` → make targeted change

### Extracting a method (refactoring)
1. `roslyn_get_method_body(typeName, methodName)` → read the method to identify lines
2. `roslyn_extract_method(filePath, startLine, endLine, methodName)` → extract code
3. Roslyn analyzes data flow: parameters (variables used) and returns (variables assigned)
4. Creates new method and replaces original code with call site

### Impact analysis before refactoring
1. `roslyn_find_symbol(pattern)` → locate the symbol
2. `roslyn_get_callers(filePath, line, column)` → see who calls it
3. Assess impact before changing signature

### Impact analysis (large codebase)
1. `roslyn_graph_analyze(solutionPath)` → build/update call graph (first time)
2. `roslyn_query_graph(symbolName, direction: "callers", maxDepth: 3)` → find all callers recursively
3. Much faster than `roslyn_get_callers` for repeated queries

### Impact analysis before refactoring (blast radius)
1. `roslyn_graph_analyze(solutionPath)` → build call graph if needed
2. `roslyn_graph_impact(symbolName)` → see all affected files and methods
3. Results grouped by file for easy review

### Finding dead code
1. `roslyn_graph_analyze(solutionPath)` → build call graph
2. `roslyn_find_dead_code(solutionPath)` → find unused methods/properties
3. Review results - automatically excludes:
   - Entry points (Main, RunAsync, event handlers)
   - Properties with attributes (likely serialization)
   - Properties on pure model/DTO classes (structural detection)
   - External/BCL symbols
4. Optional parameters for additional filtering:
   - `excludePureModelClasses: false` to include DTO properties
   - `excludeTypePatterns: ["Result", "Response"]` for name-based exclusion
   - `excludeFilePatterns: ["Models/"]` for path-based exclusion

### Cleaning up dead code
1. `roslyn_find_dead_code(solutionPath)` → identify unused members
2. Review each result to confirm it's truly dead (not reflection-based)
3. `roslyn_delete_member(typeName, memberName)` → remove confirmed dead code
4. Includes attributes and XML docs in deletion

### Fixing compiler warnings
1. `roslyn_get_diagnostics(severityFilter: "warning")` → see all warnings
2. `roslyn_get_diagnostics(diagnosticId: "CS8618")` → get details
3. `roslyn_batch_apply_code_fixes(diagnosticId: "CS8618")` → auto-fix

### Removing unnecessary usings (CS8019)
1. `roslyn_remove_unnecessary_usings(solutionPath, preview: true)` → preview what will be removed
2. `roslyn_remove_unnecessary_usings(solutionPath)` → remove all unnecessary usings
3. Automatically skips generated files in obj/ folders

**Note:** CS8019 (unnecessary using) requires this dedicated tool because Roslyn's code fix provider for it needs IDE services not available in batch mode.

### Documenting code learnings
1. After fixing a tricky bug or discovering a gotcha:
2. `roslyn_knowledge_add(category: "gotcha", title: "...", content: "...", symbolLinks: ["Namespace.Class.Method"])` → save the learning
3. Next time you work on that symbol, knowledge is searchable

### Finding relevant knowledge
1. `roslyn_knowledge_search(query: "caching performance")` → semantic search (compact results)
2. `roslyn_knowledge_get(id: 5)` → fetch full content for interesting result
3. `roslyn_knowledge_for_symbol(symbolName: "FeatureLayer.BuildCache")` → exact match (full content)
4. `roslyn_knowledge_list(category: "gotcha")` → browse by category (compact results)
