"""
PostToolUse hook — saves Edit operations to rag.db via the RAG server.
Stores a concise summary: file path + diff.
"""

import json
import os
import sys
import urllib.error
import urllib.request

SERVER_URL = "http://127.0.0.1:9781"


def main():
    try:
        data = json.load(sys.stdin)
    except (json.JSONDecodeError, EOFError):
        return

    tool = data.get("tool_name", "")
    if tool != "Edit":
        return

    tool_input = data.get("tool_input", {})
    file_path = tool_input.get("file_path", "")
    old_string = tool_input.get("old_string", "")
    new_string = tool_input.get("new_string", "")

    if not file_path or not new_string:
        return

    # Build concise edit summary
    filename = os.path.basename(file_path)
    lines = [f"Edit: {filename}"]

    # Include structured patch if available
    response = data.get("tool_response", {})
    patch = response.get("structuredPatch", [])
    if patch:
        for hunk in patch:
            for line in hunk.get("lines", []):
                lines.append(line)
    else:
        # Fallback: show old -> new
        if old_string:
            lines.append(f"- {old_string[:200]}")
        lines.append(f"+ {new_string[:200]}")

    content = "\n".join(lines)

    # Post to RAG server
    try:
        req = urllib.request.Request(
            f"{SERVER_URL}/add",
            data=json.dumps({"content": content, "role": "edit"}).encode(),
            headers={"Content-Type": "application/json"},
        )
        urllib.request.urlopen(req, timeout=3)
    except (urllib.error.URLError, OSError):
        pass


if __name__ == "__main__":
    main()
