# Pre-Pull Request Checklist

## Context

You are about to create a pull request. **You MUST complete this checklist before creating a PR.**

## Checklist

### 1. CHECK FOR ERRORS
```
roslyn_get_diagnostics(solutionPath, severityFilter: "error")
```
Result must show: `totalErrors: 0`

**If errors exist, fix them before proceeding.**

### 2. CHECK FOR WARNINGS
```
roslyn_get_diagnostics(solutionPath, severityFilter: "warning")
```
Review warnings. Fix any that are reasonable.

Use `roslyn_batch_apply_code_fixes` for bulk fixes when available.

### 3. RUN TESTS
```bash
dotnet test
```
**All tests must pass.** Do not create a PR with failing tests.

### 4. VERIFY CHANGES
```bash
git status
git diff HEAD
```
- Ensure all intended changes are staged
- Ensure no unintended files are included (secrets, build artifacts, etc.)

### 5. GET DEFAULT BRANCH
```bash
gh repo view --json defaultBranchRef -q .defaultBranchRef.name
```
Use this as the `--base` for the PR.

### 6. CREATE PR
```bash
gh pr create --base <default-branch> --title "Type: description" --body "$(cat <<'EOF'
## Summary
- Brief description of changes

## Test plan
- [ ] All tests pass (`dotnet test`)
- [ ] No compilation errors (`roslyn_get_diagnostics`)
- [ ] Warnings reviewed

Fixes #N

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

## PR Title Format

Use: `Type: brief description`

**Types:**
- `Fix` - Bug fix
- `Feature` - New functionality
- `Refactor` - Code restructuring
- `Docs` - Documentation only
- `Test` - Adding or updating tests

**Examples:**
- `Fix: Null reference in UserService.Save`
- `Feature: Add retry logic to API client`
- `Docs: Improve README for new users`
