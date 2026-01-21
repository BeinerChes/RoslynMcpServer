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
   | New feature added | `\ReleaseNotes\release_notes_rc_<version>.md`, get version from AssemblyInfoShared.c	s |
 | Bug fixed | `\ReleaseNotes\release_notes_rc_N_N_N_N.md`, get version from AssemblyInfoShared.cs |


3. **Documentation rules:**
   - Keep descriptions concise and accurate
   - Match tool descriptions in README.md with actual tool behavior
   - Don't add unnecessary commentary or emojis
   - Preserve existing formatting style

4. **Verify changes:**
   - Read the updated files to confirm accuracy
   - Ensure no broken links or references

$ARGUMENTS
