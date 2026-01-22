#!/usr/bin/env python3
"""
Hook: Suggest Roslyn MCP tools for C# file edits.

This hook shows a SUGGESTION when Edit/Write is used on .cs files,
recommending Roslyn tools for semantic code modifications.
Uses exit code 0 (allow) with a message to avoid "hook error" presentation.

The suggestion is skipped if:
1. roslyn_get_instructions("tools") was called recently for the same solution (1 min token)
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
import hashlib


def find_solution_file(file_path):
    """Find the .sln or .slnx file by walking up from the file path."""
    current = os.path.dirname(os.path.abspath(file_path))

    # Walk up to 10 levels
    for _ in range(10):
        if not current or current == os.path.dirname(current):
            break

        # Check for solution files
        for ext in ['.slnx', '.sln']:
            for item in os.listdir(current):
                if item.endswith(ext):
                    return os.path.join(current, item)

        current = os.path.dirname(current)

    return None


def get_solution_hash(solution_path):
    """Get a short hash of the solution path for unique token filename."""
    if not solution_path:
        return "global"
    # Use first 8 chars of MD5 hash
    return hashlib.md5(solution_path.lower().encode()).hexdigest()[:8]


def get_token_file_path(solution_path):
    """Get path to the per-solution tools token file."""
    home = os.path.expanduser("~")
    solution_hash = get_solution_hash(solution_path)
    return os.path.join(home, ".claude", f"roslyn-tools-token-{solution_hash}")


def get_log_file_path():
    """Get path to the suggestions log file."""
    home = os.path.expanduser("~")
    return os.path.join(home, ".claude", "roslyn-suggestions.log")


def is_token_valid(solution_path):
    """Check if a valid (non-expired) tools token exists for this solution."""
    token_file = get_token_file_path(solution_path)

    if not os.path.exists(token_file):
        return False, "No token file found"

    try:
        # Check file age (token valid for 1 minute)
        file_age = time.time() - os.path.getmtime(token_file)
        max_age = 1 * 60  # 1 minute in seconds

        if file_age > max_age:
            return False, f"Token expired ({file_age/60:.1f} minutes old, max 1 minute)"

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

    # Find solution file for this C# file
    solution_path = find_solution_file(file_path)

    # Check for valid token (from roslyn_get_instructions("tools"))
    token_valid, token_msg = is_token_valid(solution_path)
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
            if solution_path:
                f.write(f'  Solution: {os.path.basename(solution_path)}\n')
            f.write(f'  Token: {token_msg}\n')
            f.write('  Suggestion: Use Roslyn tools (roslyn_update_method, roslyn_add_member, etc.)\n')
            f.write('  Alternative: Add // ROSLYN_BYPASS: <reason> to skip this suggestion\n')
    except:
        pass  # Don't fail if logging fails

    # Exit 0 - allow the operation (suggestion logged to ~/.claude/roslyn-suggestions.log)
    sys.exit(0)


if __name__ == '__main__':
    main()
