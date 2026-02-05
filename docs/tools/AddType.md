# AddType

## Description

AddType creates a new type (class, interface, struct, record, or enum) in a specified project. It automatically determines the file location based on project structure, infers the namespace from the project name and optional folder path, and creates the file with proper formatting.

The tool handles all the complexity of:
- Finding the project directory
- Creating subfolder hierarchy if needed
- Inferring namespace from project name + folder structure
- Generating properly formatted type declaration
- Supporting modifiers (public/internal/private/protected, partial, sealed, static)
- Adding base types and interfaces

## Comparison with Native Claude Code Tools

### vs Write tool
- **Write** requires manually determining the full file path, creating directories, crafting the namespace, formatting the type declaration, and writing the complete file
- **AddType** takes just project name and type name, handles everything automatically
- **Write** needs you to know project structure and namespace conventions
- **AddType** infers namespace from project name + folder automatically
- **Write** requires manual directory creation if folders don't exist
- **AddType** creates directory structure automatically

### vs Bash + Write combo
Typical manual workflow:
1. Use Bash `find` to locate project directory
2. Use Bash `mkdir -p` to create folders
3. Craft namespace string manually
4. Format type declaration with proper syntax
5. Use Write to create the file

**AddType** does all 5 steps in one call.

### When to use AddType
- Creating new types in existing projects
- Following project namespace conventions automatically
- Creating types in subfolders with proper namespace inference
- Ensuring consistent formatting across the solution
- When you don't want to manually track project directory structures

## Real-World Example

### Scenario
Create a new `UserService` class in the `SharpOps.Examples` project under a `Services` subfolder.

**Target:**
```csharp
namespace SharpOps.Examples.Services;

public class UserService
{
}
```

### Approach 1: Using Native Write Tool

**Step 1: Find the project directory**
```
Bash("find . -name 'SharpOps.Examples.csproj' -type f")
```
**Result:** `./SharpOps.Examples/SharpOps.Examples.csproj`
**Tokens:** ~80

**Step 2: Determine namespace**
You must know the convention: `ProjectName.FolderPath`
For `SharpOps.Examples` project + `Services` folder = `SharpOps.Examples.Services`

**Step 3: Create directory if needed**
```
Bash("mkdir -p SharpOps.Examples/Services")
```
**Tokens:** ~50

**Step 4: Write the file with full content**
```
Write(
  file_path: "D:\repos\RoslynMcpServer\SharpOps.Examples\Services\UserService.cs",
  content: "namespace SharpOps.Examples.Services;

public class UserService
{
}
"
)
```
**Tokens:** ~180

**Total: ~310 tokens, 3-4 steps**

**Risks:**
- Must manually construct correct namespace
- Must know exact file path structure
- Easy to get namespace convention wrong
- Must handle directory creation separately
- No validation of type name syntax

### Approach 2: Using AddType (Roslyn)

**Step 1: Create the type**
```
AddType(
  projectName: "SharpOps.Examples",
  typeName: "UserService",
  folder: "Services"
)
```
**Total: ~60 tokens, 1 step**

**Benefits:**
- No need to know project directory path
- Namespace inferred automatically (SharpOps.Examples.Services)
- Directory created if doesn't exist
- Type name validated
- Proper formatting guaranteed

### Comparison Summary

| Aspect | Native Write + Bash | AddType (Roslyn) |
|--------|---------------------|------------------|
| **Token usage** | ~310 | ~60 |
| **Steps required** | 3-4 (find → mkdir → write) | 1 |
| **Project path needed** | Yes (must find it) | No (just project name) |
| **Namespace inference** | Manual | Automatic |
| **Directory creation** | Manual (mkdir) | Automatic |
| **Type name validation** | No | Yes |
| **Formatting** | Manual | Automatic |
| **Error risk** | High | Low |

**AddType uses 81% fewer tokens** (60 vs 310)

### Advanced Example: Interface with Base Types

**Create `IUserRepository` interface extending `IDisposable`:**

**Native approach:** ~400 tokens (find project, craft interface syntax, write)

**AddType approach:**
```
AddType(
  projectName: "MyApp.Core",
  typeName: "IUserRepository",
  typeKind: "interface",
  folder: "Repositories",
  baseTypes: "IDisposable"
)
```
**Total: ~80 tokens**

**Result:**
```csharp
namespace MyApp.Core.Repositories;

public interface IUserRepository : IDisposable
{
}
```

### When Write is Better
- Creating non-C# files (JSON, XML, config)
- Files outside of project structure
- Templates or generated content
- When you need complete control over namespace/location

## How It Works

### Type Discovery and Location
The tool takes a project name and uses Roslyn to load the solution, then searches for a matching project by name (case-insensitive). Once found, it determines the project directory from the .csproj file location.

If a `folder` parameter is provided (e.g., `"Services/Auth"`), the tool:
1. Constructs the full directory path: `<projectDir>/<folder>`
2. Creates any missing directories in the hierarchy
3. Infers the namespace by appending folder parts to project name: `ProjectName.Services.Auth`

If no `folder` is provided, the file is created in the project root directory.

### Namespace Inference
The namespace follows .NET conventions:
- **Project root:** namespace = project name
- **Subfolder:** namespace = project name + folder path (dots replace slashes)
- **Custom:** explicitly provide `namespace` parameter to override

Examples:
- Project: `MyApp.Core`, Folder: `null` → `MyApp.Core`
- Project: `MyApp.Core`, Folder: `Services` → `MyApp.Core.Services`
- Project: `MyApp.Core`, Folder: `Services/Auth` → `MyApp.Core.Services.Auth`

You can override this by providing the `namespace` parameter explicitly.

### Type Generation
The tool uses Roslyn's syntax factory to construct the type declaration:

1. **Type kind** - Creates ClassDeclarationSyntax, InterfaceDeclarationSyntax, StructDeclarationSyntax, RecordDeclarationSyntax, or EnumDeclarationSyntax
2. **Accessibility** - Adds public/internal/private/protected modifier (default: public)
3. **Modifiers** - Adds partial, sealed, or static keywords if requested
4. **Base types** - Parses comma-separated base class and interfaces, adds to base list
5. **Formatting** - Applies Roslyn formatter for consistent indentation and spacing

Generated structure:
```csharp
namespace <InferredOrExplicitNamespace>;

<accessibility> <modifiers> <typeKind> <typeName> <: baseTypes>
{
}
```

The file is created with proper using statements positioning (namespace comes first), proper line breaks, and consistent formatting.

### Supported Type Kinds

| Type Kind | Example | Use Case |
|-----------|---------|----------|
| **class** (default) | `public class UserService { }` | Regular classes, services, entities |
| **interface** | `public interface IUserService { }` | Contracts, abstractions |
| **struct** | `public struct Point { }` | Value types, small data structures |
| **record** | `public record User { }` | Immutable data objects (C# 9+) |
| **enum** | `public enum Status { }` | Enumerations |

### Modifiers

| Modifier | Applies To | Example |
|----------|------------|---------|
| **partial** | class, interface, struct, record | `public partial class User { }` |
| **sealed** | class, record | `public sealed class UserService { }` |
| **static** | class only | `public static class Utils { }` |

### Common Workflows

**Create service class:**
```
AddType(
  projectName: "MyApp.Services",
  typeName: "UserService",
  folder: "Users"
)
```

**Create interface:**
```
AddType(
  projectName: "MyApp.Core",
  typeName: "IUserRepository",
  typeKind: "interface",
  folder: "Repositories"
)
```

**Create record with base type:**
```
AddType(
  projectName: "MyApp.Domain",
  typeName: "UserCreatedEvent",
  typeKind: "record",
  baseTypes: "DomainEvent",
  folder: "Events"
)
```

**Create sealed class:**
```
AddType(
  projectName: "MyApp.Services",
  typeName: "UserService",
  isSealed: true
)
```

**Create static utility class:**
```
AddType(
  projectName: "MyApp.Utilities",
  typeName: "StringHelpers",
  isStatic: true
)
```

**Create enum:**
```
AddType(
  projectName: "MyApp.Domain",
  typeName: "UserStatus",
  typeKind: "enum"
)
```

### Following Up with AddMember

After creating a type, use AddMember to add members:
```
AddType(projectName: "MyApp.Services", typeName: "UserService")
AddMember(typeName: "UserService",
          memberCode: "public User GetById(int id)",
          auto: true,
          comment: "Gets user by ID")
```

The tool handles all complexity of project discovery, directory management, namespace inference, type generation, and file I/O, exposing a simple project-name-based interface.
