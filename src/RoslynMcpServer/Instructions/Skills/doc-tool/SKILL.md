---
name: doc-tool
description: Create comprehensive documentation for a Roslyn MCP tool. Tests native vs Roslyn approach on the test project, writes docs, updates tools.md and README.md, commits.
model: claude-sonnet-4-5-20250929
arguments: "<ToolName>"
user-invocable: true
---

# Document a Roslyn MCP Tool

You are documenting a Roslyn MCP tool for the RoslynMcpServer project.

The tool name is provided as argument: **$ARGUMENTS**

## Overview

This project provides MCP tools that use Roslyn (C# compiler) for semantic code operations.
Each tool needs documentation comparing it against native Claude Code tools (Read, Edit, Write, Grep, Glob).

**CRITICAL RULES:**
- Be FAIR in comparisons. Don't inflate native tool costs. Claude is smart - show the best native approach.
- Use REAL testing on the test project.csproj, not estimates.
- Test BOTH approaches (native first, then Roslyn) and measure actual results.
- Working branch is `rc/1.0.8` (check with `git branch --show-current`).

## Step 1: Read Tool Code

Find and read the tool's implementation:

```
FindSymbol(pattern: "<ToolName>")
```

Read both:
- **Handler**: `GetMethodBody(typeName: "RoslynTools", methodName: "Register<ToolName>Tool")`
- **Service**: `GetMethodBody(typeName: "SolutionAnalyzerService", methodName: "<ToolName>Async")` (name may vary)

Understand: parameters, behavior, what it returns, edge cases.

## Step 2: Design Test Scenario

Choose a realistic task on the test project.csproj that the tool solves.
Read the current state of the test project files to plan:

```
GetTypeMembers(typeName: "Calculator")
GetTypeMembers(typeName: "TaskBoard")
GetTypeMembers(typeName: "TaskItem")
```

Pick a task that shows the tool's value vs native approach.

## Step 3: Test Native Approach

Get a bypass token first:
```
GetInstructions(topic: "tools")
```

Create test branch:
```bash
git checkout -b test/native-<toolname>
```

Execute the task using ONLY native tools (Read, Edit, Write, Grep, Glob).
Use the BEST approach Claude would actually take - be fair, don't artificially inflate.

Note the actual operations and estimate token usage.

Reset when done:
```bash
git reset --hard HEAD
git checkout rc/1.0.8
git branch -D test/native-<toolname>
```

## Step 4: Test Roslyn Approach

Create test branch:
```bash
git checkout -b test/roslyn-<toolname>
```

Execute the SAME task using the Roslyn tool.
Note the actual operations and token usage.

Reset when done:
```bash
git reset --hard HEAD
git checkout rc/1.0.8
git branch -D test/roslyn-<toolname>
```

## Step 5: Write Documentation

Create `docs/tools/<ToolName>.md` following this exact structure:

```markdown
# <ToolName>

## Description

<1-2 paragraphs: what the tool does, key capabilities>

The tool handles:
- <bullet list of key features>

## Comparison with Native Claude Code Tools

### vs <most relevant native tool>
- <point-by-point comparison, alternating native vs Roslyn>

### When to use <ToolName>
- <bullet list of use cases>

## Real-World Example

### Scenario
<describe the exact task tested on the test project>
**Test project:** the test project.csproj (actual testing, not estimates)

### Approach 1: Using Native Tools
<show the ACTUAL steps from Step 3>
<include estimated token counts per step>
**Total: X tokens, Y operations**

### Approach 2: Using <ToolName> (Roslyn)
<show the ACTUAL steps from Step 4>
<include estimated token counts>
**Total: X tokens, Y operations**

### Comparison Summary
| Aspect | Native | <ToolName> (Roslyn) |
|--------|--------|---------------------|
| **Token usage** | X | Y |
| **Operations** | X | Y |
| <other relevant aspects> | ... | ... |

## How It Works

### <Key mechanism>
<explain how the tool works technically>
<include relevant subheadings>

### Invocation
<show basic usage patterns>

### Response Format
<show success/error JSON examples>

### Common Workflows
<2-3 practical workflows combining this tool with others>

### When Edit is Better (if applicable)
<be honest about when native tools win>
```

Read existing docs for style reference:
- `docs/tools/DeleteMember.md`
- `docs/tools/RenameSymbol.md`

## Step 6: Update tools.md (if needed)

Read `Instructions/Topics/tools.md` and check if the tool is listed.

If missing, add it to the appropriate section following the existing format:
- Reading code tools go under `**Reading code:**`
- Modifying code tools go under `**Modifying code:**`
- Diagnostics tools go under `**Diagnostics:**`
- etc.

Also check the **Gotchas** section - add any non-obvious behavior you discovered.

## Step 7: Update README.md

Edit `docs/tools/README.md`:

1. Change the tool's row from `*Coming soon* | Pending` to `[<ToolName>.md](<ToolName>.md) | Complete`
2. If you added it to tools.md, update the "In tools.md?" column to check mark
3. Update progress counters in "Documentation Progress" section:
   - Increment detailed docs count
   - If added to tools.md, increment that count too
   - Update the parenthetical list of documented tools

## Step 8: Commit

```bash
git add docs/tools/<ToolName>.md docs/tools/README.md Instructions/Topics/tools.md
git commit -m "Docs: Add <ToolName> tool documentation

<1-2 sentence summary of what the tool does>
<key comparison stat>

Progress: X/34 detailed docs, Y/34 in tools.md.

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

## Step 9: Report

Tell the user:
- What was documented
- Key comparison results (token savings, operations saved)
- What was updated (docs, tools.md, README.md)
- Current progress (X/34)
