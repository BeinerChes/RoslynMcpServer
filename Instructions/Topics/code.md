# C# Code Modification Guidelines

## Context

You are modifying C# code in a .NET solution. The Roslyn MCP server provides semantic code analysis tools that are more accurate than text-based search/replace.

## Critical Rules

- NEVER propose changes to code you haven't read
- NEVER use Grep/Glob for C# symbols - use `roslyn_find_symbol`
- NEVER use Read for large files - use `roslyn_get_method_body`
- NEVER use Edit with text patterns - use `roslyn_update_method`

## Before Any Change

Follow these steps before modifying any C# code:

### 1. LOCATE the code
- Use `roslyn_find_symbol` to find the type/method by name
- Note the file path and line number from the result

### 2. UNDERSTAND the code
- Use `roslyn_get_type_members` to see all members of the class
- Use `roslyn_get_method_body` to read the specific method
- Use `roslyn_get_callers` to understand who calls this code

### 3. VERIFY the scope
- If file > 300 lines, plan to extract helper classes
- If method > 50 lines, consider breaking it down

### 4. MAKE the change
- Use `roslyn_update_method` for existing methods
- Use `roslyn_add_member` for new methods/properties
- Use `roslyn_rename_symbol` for renaming

### 5. VERIFY the result
- Use `roslyn_get_diagnostics` to check for errors

## Tool Selection

Choose the right tool for each task:

| When you need to... | Use this tool |
|---------------------|---------------|
| Find a type or method | `roslyn_find_symbol` |
| See class structure | `roslyn_get_type_members` |
| Read a method | `roslyn_get_method_body` |
| Edit a method | `roslyn_update_method` |
| Add new member | `roslyn_add_member` |
| Find all usages | `roslyn_get_references` |
| Find who calls this | `roslyn_get_callers` |
| Find implementations | `roslyn_get_implementations` |
| Check for errors | `roslyn_get_diagnostics` |
| Fix a warning | `roslyn_apply_code_fix` |
| Fix many warnings | `roslyn_batch_apply_code_fixes` |
| Rename something | `roslyn_rename_symbol` |

## Code Quality

- Keep files under 300 lines
- Use async/await for I/O operations
- Make error messages actionable
- Avoid over-engineering - only change what's requested
