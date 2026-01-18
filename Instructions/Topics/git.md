# Git Workflow Guidelines

## Context

You are performing git operations in a project that follows issue-first development. Every code change must be linked to a GitHub issue.

## Critical Rules

- NEVER write code without a GitHub issue
- NEVER push directly to the default branch
- NEVER use `git push --force` on main/master/release branches
- NEVER use `git commit --amend` unless explicitly requested
- NEVER skip hooks (`--no-verify`)

## Before Starting

Get the default branch name:
```bash
gh repo view --json defaultBranchRef -q .defaultBranchRef.name
```

## Workflow

Follow these steps for any code change:

### 1. CREATE ISSUE (if not exists)
```bash
gh issue create --title "Type: Brief description" --body "Details" --label "bug"
```
Note the issue number (e.g., #42)

Labels: `bug`, `enhancement`, `documentation`, `refactor`

### 2. CREATE BRANCH
```bash
git checkout <default-branch>
git pull
git checkout -b issues/42
```
Branch name format: `issues/N` where N is issue number

### 3. MAKE CHANGES
- Follow TDD if required: call `roslyn_get_instructions` with topic "tdd"
- Use Roslyn tools for C# changes: call `roslyn_get_instructions` with topic "code"

### 4. PRE-PR CHECKLIST
Before creating a PR, complete the checklist: call `roslyn_get_instructions` with topic "pre-pr"

### 5. COMMIT
```bash
git add -A
git commit -m "$(cat <<'EOF'
Type: brief description

Detailed explanation if needed.

Fixes #42

Co-Authored-By: Claude <noreply@anthropic.com>
EOF
)"
```

### 6. PUSH AND CREATE PR
```bash
git push -u origin issues/42
gh pr create --base <default-branch> --title "Type: description" --body "$(cat <<'EOF'
## Summary
- Brief description of changes

## Test plan
- [ ] All tests pass
- [ ] No new errors or warnings

Fixes #42

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

## Commit Message Format

```
Type: brief description

Optional detailed explanation.

Fixes #N

Co-Authored-By: Claude <noreply@anthropic.com>
```

**Types:**
- `Fix` - Bug fix
- `Feature` - New functionality
- `Refactor` - Code restructuring
- `Docs` - Documentation only
- `Test` - Adding or updating tests
