#!/usr/bin/env python3
"""
Hook: Enforce branch naming convention before git commit.

Blocks commits if branch name doesn't follow the pattern:
- issues/<number> (required for feature work)
- rc/* (release candidates - allowed)
- main, master (default branches - allowed but discouraged for direct commits)
- docs/* (documentation-only changes - allowed)

This enforces the git workflow instruction: "Branch name format: issues/N where N is issue number"
"""
import sys
import json
import re
import subprocess


def get_current_branch():
    """Get the current git branch name."""
    try:
        result = subprocess.run(
            ['git', 'rev-parse', '--abbrev-ref', 'HEAD'],
            capture_output=True, text=True, timeout=5
        )
        return result.stdout.strip() if result.returncode == 0 else None
    except:
        return None


def is_branch_allowed(branch):
    """Check if branch name follows allowed patterns."""
    if not branch:
        return True, None  # Can't determine branch, allow

    allowed_patterns = [
        r'^issues/\d+$',      # issues/42 - standard feature branch
        r'^rc/',              # rc/1.0.3 - release candidates
        r'^main$',            # main branch
        r'^master$',          # master branch
        r'^docs/',            # docs/* - documentation changes
        r'^hotfix/',          # hotfix/* - urgent fixes
    ]

    for pattern in allowed_patterns:
        if re.match(pattern, branch):
            return True, None

    return False, branch


def main():
    try:
        data = json.load(sys.stdin)
    except:
        sys.exit(0)  # Can't parse input, allow

    command = data.get('tool_input', {}).get('command', '')

    # Only check git commit commands
    if not re.search(r'git\s+commit', command):
        sys.exit(0)  # Not a commit, allow

    branch = get_current_branch()
    allowed, current = is_branch_allowed(branch)

    if allowed:
        sys.exit(0)  # Branch name is valid
    else:
        print(f'BLOCKED: Branch "{current}" does not follow naming convention.', file=sys.stderr)
        print('', file=sys.stderr)
        print('Required: issues/<number> (e.g., issues/42)', file=sys.stderr)
        print('', file=sys.stderr)
        print('Workflow:', file=sys.stderr)
        print('1. Create issue: gh issue create --title "Type: description"', file=sys.stderr)
        print('2. Create branch: git checkout -b issues/<number>', file=sys.stderr)
        print('3. Then commit your changes', file=sys.stderr)
        sys.exit(2)


if __name__ == '__main__':
    main()
