#!/usr/bin/env python3
"""
Hook: Suggest Roslyn MCP tools for C# file edits.

This hook shows a SUGGESTION when Edit/Write is used on .cs files,
recommending Roslyn tools for semantic code modifications.
Uses exit code 0 (allow) with a message to avoid "hook error" presentation.

The suggestion is skipped if:
1. roslyn_get_instructions("tools") was called recently (generates a valid token)
2. The content includes a bypass marker: // ROSLYN_BYPASS: <reason>

The bypass marker should explain why Roslyn tools aren't being used, e.g.:
  // ROSLYN_BYPASS: Adding comment to non-compiled file
  // ROSLYN_BYPASS: Creating new file, will use roslyn_add_member after
  // ROSLYN_BYPASS: Editing .csproj embedded C# code
"""
import sys
import json
import os
import time
import re


def get_token_file_path():
    """Get path to the tools token file."""
    home = os.path.expanduser("~")
    return os.path.join(home, ".claude", "roslyn-tools-token")


def get_log_file_path():
    """Get path to the suggestions log file."""
    home = os.path.expanduser("~")
    return os.path.join(home, ".claude", "roslyn-suggestions.log")


def is_token_valid():
    """Check if a valid (non-expired) tools token exists."""
    token_file = get_token_file_path()

    if not os.path.exists(token_file):
        return False, "No token file found"

    try:
        # Check file age (token valid for 10 minutes)
        file_age = time.time() - os.path.getmtime(token_file)
        max_age = 10 * 60  # 10 minutes in seconds

        if file_age > max_age:
            return False, f"Token expired ({file_age/60:.1f} minutes old, max 10 minutes)"

        # Token exists and is fresh
        return True, "Valid token"
    except Exception as e:
        return False, f"Error reading token: {e}"


def has_bypass_marker(content):
    """Check if content contains a ROSLYN_BYPASS marker with a reason."""
    if not content:
        return False, None

    # Match: // ROSLYN_BYPASS: <reason>
    pattern = r'//\s*ROSLYN_BYPASS:\s*(.+)'
    match = re.search(pattern, content)

    if match:
        reason = match.group(1).strip()
        if reason:
            return True, reason

    return False, None


def main():
    try:
        data = json.load(sys.stdin)
    except:
        sys.exit(0)  # Can't parse input, allow (fail-safe)

    tool_name = data.get('tool_name', '')
    tool_input = data.get('tool_input', {})
    file_path = tool_input.get('file_path', '')

    # Only enforce for .cs files
    if not file_path.lower().endswith('.cs'):
        sys.exit(0)  # Not a C# file, allow silently

    # Check for valid token (from roslyn_get_instructions("tools"))
    token_valid, token_msg = is_token_valid()
    if token_valid:
        # Token is valid, allow the operation
        print(f'[Roslyn Hook] Token valid - allowing {tool_name} on C# file', file=sys.stderr)
        sys.exit(0)

    # Check for bypass marker in content
    content_to_check = ""
    if tool_name == "Edit":
        content_to_check = tool_input.get('new_string', '')
    elif tool_name == "Write":
        content_to_check = tool_input.get('content', '')

    has_bypass, bypass_reason = has_bypass_marker(content_to_check)
    if has_bypass:
        print(f'[Roslyn Hook] Bypass accepted: {bypass_reason}', file=sys.stderr)
        sys.exit(0)

    # Write suggestion to log file (Claude Code doesn't display stdout for exit 0)
    from datetime import datetime
    log_file = get_log_file_path()
    try:
        with open(log_file, 'a') as f:
            f.write(f'\n[{datetime.now().strftime("%H:%M:%S")}] {tool_name}: {file_path}\n')
            f.write(f'  Token: {token_msg}\n')
            f.write('  Suggestion: Use Roslyn tools (roslyn_update_method, roslyn_add_member, etc.)\n')
            f.write('  Alternative: Add // ROSLYN_BYPASS: <reason> to skip this suggestion\n')
    except:
        pass  # Don't fail if logging fails

    # Exit 0 - allow the operation (suggestion logged to ~/.claude/roslyn-suggestions.log)
    sys.exit(0)


if __name__ == '__main__':
    main()
