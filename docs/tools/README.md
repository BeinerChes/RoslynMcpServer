# Roslyn MCP Tools Documentation

## Overview

This directory contains detailed documentation for each Roslyn MCP tool. Each tool doc includes:
- Description and purpose
- Comparison with native Claude Code tools
- Real-world usage examples with token counts
- Technical implementation details

## Available Tools

### Code Modification

| Tool | In tools.md? | Documentation | Status |
|------|--------------|---------------|--------|
| **UpdateMethod** | ✅ | [UpdateMethod.md](UpdateMethod.md) | Complete |
| **AddMember** | ✅ | [AddMember.md](AddMember.md) | Complete |
| **AddType** | ✅ | [AddType.md](AddType.md) | Complete |
| **DeleteMember** | ✅ | [DeleteMember.md](DeleteMember.md) | Complete |
| **AddUsing** | ✅ | [AddUsing.md](AddUsing.md) | Complete |
| **RemoveUnnecessaryUsings** | ✅ | [RemoveUnnecessaryUsings.md](RemoveUnnecessaryUsings.md) | Complete |
| **RenameSymbol** | ✅ | [RenameSymbol.md](RenameSymbol.md) | Complete |
| **ExtractMethod** | ✅ | [ExtractMethod.md](ExtractMethod.md) | Complete |

### Code Navigation & Exploration

| Tool | In tools.md? | Documentation | Status |
|------|--------------|---------------|--------|
| **FindSymbol** | ✅ | [FindSymbol.md](FindSymbol.md) | Complete |
| **GetTypeMembers** | ✅ | [GetTypeMembers.md](GetTypeMembers.md) | Complete |
| **GetMethodBody** | ✅ | [GetMethodBody.md](GetMethodBody.md) | Complete |
| **GetReferences** | ✅ | [GetReferences.md](GetReferences.md) | Complete |
| **GetCallers** | ✅ | [GetCallers.md](GetCallers.md) | Complete |

### Code Analysis & Quality

| Tool | In tools.md? | Documentation | Status |
|------|--------------|---------------|--------|
| **GetDiagnostics** | ✅ | [GetDiagnostics.md](GetDiagnostics.md) | Complete |
| **ApplyCodeFix** | ✅ | [ApplyCodeFix.md](ApplyCodeFix.md) | Complete |
| **BatchApplyCodeFixes** | ✅ | [BatchApplyCodeFixes.md](BatchApplyCodeFixes.md) | Complete |
| **FindDeadCode** | ✅ | [FindDeadCode.md](FindDeadCode.md) | Complete |

### AI Code Generation & Training

| Tool | In tools.md? | Documentation | Status |
|------|--------------|---------------|--------|
| **Finetune** | ✅ | *Coming soon* | Pending |

### Knowledge Base

| Tool | In tools.md? | Documentation | Status |
|------|--------------|---------------|--------|
| **KnowledgeAdd** | ✅ | *Coming soon* | Pending |
| **KnowledgeSearch** | ✅ | *Coming soon* | Pending |
| **KnowledgeDelete** | ❌ | *Coming soon* | Pending |
| **KnowledgeGet** | ❌ | *Coming soon* | Pending |

### Setup & Utilities

| Tool | In tools.md? | Documentation | Status |
|------|--------------|---------------|--------|
| **GetInstructions** | ✅ | [GetInstructions.md](GetInstructions.md) | Complete |
| **GetUsageReport** | ❌ | *Coming soon* | Pending |

## Tool Count

**Total: 24 tools** across 6 categories

## Documentation Progress

### Detailed Documentation
- ✅ **18/24** tools have detailed docs (UpdateMethod, AddMember, AddType, DeleteMember, AddUsing, RemoveUnnecessaryUsings, ExtractMethod, RenameSymbol, FindSymbol, GetTypeMembers, GetMethodBody, GetReferences, GetCallers, ApplyCodeFix, BatchApplyCodeFixes, GetDiagnostics, FindDeadCode, GetInstructions)
- ⏳ **6/24** remaining

### Claude Code Integration (Instructions/Topics/tools.md)
- ✅ **20/24** tools documented in quick reference
- ❌ **4/24** tools missing from Claude Code docs

## Documentation Algorithm

For each tool, follow this workflow:

### 1. Read Tool Code
```
Read tool implementation in src/RoslynTools.*.cs
Understand what it does, parameters, behavior
```

### 2. Create Test Scenario on SharpOps.Examples
Choose a representative task that the tool solves. Examples:
- UpdateMethod: Change a method implementation
- AddMember: Add a new method
- RemoveUnnecessaryUsings: Remove unused using directives

### 3. Test Native Approach (Fair & Realistic)
Create a local test branch:
```bash
git checkout -b test/tool-comparison
```

**Execute the task using ONLY native Claude Code tools:**
- Read, Write, Edit, Glob, Grep, Bash
- **Be realistic**: Use the most efficient approach Claude would actually use
- **No artificial inflation**: If Edit with replace_all works, use it
- **Track actual tokens**: Count tokens from each tool call
- **Time yourself**: Note how long it takes

Document:
- Exact tool calls made
- Token count per call
- Total tokens
- Total operations
- Any difficulties or errors encountered

### 4. Reset State
```bash
git reset --hard HEAD
git checkout main (or working branch)
git branch -D test/tool-comparison
```

### 5. Test Roslyn Tool Approach
Create a new test branch:
```bash
git checkout -b test/roslyn-comparison
```

**Execute the same task using the Roslyn tool:**
- Track actual tokens
- Time yourself
- Document any differences in result

### 6. Compare Results
Calculate:
- Token savings: `(native_tokens - roslyn_tokens) / native_tokens * 100`
- Operation reduction: `native_ops vs roslyn_ops`
- Any quality differences in the output

### 7. Write Documentation
Create `docs/tools/{ToolName}.md` following this structure:

```markdown
# ToolName

## Description
What the tool does, key capabilities

## Comparison with Native Claude Code Tools
### vs Edit/Write/Read (whichever is most relevant)
- Point-by-point comparison
- Focus on semantic vs text-based operations

## Real-World Example
### Scenario
Describe the exact task tested on SharpOps.Examples

### Approach 1: Using Native Tools
Show ACTUAL tool calls used in testing
Include ACTUAL token counts
Be fair - show the best native approach

### Approach 2: Using Roslyn Tool
Show ACTUAL tool calls
Include ACTUAL token counts

### Comparison Summary
Table with actual measured results
Honest percentage savings

## How It Works
Technical explanation of the tool's internals
```

### 8. Update Integration Files
```bash
# Check if tool is in tools.md
grep -i "ToolName" Instructions/Topics/tools.md

# If missing, add to appropriate section
Edit Instructions/Topics/tools.md

# Update this README
# - Mark tool as Complete in table
# - Update progress counters (detailed docs count, tools.md count)
Edit docs/tools/README.md
```

### 9. Clean Up Test Branches
```bash
git checkout main
git branch -D test/roslyn-comparison
# (test/tool-comparison already deleted in step 4)
```

## Key Principles

1. **Be Fair**: Don't artificially inflate native tool token counts
2. **Be Realistic**: Use the approach Claude would actually take
3. **Test Everything**: Real comparisons on real code, not estimates
4. **Be Honest**: If a tool doesn't save much, say so
5. **Show Trade-offs**: Some tools trade tokens for other benefits (semantic understanding, safety, etc.)

## Contributing

When adding a new tool, ensure:
1. Tool is registered in appropriate `RoslynTools.*.cs` file
2. Documentation follows the algorithm above
3. Real-world testing completed on SharpOps.Examples.csproj
4. Update this README's tool table
5. Update tool count

## Related Documentation

- [Instructions/Topics/tools.md](../../Instructions/Topics/tools.md) - Quick reference for Claude Code
- [README.md](../../README.md) - Main project README with overview
