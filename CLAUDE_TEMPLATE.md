# CLAUDE.md Template for C# Projects

> **Setup Instructions:**
> 1. Copy this file to your project root as `CLAUDE.md`
> 2. Customize the "Project Overview" section
> 3. Choose your workflow template (see below)
> 4. Delete this intro section

---

## Quick Setup (Recommended)

Instead of manually maintaining instructions, use the Roslyn MCP server to get up-to-date guidance:

```bash
# Get a template suited to your workflow
roslyn_get_template(template: "standard")   # Basic workflow
roslyn_get_template(template: "tdd")        # Test-driven development
roslyn_get_template(template: "team")       # Full team workflow with issues
roslyn_get_template(template: "minimal")    # Just MCP pointers
```

Copy the returned content to your CLAUDE.md.

---

# CLAUDE.md - Instructions for Claude Code

## Project Overview

<!-- Customize: Brief description of what this project does -->

## Tool Preferences

When working with C# files in .NET solutions, use Roslyn MCP tools.

For full tool preferences: `roslyn_get_instructions(topic: "tools")`

## Development Workflows

Before modifying C# code:
```
roslyn_get_instructions(topic: "code")
```

Before git operations (issues, branches, commits, PRs):
```
roslyn_get_instructions(topic: "git")
```

Before creating a pull request:
```
roslyn_get_instructions(topic: "pre-pr")
```

For test-driven development workflow:
```
roslyn_get_instructions(topic: "tdd")
```

## Project Structure

<!-- Customize this section for your project -->
```
YourProject/
├── CLAUDE.md                 # This file
├── YourProject.sln           # Solution file
├── src/
│   └── YourProject/          # Main project
└── tests/
    └── YourProject.Tests/    # Test project
```

## Build Commands

```bash
dotnet build        # Build
dotnet test         # Run tests
dotnet run          # Run the application
```

<!-- Add any project-specific commands here -->
