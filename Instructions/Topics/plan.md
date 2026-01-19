# Plan Management Guidelines

## Context

You are working on tasks that may span multiple sessions. Long sessions experience context resets and summarization, causing work to be lost or repeated. Plan files provide persistent memory across sessions.

## Critical Rules

- ALWAYS create a plan file for any non-trivial task
- ALWAYS update the plan after each significant change
- ALWAYS re-read CLAUDE.md after each fix
- NEVER assume context is preserved between sessions

## Plan File Location

Create plan files at:
```
~/.claude/plans/<descriptive-name>.md
```

For issue-linked work: `~/.claude/plans/issue-<number>.md`

## At Session Start

1. **Ask user:** "New issue or continuing existing work?"

2. **If new issue:**
   - Create GitHub issue: `gh issue create --title "Type: description" --body "Details"`
   - Create branch: `git checkout -b issues/<number>`
   - Create plan file: `~/.claude/plans/issue-<number>.md`

3. **If continuing existing work:**
   - Get issue number from user
   - Read GitHub issue and comments: `gh issue view <number>`
   - Check for local plan: `ls ~/.claude/plans/`
   - Read plan file if exists (create if not)

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

## Why Plan Files Matter

- Long sessions get context-reset/summarized
- Plan file is persistent memory across sessions
- Without it, work gets lost or repeated
- Enables handoff between sessions or collaborators
