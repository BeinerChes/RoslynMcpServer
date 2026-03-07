# Roslyn MCP Tools

Hook blocks Read/Edit/Write on .cs files. Use Roslyn tools instead.
Native tools are fine for non-C# files (JSON, XML, markdown, .csproj, etc.), Glob, and Grep for string searches.

## Tool Mapping

**Reading code:**
- See class structure: `GetTypeMembers(typeName)`
- Read method source: `GetMethodBody(typeName, methodName)` — use `parameterTypes` if overloaded
- Find symbol definition: `FindSymbol(pattern)` — falls back to fuzzy match via graph DB
- Find all usages: `GetReferences(filePath, line, column)`
- Find callers only: `GetCallers(symbolName)` — excludes docs/comments/type refs, only actual call sites

**Modifying code:**
- Edit method (full replace): `UpdateMethod(typeName, methodName, newSourceCode)`
- Edit method (targeted): `UpdateMethod(typeName, methodName, oldText, newText)` — oldText must be unique within the method
- Add member: `AddMember(typeName, memberCode)`
- Add using: `AddUsing(typeName, usingDirective)` — sorted, idempotent
- Remove unused usings: `RemoveUnnecessaryUsings(projectFilter?, fileFilter?)` — batch, entire solution
- Create type: `AddType(projectName, typeName, folder?, typeKind?)`
- Delete member: `DeleteMember(typeName, memberName)` — removes XML docs and attributes too
- Rename across solution: `RenameSymbol(filePath, line, column, newName)`
- Extract to method: `ExtractMethod(filePath, startLine, endLine, methodName)` — auto-detects parameters/returns via data flow

**Diagnostics:**
- Check errors: `GetDiagnostics()` — summary. Add `diagnosticId` for details.
- Batch fix: `BatchApplyCodeFixes(diagnosticId)` — does NOT work for CS8019, use `RemoveUnnecessaryUsings` instead
- Single fix: `ApplyCodeFix(filePath, line, column)`
- Find dead code: `FindDeadCode(includePrivate?, includeTests?, maxResults?)` — solution-wide, two-phase (graph DB + Roslyn validation). Use with `DeleteMember` to clean up.

**Knowledge base:**
- Save a learning: `KnowledgeAdd(category, title, content)` — use after fixing tricky bugs or discovering non-obvious behavior. Optional: `symbolLinks`, `tags`, `confidence`
- Search before unfamiliar code: `KnowledgeSearch(query)` — three-layer search: symbol links (exact), FTS (keywords), vector similarity (semantic). Optional: `symbols`, `limit`
- Get full entry: `KnowledgeGet(id)` — returns full content, timestamps, tags, symbol links
- Delete entry: `KnowledgeDelete(id)` — removes entry by ID

**Documentation:**
- Get instructions: `GetInstructions(topic)` — topics: `git`, `plan`, `tools`. Generates hook bypass tokens for git/plan/tools topics.
- Usage analytics: `GetUsageReport(hours?, toolFilter?)` — aggregated tool usage stats. Server-side processing avoids loading large logs into context.

## Gotchas

**Overloaded methods:** `GetMethodBody`, `UpdateMethod`, `DeleteMember` need `parameterTypes: "int, int"` to disambiguate. Without it, you get an error listing available overloads.

**typeName is short name:** Use `"Calculator"` not `"MyApp.Calculator"`. Roslyn searches across the solution.

**UpdateMethod edit mode:** `oldText` must be unique within that method's source. If it matches multiple places, use a longer string or `replaceAll: true`.

**GetReferences vs GetCallers:** GetReferences returns ALL references (declarations, docs, type constraints, etc.). GetCallers returns only actual call sites — use this for impact analysis.

**AddType namespace:** Inferred from `projectName + folder`. Don't specify `namespace` unless you need to override.

**RemoveUnnecessaryUsings vs BatchApplyCodeFixes("CS8019"):** CS8019 code fix requires IDE services. RemoveUnnecessaryUsings is the only way to batch-remove unused usings.

**ExtractMethod limitations:** Cannot extract code containing `return` statements. Code must be inside a method body.

**ApplyCodeFix with multiple fixes:** If multiple fixes available at a location, returns the list. Call again with `fixIndex` to pick one.

## When to Use Native Tools on .cs Files

Sometimes Edit/Write is better:
- Editing comments, regions, or disabled code
- Code Roslyn can't parse (syntax errors)
- Bulk text changes not related to symbols

Get a bypass token: `GetInstructions(topic: "tools")` — valid 1 minute.
