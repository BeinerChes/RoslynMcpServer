# Plan Management Guidelines

## Context

You are working on tasks that may span multiple sessions. Long sessions experience context resets and summarization, causing work to be lost or repeated. Plan files provide persistent memory across sessions.

## Critical Rules

- ALWAYS create a GitHub issue before writing any code
- ALWAYS create a plan file for any non-trivial task
- ALWAYS name plan files `issue-<number>.md` (matches GitHub issue)
- ALWAYS update the plan after each significant change
- ALWAYS re-read CLAUDE.md after each fix
- ALWAYS use Roslyn MCP tools for C# code (NOT Explore/Grep/Glob agents)
- NEVER assume context is preserved between sessions
- NEVER create plans with descriptive names - use issue numbers only

## Plan File Location

Create plan files **in the solution directory**:
```
<solution-root>/.claude/plans/issue-<number>.md
```

**Why issue numbers?**
- Links plan to GitHub issue for traceability
- Prevents orphaned plans with no tracking
- Makes it easy to find the plan for any issue

**Why per-solution?**
- Plans are project-specific
- Can be version controlled (or .gitignored)
- No mixing up plans from different projects
- Team members can share plans

## At Session Start

1. **Ask user:** "New issue or continuing existing work?"

2. **Get the issue number** (REQUIRED before any code changes):

   **If new work:**
   - Create GitHub issue FIRST: `gh issue create --title "Type: description" --body "Details"`
   - Note the issue number (e.g., #57)
   - Create branch: `git checkout -b issues/57`
   - Create plan file: `.claude/plans/issue-57.md`

   **If continuing existing work:**
   - Get issue number from user
   - Read GitHub issue and comments: `gh issue view <number>`
   - Check for local plan: `ls .claude/plans/`
   - Read plan file if exists (create if not)

3. **Search knowledge base:** `roslyn_knowledge_search(query: "<brief description of task>")`
   - Look for relevant lessons, error resolutions, or conventions
   - Past sessions may have captured useful insights

4. **Get Roslyn tool guidance:** `roslyn_get_instructions("tools")`
   - Use Roslyn tools for C# code navigation, NOT Explore/Grep/Glob
   - Roslyn provides semantic understanding (types, references, callers)

## Plan Structure

```markdown
# Plan: <Issue/Task Name>

## Problem Statement
<What needs to be fixed/built, with measurable target>

## Completed Fixes
<List each fix with file path and code snippet>

## Test Results
<Results after each fix - show progress>

## Current Status
<Where we are now>

## Next Steps
<What's left to do>

## Workflow Reminder (MANDATORY)
After each fix:
1. Run `roslyn_get_diagnostics` to check for errors
2. **MANDATORY: Re-read and follow CLAUDE.md**
3. **Update this plan**
4. Keep working until issue is resolved
```

## Capture Learnings

**Add to knowledge base when you encounter:**

| Situation | Category | Example |
|-----------|----------|---------|
| Fixed error after 2+ attempts | `error-resolution` | "CA2007 fix requires ConfigureAwait(false) in library code" |
| Discovered non-obvious behavior | `lesson` | "roslyn_find_symbol doesn't search method bodies" |
| User corrected your approach | `convention` | "This project uses record types for DTOs, not classes" |
| Found hidden dependency | `lesson` | "Changing X.cs requires also updating Y.cs" |
| Workflow insight | `instruction` | "Always run tests before committing in this repo" |

**How to capture:**
```
roslyn_knowledge_add(
    category: "error-resolution",
    title: "Brief summary of the learning",
    content: "Detailed explanation with context",
    symbolLinks: ["Namespace.Class.Method"],  // optional
    tags: ["relevant", "keywords"],           // optional
    confidence: 0.8                           // lower if uncertain
)
```

**Lower confidence (0.5-0.8)** for learnings you're not 100% sure about - they can be verified or updated later.

## Why Plan Files Matter

- Long sessions get context-reset/summarized
- Plan file is persistent memory across sessions
- Without it, work gets lost or repeated
- Enables handoff between sessions or collaborators
