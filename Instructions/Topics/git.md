# Git Workflow Guidelines

## Context

You are performing git operations in a project that follows issue-first development. Every code change must be linked to a GitHub issue.

## Critical Rules

- NEVER write code without a GitHub issue
- NEVER push directly to the default branch
- NEVER use `git push --force` on main/master/release branches
- NEVER use `git commit --amend` unless explicitly requested
- NEVER skip hooks (`--no-verify`)
- **ALWAYS get the default branch from GitHub** - never assume it's `main` or `master`

## Before Starting (MANDATORY)

**FIRST STEP: Get the default branch name from GitHub:**
```bash
gh repo view --json defaultBranchRef -q .defaultBranchRef.name
```

Store this value and use it for ALL branch operations below. The default branch may be `main`, `master`, `rc/X.Y.Z`, or something else. **Never assume - always check.**

## Workflow

Follow these steps for any code change:

### 1. CREATE ISSUE (if not exists)
```bash
gh issue create --title "Type: Brief description" --body "Details" --label "bug"
```
Note the issue number (e.g., #42)

Labels: `bug`, `enhancement`, `documentation`, `refactor`

### 2. CREATE BRANCH
Use the default branch from step "Before Starting":
```bash
# Example: if default branch is rc/1.0.3
git checkout rc/1.0.3
git pull
git checkout -b issues/42
```
Branch name format: `issues/N` where N is issue number

### 3. MAKE CHANGES
- Follow TDD if required: call `roslyn_get_instructions` with topic "tdd"
- Use Roslyn tools for C# changes: call `roslyn_get_instructions` with topic "code"

### 4. WRITE AND RUN TESTS

**Every code change should have unit tests.** Use the existing test framework in the solution (xUnit preferred).

**Discover the test framework:**
```bash
# Check existing test projects
ls *Tests*/*.csproj
# Or look for test packages in .csproj files
grep -r "xunit\|nunit\|mstest" *.csproj
```

**Write tests following these patterns:**
- Place tests in the corresponding `*.Tests` project
- Name test classes `<ClassUnderTest>Tests.cs`
- Use `[Fact]` for single tests, `[Theory]` for parameterized tests
- Follow Arrange/Act/Assert pattern
- Link to issue number in XML doc: `/// Issue: #N`

**Test file structure:**
```csharp
namespace MyProject.Tests;

/// <summary>
/// Tests for <ClassName>.
/// Issue: #42
/// </summary>
public class ClassNameTests
{
    [Fact]
    public void MethodName_Scenario_ExpectedResult()
    {
        // Arrange
        var sut = new ClassName();

        // Act
        var result = sut.Method();

        // Assert
        Assert.Equal(expected, result);
    }
}
```

**Run tests before committing:**
```bash
dotnet test --no-build
```

If tests fail, fix them before proceeding. **Do not commit with failing tests.**

### 5. PRE-PR CHECKLIST
Before creating a PR, complete the checklist: call `roslyn_get_instructions` with topic "pre-pr"

### 6. CAPTURE LEARNINGS
Before committing, consider: did you learn anything that would help future sessions?
- Errors that took multiple attempts to fix → `roslyn_knowledge_add(category: "error-resolution", ...)`
- Non-obvious codebase behaviors discovered → `roslyn_knowledge_add(category: "lesson", ...)`
- User corrections to your approach → `roslyn_knowledge_add(category: "convention", ...)`

### 7. COMMIT
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

### 8. PUSH AND CREATE PR
Use the default branch from step "Before Starting" for `--base`:
```bash
git push -u origin issues/42
# Example: if default branch is rc/1.0.3
gh pr create --base rc/1.0.3 --title "Type: description" --body "$(cat <<'EOF'
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

### 9. MERGE AND CLEANUP
After the PR is ready (tests pass, no errors):
```bash
gh pr merge --squash --delete-branch
```

This will:
- Squash merge the PR into the default branch
- Delete the remote `issues/N` branch
- Auto-close issue #N (because of "Fixes #N" in the PR body)

Then update your local repo:
```bash
# Example: if default branch is rc/1.0.3
git checkout rc/1.0.3
git pull
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
