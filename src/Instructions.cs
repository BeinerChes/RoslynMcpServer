namespace RoslynMcpServer;

/// <summary>
/// Contains embedded instruction content for CLAUDE.md templates and topic-specific guidance.
/// Uses XML tags and chain-of-thought patterns for improved Claude reasoning.
/// Issues: #15, #17
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
            <context>
            You are modifying C# code in a .NET solution. The Roslyn MCP server provides semantic code analysis tools that are more accurate than text-based search/replace.
            </context>

            <critical_rules>
            - NEVER propose changes to code you haven't read
            - NEVER use Grep/Glob for C# symbols - use roslyn_find_symbol
            - NEVER use Read for large files - use roslyn_get_method_body
            - NEVER use Edit with text patterns - use roslyn_update_method
            </critical_rules>

            <before_any_change>
            Follow these steps before modifying any C# code:

            1. LOCATE the code:
               - Use roslyn_find_symbol to find the type/method by name
               - Note the file path and line number from the result

            2. UNDERSTAND the code:
               - Use roslyn_get_type_members to see all members of the class
               - Use roslyn_get_method_body to read the specific method
               - Use roslyn_get_callers to understand who calls this code

            3. VERIFY the scope:
               - If file > 300 lines, plan to extract helper classes
               - If method > 50 lines, consider breaking it down

            4. MAKE the change:
               - Use roslyn_update_method for existing methods
               - Use roslyn_add_member for new methods/properties
               - Use roslyn_rename_symbol for renaming

            5. VERIFY the result:
               - Use roslyn_get_diagnostics to check for errors
            </before_any_change>

            <tool_selection>
            Choose the right tool for each task:

            | When you need to... | Use this tool |
            |---------------------|---------------|
            | Find a type or method | roslyn_find_symbol |
            | See class structure | roslyn_get_type_members |
            | Read a method | roslyn_get_method_body |
            | Edit a method | roslyn_update_method |
            | Add new member | roslyn_add_member |
            | Find all usages | roslyn_get_references |
            | Find who calls this | roslyn_get_callers |
            | Find implementations | roslyn_get_implementations |
            | Check for errors | roslyn_get_diagnostics |
            | Fix a warning | roslyn_apply_code_fix |
            | Fix many warnings | roslyn_batch_apply_code_fixes |
            | Rename something | roslyn_rename_symbol |
            </tool_selection>

            <code_quality>
            - Keep files under 300 lines
            - Use async/await for I/O operations
            - Make error messages actionable
            - Avoid over-engineering - only change what's requested
            </code_quality>
            """;

        public const string Git = """
            <context>
            You are performing git operations in a project that follows issue-first development. Every code change must be linked to a GitHub issue.
            </context>

            <critical_rules>
            - NEVER write code without a GitHub issue
            - NEVER use git push --force on main/master
            - NEVER use git commit --amend unless explicitly requested
            - NEVER skip hooks (--no-verify)
            </critical_rules>

            <workflow>
            Follow these steps for any code change:

            1. CREATE ISSUE (if not exists):
               ```bash
               gh issue create --title "Brief description" --body "Details" --label "bug|enhancement"
               ```
               Note the issue number (e.g., #42)

            2. CREATE BRANCH:
               ```bash
               git checkout -b issues/42
               ```
               Branch name format: issues/N where N is issue number

            3. MAKE CHANGES:
               - Follow TDD if required (roslyn_get_instructions topic: "tdd")
               - Use Roslyn tools for C# changes (roslyn_get_instructions topic: "code")

            4. VERIFY BUILD:
               ```
               roslyn_get_diagnostics(severityFilter: "error")
               ```

            5. COMMIT:
               ```bash
               git add -A
               git commit -m "Fix: description

               Fixes #42

               Co-Authored-By: Claude <noreply@anthropic.com>"
               ```

            6. PUSH AND PR:
               ```bash
               git push -u origin issues/42
               gh pr create --base rc/X.X.X --title "Fix: description" --body "Fixes #42"
               ```
            </workflow>

            <commit_format>
            Use this commit message format:

            ```
            Type: brief description

            Fixes #N

            Co-Authored-By: Claude <noreply@anthropic.com>
            ```

            Types: Fix, Feature, Refactor, Docs, Test
            </commit_format>
            """;

        public const string Tdd = """
            <context>
            You are following Test-Driven Development (TDD). Tests must be written BEFORE implementation code.
            </context>

            <critical_rules>
            - NEVER write implementation code before tests
            - NEVER skip the "verify test fails" step
            - NEVER write more code than needed to pass tests
            </critical_rules>

            <workflow>
            Follow these steps exactly:

            1. WRITE TEST FIRST:
               - Create test method with descriptive name
               - Add issue reference in XML comment
               - Write test that exercises the expected behavior
               ```csharp
               /// <summary>
               /// Tests that Save throws when database unavailable.
               /// Issue: #42
               /// </summary>
               [Fact]
               public async Task Save_WhenDatabaseUnavailable_ThrowsException()
               {
                   // Arrange
                   var service = new UserService(mockDb.Object);
                   mockDb.Setup(x => x.IsAvailable).Returns(false);

                   // Act & Assert
                   await Assert.ThrowsAsync<DatabaseException>(
                       () => service.SaveAsync(user));
               }
               ```

            2. RUN TEST - VERIFY IT FAILS:
               ```bash
               dotnet test --filter "Name~Save_WhenDatabaseUnavailable"
               ```
               If test passes, your test is wrong - fix it first.

            3. WRITE MINIMUM CODE:
               - Only write enough code to make the test pass
               - Do not add extra features or error handling

            4. RUN TEST - VERIFY IT PASSES:
               ```bash
               dotnet test --filter "Name~Save_WhenDatabaseUnavailable"
               ```

            5. REFACTOR (optional):
               - Clean up code while keeping tests passing
               - Run tests after each refactor step

            6. COMMIT:
               - Commit test and implementation together
            </workflow>

            <naming_convention>
            Test method names follow: MethodName_Scenario_ExpectedResult

            Examples:
            - Save_WithValidUser_ReturnsSuccess
            - Save_WhenDatabaseUnavailable_ThrowsException
            - Calculate_WithNegativeInput_ReturnsZero
            </naming_convention>
            """;

        public const string PrePr = """
            <context>
            You are about to create a pull request. Complete this checklist to ensure code quality.
            </context>

            <checklist>
            Complete these steps before creating a PR:

            1. CHECK FOR ERRORS:
               ```
               roslyn_get_diagnostics(severityFilter: "error")
               ```
               Result must show: totalErrors: 0
               If errors exist, fix them before proceeding.

            2. CHECK FOR WARNINGS:
               ```
               roslyn_get_diagnostics(severityFilter: "warning")
               ```
               Review warnings. Fix any that are reasonable.
               Use roslyn_batch_apply_code_fixes for bulk fixes.

            3. RUN TESTS:
               ```bash
               dotnet test
               ```
               All tests must pass.

            4. VERIFY CHANGES:
               ```bash
               git status
               git diff HEAD
               ```
               Ensure all intended changes are staged.
               Ensure no unintended files are included.

            5. CREATE PR:
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
            </checklist>

            <pr_title_format>
            Use: Type: brief description
            Types: Fix, Feature, Refactor, Docs, Test

            Examples:
            - Fix: Null reference in UserService.Save
            - Feature: Add retry logic to API client
            - Refactor: Extract validation to separate class
            </pr_title_format>
            """;

        public const string Tools = """
            <context>
            You have access to Roslyn MCP tools for C# code analysis. These tools provide semantic understanding of code, unlike text-based search.
            </context>

            <tool_selection_guide>
            Use this guide to select the right tool:

            | Task | Roslyn Tool | Why NOT native tool |
            |------|-------------|---------------------|
            | Find type/method | roslyn_find_symbol | Grep finds text, not symbols |
            | See class structure | roslyn_get_type_members | Read shows raw text, not structure |
            | Read a method | roslyn_get_method_body | Read requires knowing line numbers |
            | Edit a method | roslyn_update_method | Edit can break code with text patterns |
            | Add new member | roslyn_add_member | Edit doesn't format or place correctly |
            | Find usages | roslyn_get_references | Grep finds text matches, not usages |
            | Find callers | roslyn_get_callers | References includes non-calls |
            | Find implementations | roslyn_get_implementations | Grep can't follow inheritance |
            | Check errors | roslyn_get_diagnostics | dotnet build output is harder to parse |
            | Fix warning | roslyn_apply_code_fix | Manual edit may introduce errors |
            | Fix many warnings | roslyn_batch_apply_code_fixes | One-by-one is slow |
            | Rename | roslyn_rename_symbol | Find/replace misses some references |
            </tool_selection_guide>

            <when_to_use_native_tools>
            Use native tools (Read, Edit, Grep, Glob) only for:
            - Non-C# files: JSON, XML, YAML, markdown, .csproj
            - New files: Use Write to create, then roslyn_add_member to populate
            - Very small files: < 100 lines where Read/Edit is simpler
            - When MCP server is not connected
            </when_to_use_native_tools>

            <common_workflows>
            Understanding a large class:
            1. roslyn_get_type_members(typeName) → see all members
            2. roslyn_get_method_body(typeName, methodName) → read specific method
            3. roslyn_update_method(...) → make targeted change

            Impact analysis before refactoring:
            1. roslyn_find_symbol(pattern) → locate the symbol
            2. roslyn_get_callers(filePath, line, column) → see who calls it
            3. Assess impact before changing signature

            Fixing compiler warnings:
            1. roslyn_get_diagnostics(severityFilter: "warning") → see all warnings
            2. roslyn_get_diagnostics(diagnosticId: "CS8618") → get details
            3. roslyn_batch_apply_code_fixes(diagnosticId: "CS8618") → auto-fix
            </common_workflows>
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
