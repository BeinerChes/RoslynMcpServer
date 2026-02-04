# Roslyn Tools for C#

Hook blocks Read/Edit on .cs files. Use Roslyn tools instead.

## Quick Reference

| Task | Tool |
|------|------|
| Find symbol | `FindSymbol(pattern)` |
| See class structure | `GetTypeMembers(typeName)` |
| Read method | `GetMethodBody(typeName, methodName)` |
| Edit method | `UpdateMethod(typeName, methodName, newSourceCode, comment?)` |
| Add member | `AddMember(typeName, memberCode, comment?)` |
| Create type | `AddType(projectName, typeName)` |
| Delete member | `DeleteMember(typeName, memberName)` |
| Find usages | `GetReferences(filePath, line, column)` |
| Find callers | `GetCallers(filePath, line, column)` |
| Find implementations | `GetImplementations(typeName)` |
| Rename | `RenameSymbol(filePath, line, column, newName)` |
| Check errors | `GetDiagnostics()` |
| Fix warnings | `BatchApplyCodeFixes(diagnosticId)` |

## Native Tools OK For

- Non-C# files (JSON, XML, markdown, .csproj)
- Glob for file discovery
- Grep for non-symbol searches (strings, comments, config)

## Adding Methods (auto-generation)

When adding a new method, ALWAYS use AddMember with `auto=true` first:
```
AddMember(typeName: "MyClass", memberCode: "public int Add(int a, int b)", auto: true)
```

The built-in model generates the method body. **You MUST review the result:**
- If `autoGenerationFailed=true` → model failed, call `UpdateMethod` with your implementation
- If `autoGenerationFailed=false` → **read `generatedCode` carefully**:
  - Code is correct → done, move on
  - Code is wrong or nonsensical → call `UpdateMethod` with the correct implementation

**Why this matters:** Both `AddMember` and `UpdateMethod` collect finetune training data.
When you correct bad model output via `UpdateMethod`, your correction becomes a training
example that improves the model over time. Always let the model try first, then fix.

Only use `auto=false` for non-method members (fields, properties, events).

## Patterns

**Explore class → edit method:**
```
GetTypeMembers(typeName)
GetMethodBody(typeName, methodName)
UpdateMethod(typeName, methodName, newCode)
```

**Create new type with members:**
```
AddType(projectName, typeName, folder: "Services")
AddMember(typeName, "public void DoThing() { }", comment: "Does the thing")
```

**XML doc comments:**
- `AddMember`: Pass `comment` for summary text. Public members get XML doc stubs automatically.
- `UpdateMethod`: Pass `comment` to replace/add XML doc on the method.

**Impact before refactoring:**
```
GetCallers(filePath, line, column)
```

## Knowledge Base

| When | Do |
|------|----|
| Learned something non-obvious | `KnowledgeAdd(category: "lesson", ...)` |
| Fixed tricky bug | `KnowledgeAdd(category: "error-resolution", ...)` |
| Starting unfamiliar code | `KnowledgeSearch(query)` |
