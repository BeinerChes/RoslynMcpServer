#!/usr/bin/env python3
"""
Hook: Enforce calling roslyn_get_instructions("tools") before Read on C# files.

This hook BLOCKS Read operations on .cs files unless a valid tools token exists.
Forces Claude to use Roslyn MCP tools (roslyn_get_method_body, etc.) for C# code.

Tokens are per-solution (based on solution file hash) and valid for 1 minute.

Flow:
1. roslyn_get_instructions("tools") generates a token and writes to ~/.claude/roslyn-tools-token-{hash}
2. This hook reads the token and validates it (file age < 1 min)
3. If valid, Read is allowed
4. If invalid/missing, Read is BLOCKED with helpful message
"""
import sys
import json
import os
import time
import hashlib
import urllib.request
import urllib.error
import urllib.parse


def find_solution_file(file_path):
    """Find the .sln or .slnx file by walking up from the file path."""
    current = os.path.dirname(os.path.abspath(file_path))

    # Walk up to 10 levels
    for _ in range(10):
        if not current or current == os.path.dirname(current):
            break

        # Check for solution files
        try:
            for ext in ['.slnx', '.sln']:
                for item in os.listdir(current):
                    if item.endswith(ext):
                        return os.path.join(current, item)
        except:
            pass

        current = os.path.dirname(current)

    return None


def get_solution_hash(solution_path):
    """Get a short hash of the solution path for unique token filename.

    Returns None if no solution path provided - caller must handle this case.
    Global tokens are not supported.
    """
    if not solution_path:
        return None
    # Use first 8 chars of MD5 hash
    return hashlib.md5(solution_path.lower().encode()).hexdigest()[:8]


def get_token_file_path(solution_path):
    """Get path to the per-solution tools token file."""
    home = os.path.expanduser("~")
    solution_hash = get_solution_hash(solution_path)
    return os.path.join(home, ".claude", f"roslyn-tools-token-{solution_hash}")


def get_hook_port():
    """Read the port number from the port file."""
    port_file = os.path.join(os.path.expanduser('~'), '.claude', 'roslyn-hook-port')
    try:
        with open(port_file, 'r') as f:
            return int(f.read().strip())
    except:
        return None


def get_tools_token(solution_path):
    """Read the tools token from the per-solution token file."""
    token_file = get_token_file_path(solution_path)
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

    # Find solution file for this C# file
    solution_path = find_solution_file(file_path)
    if not solution_path:
        print('BLOCKED: No solution file (.sln or .slnx) found.', file=sys.stderr)
        print('Run: roslyn_get_instructions(topic: "tools", solutionPath: "<path>") with the correct solution path.', file=sys.stderr)
        print('Find the solution file first with: glob pattern "*.sln*"', file=sys.stderr)
        sys.exit(2)

    # Get the token
    token = get_tools_token(solution_path)
    if not token:
        print(f'BLOCKED: No tools token found for {os.path.basename(solution_path)}.', file=sys.stderr)
        print(f'Run: roslyn_get_instructions(topic: "tools", solutionPath: "{solution_path}")', file=sys.stderr)
        print('Alternative: Use roslyn_get_method_body or roslyn_get_type_members instead of Read.', file=sys.stderr)
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
        print('Run: roslyn_get_instructions(topic: "tools") before reading C# files.', file=sys.stderr)
        print('Alternative: Use roslyn_get_method_body or roslyn_get_type_members instead of Read.', file=sys.stderr)
        sys.exit(2)


if __name__ == '__main__':
    main()
