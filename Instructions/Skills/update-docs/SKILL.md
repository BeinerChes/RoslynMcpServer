---
name: update-docs
description: Update documentation files (README.md, tools.md, etc.) after code changes. Use when tools are added/modified or features change.
model: claude-haiku-4-5-20251001
allowed-tools: Read, Write, Edit, Glob, Grep, Bash
disable-model-invocation: true
---

# Update Documentation

Update all relevant documentation to reflect recent code changes.

## Steps

1. **Check what changed:**
   ```bash
   git diff --name-only HEAD~1
   ```

2. **For each modified source file, update relevant docs:**

   | If changed... | Update... |
   |---------------|-----------|
   | `src/RoslynTools*.cs` | `README.md` (tools table), `Instructions/Topics/tools.md` |
   | `Instructions/Topics/*.md` | Verify consistency with code |
   | `Instructions/Hooks/*.py` | `README.md` (setup section) |
   | New feature added | `README.md` (features section) |

3. **Documentation rules:**
   - Keep descriptions concise and accurate
   - Match tool descriptions in README.md with actual tool behavior
   - Don't add unnecessary commentary or emojis
   - Preserve existing formatting style

4. **Verify changes:**
   - Read the updated files to confirm accuracy
   - Ensure no broken links or references

$ARGUMENTS
