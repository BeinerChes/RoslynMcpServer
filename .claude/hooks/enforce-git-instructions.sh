#!/bin/bash
# Hook: Enforce calling roslyn_get_instructions("git") before git commit/push
# Exit 2 = block the command

INPUT=$(cat)
COMMAND=$(echo "$INPUT" | jq -r '.tool_input.command // ""')

# Check if this is a git commit or push command
if [[ "$COMMAND" =~ git.*commit|git.*push ]]; then
  TRANSCRIPT=$(echo "$INPUT" | jq -r '.transcript_path')

  # Check if roslyn_get_instructions with "git" topic was called in this session
  if [ -f "$TRANSCRIPT" ]; then
    if ! grep -q 'roslyn_get_instructions.*"git"' "$TRANSCRIPT" 2>/dev/null; then
      echo "BLOCKED: You must call roslyn_get_instructions(\"git\") before committing." >&2
      echo "Run: roslyn_get_instructions(topic: \"git\") and follow those instructions." >&2
      exit 2
    fi
  fi
fi

exit 0
