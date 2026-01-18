# Pre-Pull Request Checklist

## Context

You are about to create a pull request. Complete this checklist to ensure code quality.

## Checklist

Complete these steps before creating a PR:

### 1. CHECK FOR ERRORS
```
roslyn_get_diagnostics(severityFilter: "error")
```
Result must show: `totalErrors: 0`

If errors exist, fix them before proceeding.

### 2. CHECK FOR WARNINGS
```
roslyn_get_diagnostics(severityFilter: "warning")
```
Review warnings. Fix any that are reasonable.

Use `roslyn_batch_apply_code_fixes` for bulk fixes.

### 3. RUN TESTS
```bash
dotnet test
```
All tests must pass.

### 4. VERIFY CHANGES
```bash
git status
git diff HEAD
```
- Ensure all intended changes are staged
- Ensure no unintended files are included

### 5. CREATE PR
```bash
gh pr create --base rc/X.X.X --title "Type: description" --body "$(cat <<'EOF'
## Summary
- Brief description of changes

## Test Plan
- [ ] Unit tests pass
- [ ] Manual testing done (if applicable)

Fixes #N
EOF
)"
```

## PR Title Format

Use: `Type: brief description`

Types: Fix, Feature, Refactor, Docs, Test

Examples:
- `Fix: Null reference in UserService.Save`
- `Feature: Add retry logic to API client`
- `Refactor: Extract validation to separate class`
