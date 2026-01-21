# Plan: Enhance architect skill with interactive mode and deep task generation

GitHub Issue: https://github.com/BeinerChes/RoslynMcpServer/issues/65

## Problem Statement
The architect skill currently:
1. Requires solution path as argument, only asks for it if missing
2. Generates high-level task summaries that lack implementation details

Need to add:
1. Interactive scope selection when no args provided
2. Deep task generation with best practices, tests, acceptance criteria

## Completed Fixes

### 1. Interactive Mode Section
Added to `Instructions/Skills/architect/SKILL.md` after `## Input`:
- 4 scope options when no arguments provided (solution, project, class, method)
- Argument parsing logic to determine scope
- Added `AskUserQuestion` to allowed-tools

### 2. Scope-Specific Analysis Sections
Added new section with detailed instructions for:
- **Class Scope Analysis** - members, references, impact, implementations, diagnostics
- **Method Scope Analysis** - body, callers, call graph, impact, knowledge

### 3. Deep Task Generation Template
Replaced section 6.2 with comprehensive template including:
- Problem (location, severity, impact, metrics)
- Root Cause
- Implementation Steps
- Code Changes (before/after)
- Best Practices
- Unit Tests Required (checkboxes)
- Acceptance Criteria (checkboxes)
- Dependencies
- Estimated Complexity

Added examples:
- Performance task (caching)
- Security task (SQL injection fix)

## Test Results
Ready for real-world testing with `/architect` command.

## Current Status
✅ Implementation complete - awaiting user testing

## Files Modified
- `Instructions/Skills/architect/SKILL.md` - All changes

## Next Steps
1. ~~Add interactive mode section to SKILL.md~~ ✅
2. ~~Add deep task generation template~~ ✅
3. ~~Add scope-specific analysis instructions~~ ✅
4. Test the enhanced skill with user
5. Commit and create PR

## Workflow Reminder (MANDATORY)
After each fix:
1. Re-read and follow CLAUDE.md
2. Update this plan
3. Keep working until issue is resolved
