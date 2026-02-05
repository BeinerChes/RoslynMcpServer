#!/usr/bin/env python3
"""
Hook: Enforce calling GetInstructions("tools") before Read on C# files.

This hook BLOCKS Read operations on .cs files unless a valid tools token exists.
Forces Claude to use Roslyn MCP tools (GetMethodBody, etc.) for C# code.

Tokens are stored in .roslyn-mcp/ folder and valid for 1 minute.

Flow:
1. GetInstructions("tools") generates a token and writes to .roslyn-mcp/tools-token
2. This hook reads the token and validates it
3. If valid, Read is allowed
4. If invalid/missing, Read is BLOCKED with helpful message
"""
import sys
import json
import os
import time
import urllib.request
import urllib.error
import urllib.parse


def find_roslyn_mcp_dir(file_path):
    """Find the .roslyn-mcp directory by walking up from the file path."""
    current = os.path.dirname(os.path.abspath(file_path))

    # Walk up to 10 levels
    for _ in range(10):
        if not current or current == os.path.dirname(current):
            break

        roslyn_dir = os.path.join(current, '.roslyn-mcp')
        if os.path.isdir(roslyn_dir):
            return roslyn_dir

        current = os.path.dirname(current)

    return None


def get_hook_port():
    """Read the port number from the port file."""
    port_file = os.path.join(os.path.expanduser('~'), '.claude', 'roslyn-hook-port')
    try:
        with open(port_file, 'r') as f:
            return int(f.read().strip())
    except:
        return None


def get_token(roslyn_dir, topic):
    """Read the token from .roslyn-mcp/{topic}-token."""
    token_file = os.path.join(roslyn_dir, f'{topic}-token')
    try:
        with open(token_file, 'r') as f:
            return f.read().strip()
    except:
        return None


def validate_token_http(token, port):
    """Validate token via HTTP endpoint."""
    try:
        encoded_token = urllib.parse.quote(token, safe='')
        url = f'http://localhost:{port}/validate?token={encoded_token}&topic=tools'
        with urllib.request.urlopen(url, timeout=5) as response:
            data = json.loads(response.read().decode('utf-8'))
            return data.get('valid', False), data.get('message', 'Unknown error')
    except urllib.error.URLError as e:
        return False, f'Cannot connect to validation server: {e.reason}'
    except Exception as e:
        return False, f'Validation error: {e}'


def validate_token_local(token):
    """Fallback: validate token locally by checking timestamp."""
    try:
        parts = token.split('.')
        if len(parts) != 4:
            return False, 'Invalid token format'

        timestamp = int(parts[1])
        age_seconds = time.time() - timestamp

        if age_seconds > 60:  # 1 minute
            return False, f'Token expired ({age_seconds/60:.1f} minutes old, max 1 minute)'

        if age_seconds < 0:
            return False, 'Token timestamp is in the future'

        return True, f'Valid ({age_seconds:.0f}s old)'
    except Exception as e:
        return False, f'Local validation error: {e}'


def main():
    try:
        data = json.load(sys.stdin)
    except:
        sys.exit(0)  # Can't parse input, allow

    tool_input = data.get('tool_input', {})
    file_path = tool_input.get('file_path', '')

    # Only enforce for .cs files
    if not file_path.lower().endswith('.cs'):
        sys.exit(0)  # Not a C# file, allow silently

    # Find .roslyn-mcp directory
    roslyn_dir = find_roslyn_mcp_dir(file_path)
    if not roslyn_dir:
        print('BLOCKED: No .roslyn-mcp directory found.', file=sys.stderr)
        print('Run: GetInstructions(topic: "tools")', file=sys.stderr)
        print('Alternative: Use GetMethodBody or GetTypeMembers instead of Read.', file=sys.stderr)
        sys.exit(2)

    # Get the token
    token = get_token(roslyn_dir, 'tools')
    if not token:
        print('BLOCKED: No tools token found.', file=sys.stderr)
        print('Run: GetInstructions(topic: "tools")', file=sys.stderr)
        print('Alternative: Use GetMethodBody or GetTypeMembers instead of Read.', file=sys.stderr)
        sys.exit(2)

    # Try HTTP validation first
    port = get_hook_port()
    if port:
        valid, message = validate_token_http(token, port)
    else:
        # Fallback to local validation
        valid, message = validate_token_local(token)

    if valid:
        sys.exit(0)  # Allow
    else:
        print(f'BLOCKED: {message}', file=sys.stderr)
        print('Run: GetInstructions(topic: "tools")', file=sys.stderr)
        print('Alternative: Use GetMethodBody or GetTypeMembers instead of Read.', file=sys.stderr)
        sys.exit(2)


if __name__ == '__main__':
    main()
