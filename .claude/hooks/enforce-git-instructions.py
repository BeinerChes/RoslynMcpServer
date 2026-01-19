#!/usr/bin/env python3
"""Hook: Enforce calling roslyn_get_instructions("git") before git commit/push"""
import sys
import json
import re

def main():
    try:
        data = json.load(sys.stdin)
    except:
        sys.exit(0)  # Can't parse input, allow

    command = data.get('tool_input', {}).get('command', '')

    # Check if this is a git commit or push command
    if not re.search(r'git\s+(commit|push)', command):
        sys.exit(0)  # Not a commit/push, allow

    transcript_path = data.get('transcript_path', '')

    if transcript_path:
        try:
            with open(transcript_path, 'r', encoding='utf-8') as f:
                content = f.read()
                # Check if roslyn_get_instructions with "git" was called
                if 'roslyn_get_instructions' in content and '"git"' in content:
                    sys.exit(0)  # Instructions were called, allow
        except:
            pass

    # Block the commit
    print('BLOCKED: You must call roslyn_get_instructions("git") before committing.', file=sys.stderr)
    print('Run: roslyn_get_instructions(topic: "git") and follow those instructions.', file=sys.stderr)
    sys.exit(2)

if __name__ == '__main__':
    main()
