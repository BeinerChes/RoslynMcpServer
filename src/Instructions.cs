namespace RoslynMcpServer;

/// <summary>
/// Contains embedded instruction content for CLAUDE.md templates and topic-specific guidance.
/// Issue: #15
/// </summary>
public static class Instructions
{
    #region Templates

    public static class Templates
    {
        public const string Minimal = """
            # CLAUDE.md - C# Development with Roslyn MCP

            When working with C# code, use the Roslyn MCP server for guidance:

            - Before modifying C# code: `roslyn_get_instructions(topic: "code")`
            - Before git operations: `roslyn_get_instructions(topic: "git")`
            - Before creating PR: `roslyn_get_instructions(topic: "pre-pr")`

            Use Roslyn MCP tools (`roslyn_*`) instead of native tools (Grep, Read, Edit) for C# files.
            """;

        public const string Standard = """
            # CLAUDE.md - C# Development with Roslyn MCP

            ## Tool Preferences

            When working with C# files in .NET solutions, prefer Roslyn MCP tools over native tools:

            | Task | Use This | Not This |
            |------|----------|----------|
            | Find type/method | `roslyn_find_symbol` | Grep |
            | Read a method | `roslyn_get_method_body` | Read entire file |
            | Edit a method | `roslyn_update_method` | Edit with text patterns |
            | Find references | `roslyn_get_references` | Grep for text |
            | Check errors | `roslyn_get_diagnostics` | dotnet build |

            For full tool preferences: `roslyn_get_instructions(topic: "tools")`

            ## Workflows

            - Before modifying C# code: `roslyn_get_instructions(topic: "code")`
            - Before git operations: `roslyn_get_instructions(topic: "git")`
            - Before creating PR: `roslyn_get_instructions(topic: "pre-pr")`
            """;

        public const string Tdd = """
            # CLAUDE.md - C# Development with Roslyn MCP (TDD)

            ## Test-Driven Development

            **YOU MUST follow TDD for ALL code changes. No exceptions.**

            1. Write FAILING test(s) first
            2. Run tests - verify they FAIL
            3. Write minimum code to make tests PASS
            4. Refactor if needed (tests must still pass)

            For full TDD workflow: `roslyn_get_instructions(topic: "tdd")`

            ## Tool Preferences

            When working with C# files, prefer Roslyn MCP tools:

            | Task | Use This | Not This |
            |------|----------|----------|
            | Find type/method | `roslyn_find_symbol` | Grep |
            | Read a method | `roslyn_get_method_body` | Read entire file |
            | Edit a method | `roslyn_update_method` | Edit with text patterns |
            | Check errors | `roslyn_get_diagnostics` | dotnet build |

            For full tool preferences: `roslyn_get_instructions(topic: "tools")`

            ## Workflows

            - Before modifying C# code: `roslyn_get_instructions(topic: "code")`
            - Before git operations: `roslyn_get_instructions(topic: "git")`
            - Before creating PR: `roslyn_get_instructions(topic: "pre-pr")`
            """;

        public const string Team = """
            # CLAUDE.md - C# Development with Roslyn MCP (Team Workflow)

            ## Issue-First Development

            **NEVER write code without a GitHub issue.**

            1. Create GitHub issue first describing the feature/bug
            2. Create feature branch: `issues/N`
            3. Reference issue in commits: `Fixes #N`

            For full git workflow: `roslyn_get_instructions(topic: "git")`

            ## Test-Driven Development

            **YOU MUST follow TDD for ALL code changes.**

            For full TDD workflow: `roslyn_get_instructions(topic: "tdd")`

            ## Tool Preferences

            When working with C# files, prefer Roslyn MCP tools:

            | Task | Use This | Not This |
            |------|----------|----------|
            | Find type/method | `roslyn_find_symbol` | Grep |
            | Read a method | `roslyn_get_method_body` | Read entire file |
            | Edit a method | `roslyn_update_method` | Edit with text patterns |
            | Check errors | `roslyn_get_diagnostics` | dotnet build |

            For full tool preferences: `roslyn_get_instructions(topic: "tools")`

            ## Workflows

            - Before modifying C# code: `roslyn_get_instructions(topic: "code")`
            - Before git operations: `roslyn_get_instructions(topic: "git")`
            - Before creating PR: `roslyn_get_instructions(topic: "pre-pr")`
            """;

        public static string? Get(string templateName) => templateName.ToLowerInvariant() switch
        {
            "minimal" => Minimal,
            "standard" => Standard,
            "tdd" => Tdd,
            "team" => Team,
            _ => null
        };

        public static string[] Available => ["minimal", "standard", "tdd", "team"];
    }

    #endregion

    #region Topics

    public static class Topics
    {
        public const string Code = """
            # C# Code Modification Guidelines

            ## Before Modifying Code

            1. **NEVER propose changes to code you haven't read.** Use `roslyn_get_method_body` first.
            2. **Understand existing patterns** before suggesting modifications.
            3. **Keep files under 300 lines.** Extract helper classes if needed.

            ## Tool Preferences

            | Task | Use This | Not This |
            |------|----------|----------|
            | Find a type/method | `roslyn_find_symbol` | Grep or Glob |
            | Understand a class | `roslyn_get_type_members` | Read the whole file |
            | Read a method | `roslyn_get_method_body` | Read the whole file |
            | Edit a method | `roslyn_update_method` | Edit with text patterns |
            | Add a member | `roslyn_add_member` | Edit to insert code |
            | Find all references | `roslyn_get_references` | Grep for text |
            | Find callers only | `roslyn_get_callers` | roslyn_get_references |
            | Find implementations | `roslyn_get_implementations` | Grep for class names |
            | Check for errors | `roslyn_get_diagnostics` | dotnet build |
            | Fix one warning | `roslyn_apply_code_fix` | Manual Edit |
            | Fix many warnings | `roslyn_batch_apply_code_fixes` | Loop of single fixes |
            | Rename symbol | `roslyn_rename_symbol` | Manual find/replace |

            ## Code Quality

            - Use async/await for all I/O operations
            - Error messages should be actionable
            - Avoid over-engineering - only make changes that are directly requested
            - Don't add features, refactor code, or make "improvements" beyond what was asked
            """;

        public const string Git = """
            # Git Workflow Guidelines

            ## Issue-First Development

            **Create a GitHub issue BEFORE making code changes.**

            ```bash
            gh issue create --title "Brief description" --body "Details" --label "bug|enhancement"
            ```

            ## Branch Naming

            - Feature branches: `issues/N` (where N is the issue number)
            - Base all work on the default RC branch

            ## Commit Messages

            ```
            Fix: description of change

            Fixes #N

            Co-Authored-By: Claude <noreply@anthropic.com>
            ```

            ## Workflow

            1. Create GitHub issue
            2. Create branch: `git checkout -b issues/N`
            3. Make changes with TDD
            4. Verify build: `roslyn_get_diagnostics`
            5. Commit with issue reference
            6. Push and create PR: `gh pr create --base rc/X.X.X`

            ## Safety Rules

            - NEVER use `git push --force` on main/master
            - NEVER use `git commit --amend` unless explicitly requested
            - NEVER skip hooks (`--no-verify`)
            """;

        public const string Tdd = """
            # Test-Driven Development Guidelines

            ## TDD Workflow

            1. **Write FAILING test(s) first** - with issue reference comment
            2. **Run tests** - verify they FAIL
            3. **Write minimum code** to make tests PASS
            4. **Refactor** if needed (tests must still pass)
            5. **Commit**

            ## Test Naming Convention

            ```csharp
            // Format: MethodName_Scenario_ExpectedResult
            [Fact]
            public void FindSymbols_WithValidPattern_ReturnsMatchingSymbols()
            ```

            ## Test Comment Format (Required)

            ```csharp
            /// <summary>
            /// Tests that FindSymbols returns matching symbols for a valid pattern.
            /// Issue: #42
            /// </summary>
            [Fact]
            public void FindSymbols_WithValidPattern_ReturnsMatchingSymbols()
            {
                // Arrange
                // Act
                // Assert
            }
            ```

            ## Running Tests

            ```bash
            dotnet test                           # Run all tests
            dotnet test --filter "Name~MyTest"    # Run specific test
            ```
            """;

        public const string PrePr = """
            # Pre-Pull Request Checklist

            ## Before Creating a PR

            1. **Verify build has no errors:**
               ```
               roslyn_get_diagnostics(severityFilter: "error")
               ```

            2. **Check for new warnings** (fix if reasonable):
               ```
               roslyn_get_diagnostics(severityFilter: "warning")
               ```

            3. **Run tests:**
               ```bash
               dotnet test
               ```

            4. **Verify all changes are committed:**
               ```bash
               git status
               ```

            5. **Review your changes:**
               ```bash
               git diff main...HEAD
               ```

            ## Creating the PR

            ```bash
            gh pr create --base rc/X.X.X --title "Fix: description" --body "Fixes #N"
            ```

            ## PR Body Format

            ```markdown
            ## Summary
            - Brief description of changes

            ## Test Plan
            - [ ] Unit tests pass
            - [ ] Manual testing done

            Fixes #N
            ```
            """;

        public const string Tools = """
            # Roslyn MCP Tool Preferences

            When working with C# files in .NET solutions, **PREFER Roslyn MCP tools over native tools**:

            | Task | Use This | NOT This |
            |------|----------|----------|
            | Find a type/method | `roslyn_find_symbol` | `Grep` or `Glob` |
            | Understand a class | `roslyn_get_type_members` | `Read` the whole file |
            | Read a method | `roslyn_get_method_body` | `Read` the whole file |
            | Edit a method | `roslyn_update_method` | `Edit` with text patterns |
            | Add a member | `roslyn_add_member` | `Edit` to insert code |
            | Find all references | `roslyn_get_references` | `Grep` for text |
            | Find callers only | `roslyn_get_callers` | `roslyn_get_references` (includes non-calls) |
            | Find implementations | `roslyn_get_implementations` | `Grep` for class names |
            | Check for errors | `roslyn_get_diagnostics` | `Bash` dotnet build |
            | Fix one warning | `roslyn_apply_code_fix` | Manual `Edit` |
            | Fix many warnings | `roslyn_batch_apply_code_fixes` | Loop of single fixes |
            | Rename symbol | `roslyn_rename_symbol` | Manual find/replace |

            **Only use native tools for:**
            - Non-C# files (JSON, XML, markdown, .csproj)
            - Creating brand new .cs files (use `Write`, then `roslyn_add_member`)
            - Very small files (< 100 lines) where `Read`/`Edit` is simpler
            - When Roslyn MCP server is not connected
            """;

        public static string? Get(string topicName) => topicName.ToLowerInvariant() switch
        {
            "code" => Code,
            "git" => Git,
            "tdd" => Tdd,
            "pre-pr" or "prepr" or "pr" => PrePr,
            "tools" => Tools,
            _ => null
        };

        public static string[] Available => ["code", "git", "tdd", "pre-pr", "tools"];
    }

    #endregion
}
