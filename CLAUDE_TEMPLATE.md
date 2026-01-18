# CLAUDE.md - C# Development with Roslyn MCP

> **What is this file?** This is a CLAUDE.md file - instructions that Claude reads automatically when you start a conversation. Copy this file to your project root as `CLAUDE.md`.

## MANDATORY: Get Instructions Before Operations

**Call `roslyn_get_instructions` with the appropriate topic before each operation:**

| Before doing this... | Call with topic |
|---------------------|-----------------|
| Using any Roslyn tool | `"tools"` |
| Making any code change | `"git"` |
| Modifying C# code | `"code"` |
| Writing or running tests | `"tdd"` |
| Creating a pull request | `"pre-pr"` |

## Project Structure

<!-- Customize this section for your project -->
```
YourProject/
├── CLAUDE.md                 # This file
├── YourProject.sln           # Solution file
├── src/                      # Source code
└── tests/                    # Test projects
```

## Build Commands

```bash
dotnet build        # Build
dotnet test         # Run tests
```
