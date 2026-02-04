# Roslyn Tools for C#

Hook blocks Read/Edit on .cs files. Use Roslyn tools instead.

## Quick Reference

| Task | Tool |
|------|------|
| Find symbol | `roslyn_find_symbol(pattern)` |
| See class structure | `roslyn_get_type_members(typeName)` |
| Read method | `roslyn_get_method_body(typeName, methodName)` |
| Edit method | `roslyn_update_method(typeName, methodName, newSourceCode, comment?)` |
| Add member | `roslyn_add_member(typeName, memberCode, comment?)` |
| Create type | `roslyn_add_type(projectName, typeName)` |
| Delete member | `roslyn_delete_member(typeName, memberName)` |
| Find usages | `roslyn_get_references(filePath, line, column)` |
| Find callers | `roslyn_get_callers(filePath, line, column)` |
| Find implementations | `roslyn_get_implementations(typeName)` |
| Rename | `roslyn_rename_symbol(filePath, line, column, newName)` |
| Check errors | `roslyn_get_diagnostics()` |
| Fix warnings | `roslyn_batch_apply_code_fixes(diagnosticId)` |

## Native Tools OK For

- Non-C# files (JSON, XML, markdown, .csproj)
- Glob for file discovery
- Grep for non-symbol searches (strings, comments, config)

## Patterns

**Explore class → edit method:**
```
roslyn_get_type_members(typeName)
roslyn_get_method_body(typeName, methodName)
roslyn_update_method(typeName, methodName, newCode)
```

**Create new type with members:**
```
roslyn_add_type(projectName, typeName, folder: "Services")
roslyn_add_member(typeName, "public void DoThing() { }", comment: "Does the thing")
```

**XML doc comments:**
- `roslyn_add_member`: Pass `comment` for summary text. Public members get XML doc stubs automatically.
- `roslyn_update_method`: Pass `comment` to replace/add XML doc on the method.

**Impact before refactoring:**
```
roslyn_get_callers(filePath, line, column)
```

## Knowledge Base

| When | Do |
|------|----|
| Learned something non-obvious | `roslyn_knowledge_add(category: "lesson", ...)` |
| Fixed tricky bug | `roslyn_knowledge_add(category: "error-resolution", ...)` |
| Starting unfamiliar code | `roslyn_knowledge_search(query)` |
