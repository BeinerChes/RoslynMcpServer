# CLAUDE.md - C# Development with Roslyn MCP

When working with C# code, use the Roslyn MCP server for guidance:

- Before modifying C# code: `roslyn_get_instructions(topic: "code")`
- Before git operations: `roslyn_get_instructions(topic: "git")`
- Before creating PR: `roslyn_get_instructions(topic: "pre-pr")`

Use Roslyn MCP tools (`roslyn_*`) instead of native tools (Grep, Read, Edit) for C# files.
