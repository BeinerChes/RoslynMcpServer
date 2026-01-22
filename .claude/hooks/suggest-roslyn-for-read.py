#!/usr/bin/env python3
"""
Hook: Suggest Roslyn MCP tools for reading C# files.

This hook shows a SUGGESTION when Read is used on .cs files,
recommending Roslyn tools for semantic code understanding.
Uses exit code 0 (allow) with a message to avoid "hook error" presentation.

The suggestion is skipped if roslyn_get_instructions("tools") was called recently
for the same solution. Tokens are per-solution and valid for 1 minute.
"""
import sys
import json
import os
import time
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
        return False

    try:
        file_age = time.time() - os.path.getmtime(token_file)
        max_age = 1 * 60  # 1 minute
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

    # Only suggest for .cs files
    if not file_path.lower().endswith('.cs'):
        sys.exit(0)  # Not a C# file, allow silently

    # Find solution file for this C# file
    solution_path = find_solution_file(file_path)

    # If they already called roslyn_get_instructions("tools") for this solution, allow
    if is_token_valid(solution_path):
        sys.exit(0)  # Already aware of Roslyn tools

    # Write suggestion to log file (Claude Code doesn't display stdout for exit 0)
    from datetime import datetime
    log_file = get_log_file_path()
    try:
        with open(log_file, 'a') as f:
            f.write(f'\n[{datetime.now().strftime("%H:%M:%S")}] Read: {file_path}\n')
            if solution_path:
                f.write(f'  Solution: {os.path.basename(solution_path)}\n')
            f.write('  Suggestion: Use Roslyn tools for C# files (roslyn_get_type_members, roslyn_get_method_body, etc.)\n')
    except:
        pass  # Don't fail if logging fails

    # Exit 0 - allow the Read (suggestion logged to ~/.claude/roslyn-suggestions.log)
    sys.exit(0)


if __name__ == '__main__':
    main()
