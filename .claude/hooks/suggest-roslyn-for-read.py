#!/usr/bin/env python3
"""
Hook: Suggest Roslyn MCP tools for reading C# files.

This hook BLOCKS Read operations on .cs files by default.
Uses exit code 2 for hard blocking (cannot be bypassed without token).

To proceed, call roslyn_get_instructions("tools") first.
"""
import sys
import json
import os
import time


def get_token_file_path():
    """Get path to the tools token file."""
    home = os.path.expanduser("~")
    return os.path.join(home, ".claude", "roslyn-tools-token")


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

    # Print blocking message to stderr
    print('BLOCKED: Consider using Roslyn tools for C# files.', file=sys.stderr)
    print('', file=sys.stderr)
    print('Roslyn tools provide semantic understanding, not just text:', file=sys.stderr)
    print('  - roslyn_get_type_members: Get all members of a class/interface', file=sys.stderr)
    print('  - roslyn_get_method_body: Get a specific method implementation', file=sys.stderr)
    print('  - roslyn_find_symbol: Search for types/methods by name', file=sys.stderr)
    print('  - roslyn_get_callers: Find all callers of a method', file=sys.stderr)
    print('', file=sys.stderr)
    print('TO PROCEED: Call roslyn_get_instructions("tools") first.', file=sys.stderr)

    # Exit 2 - hard block that requires token
    sys.exit(2)


if __name__ == '__main__':
    main()
