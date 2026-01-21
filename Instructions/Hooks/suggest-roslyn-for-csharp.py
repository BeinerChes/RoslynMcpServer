#!/usr/bin/env python3
"""
Hook: Enforce Roslyn MCP tools for C# file edits.

This hook BLOCKS Edit/Write operations on .cs files by default.

To proceed, Claude must either:
1. Call roslyn_get_instructions("tools") first (generates a valid token)
2. Include a bypass marker in the content: // ROSLYN_BYPASS: <reason>

The bypass marker should explain why Roslyn tools can't be used, e.g.:
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

    # BLOCK - No valid token and no bypass marker
    print('')
    print('=' * 70)
    print('BLOCKED: C# file edit requires Roslyn MCP tools or explicit bypass')
    print('=' * 70)
    print('')
    print(f'File: {file_path}')
    print(f'Token status: {token_msg}')
    print('')
    print('TO PROCEED, choose one option:')
    print('')
    print('OPTION 1 - Use Roslyn tools (RECOMMENDED):')
    print('  1. Call: roslyn_get_instructions("tools")')
    print('  2. Use the appropriate Roslyn tool:')
    print('     - roslyn_update_method: Replace a method implementation')
    print('     - roslyn_add_member: Add new method/property/field to a type')
    print('     - roslyn_delete_member: Remove a member from a type')
    print('     - roslyn_rename_symbol: Rename across entire solution')
    print('')
    print('OPTION 2 - Bypass with reason (if Roslyn not applicable):')
    print('  Add this comment in your code change:')
    print('    // ROSLYN_BYPASS: <your reason here>')
    print('')
    print('  Valid reasons include:')
    print('    - Creating new file (use roslyn_add_member after)')
    print('    - Editing comments or documentation only')
    print('    - Non-standard C# (T4 templates, .csx scripts)')
    print('    - Simple text changes not involving code structure')
    print('')
    print('=' * 70)

    # Exit 1 = BLOCK
    sys.exit(1)


if __name__ == '__main__':
    main()
