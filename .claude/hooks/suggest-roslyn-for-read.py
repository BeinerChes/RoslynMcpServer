#!/usr/bin/env python3
"""
Hook: Suggest Roslyn MCP tools for reading C# files.

This hook shows a SUGGESTION when Read is used on .cs files,
recommending Roslyn tools for semantic code understanding.
Uses exit code 0 (allow) with a message to avoid "hook error" presentation.

The suggestion is skipped if roslyn_get_instructions("tools") was called recently.
"""
import sys
import json
import os
import time


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
        return False

    try:
        file_age = time.time() - os.path.getmtime(token_file)
        max_age = 10 * 60  # 10 minutes
        return file_age <= max_age
    except:
        return False


def main():
    try:
        data = json.load(sys.stdin)
    except:
        sys.exit(0)  # Can't parse input, allow

    tool_input = data.get('tool_input', {})
    file_path = tool_input.get('file_path', '')

    # Only block for .cs files
    if not file_path.lower().endswith('.cs'):
        sys.exit(0)  # Not a C# file, allow silently

    # If they already called roslyn_get_instructions("tools"), allow
    if is_token_valid():
        sys.exit(0)  # Already aware of Roslyn tools

    # Write suggestion to log file (Claude Code doesn't display stdout for exit 0)
    from datetime import datetime
    log_file = get_log_file_path()
    try:
        with open(log_file, 'a') as f:
            f.write(f'\n[{datetime.now().strftime("%H:%M:%S")}] Read: {file_path}\n')
            f.write('  Suggestion: Use Roslyn tools for C# files (roslyn_get_type_members, roslyn_get_method_body, etc.)\n')
    except:
        pass  # Don't fail if logging fails

    # Exit 0 - allow the Read (suggestion logged to ~/.claude/roslyn-suggestions.log)
    sys.exit(0)


if __name__ == '__main__':
    main()
