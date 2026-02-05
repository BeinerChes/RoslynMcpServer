# Git Workflow

## Rules

- Get default branch first: `gh repo view --json defaultBranchRef -q .defaultBranchRef.name`
- Never assume main/master - could be `rc/X.Y.Z`
- Never push --force on main/master/release
- Never --amend unless requested
- Never --no-verify

## Workflow

```bash
# 1. Get default branch
DEFAULT=$(gh repo view --json defaultBranchRef -q .defaultBranchRef.name)

# 2. Create issue (if needed)
gh issue create --title "Type: description" --body "Details"
# Note issue number, e.g. #42

# 3. Create branch
git checkout $DEFAULT && git pull
git checkout -b issues/42

# 4. Make changes, run tests
dotnet test

# 5. Commit
git add <files>
git commit -m "Type: description

Fixes #42

Co-Authored-By: Claude <noreply@anthropic.com>"

# 6. Push and PR
git push -u origin issues/42
gh pr create --base $DEFAULT --title "Type: description" --body "Fixes #42"

# 7. Merge
gh pr merge --squash --delete-branch
git checkout $DEFAULT && git pull
```

## Commit Types

`Fix` | `Feature` | `Refactor` | `Docs` | `Test`
