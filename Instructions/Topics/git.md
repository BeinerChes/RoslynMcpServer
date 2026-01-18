# Git Workflow Guidelines

## Context

You are performing git operations in a project that follows issue-first development. Every code change must be linked to a GitHub issue.

## Critical Rules

- NEVER write code without a GitHub issue
- NEVER use `git push --force` on main/master
- NEVER use `git commit --amend` unless explicitly requested
- NEVER skip hooks (`--no-verify`)

## Workflow

Follow these steps for any code change:

### 1. CREATE ISSUE (if not exists)
```bash
gh issue create --title "Brief description" --body "Details" --label "bug|enhancement"
```
Note the issue number (e.g., #42)

### 2. CREATE BRANCH
```bash
git checkout -b issues/42
```
Branch name format: `issues/N` where N is issue number

### 3. MAKE CHANGES
- Follow TDD if required: `roslyn_get_instructions(topic: "tdd")`
- Use Roslyn tools for C# changes: `roslyn_get_instructions(topic: "code")`

### 4. VERIFY BUILD
```
roslyn_get_diagnostics(severityFilter: "error")
```

### 5. COMMIT
```bash
git add -A
git commit -m "Fix: description

Fixes #42

Co-Authored-By: Claude <noreply@anthropic.com>"
```

### 6. PUSH AND PR
```bash
git push -u origin issues/42
gh pr create --base rc/X.X.X --title "Fix: description" --body "Fixes #42"
```

## Commit Format

Use this commit message format:

```
Type: brief description

Fixes #N

Co-Authored-By: Claude <noreply@anthropic.com>
```

Types: Fix, Feature, Refactor, Docs, Test
