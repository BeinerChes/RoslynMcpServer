"""
RAG hook for Claude Code UserPromptSubmit.
Saves every prompt, injects truncated similar past entries.
"""

import json
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

SERVER_URL = "http://127.0.0.1:9781"
VENV_PYTHON = str(Path(__file__).parent / ".venv" / "Scripts" / "python.exe")
SERVER_SCRIPT = str(Path(__file__).parent / "rag_server.py")
MAX_RETRIES = 15
RETRY_DELAY = 0.5


def server_is_running():
    try:
        urllib.request.urlopen(f"{SERVER_URL}/health", timeout=1)
        return True
    except (urllib.error.URLError, OSError):
        return False


def start_server():
    # CREATE_NEW_CONSOLE: own window for logs
    # CREATE_BREAKAWAY_FROM_JOB: detach from Claude Code's process tree
    subprocess.Popen(
        [VENV_PYTHON, SERVER_SCRIPT],
        creationflags=subprocess.CREATE_NEW_CONSOLE | 0x01000000,
        close_fds=True,
    )


def wait_for_server():
    for _ in range(MAX_RETRIES):
        if server_is_running():
            return True
        time.sleep(RETRY_DELAY)
    return False


def prompt(text):
    data = json.dumps({"prompt": text}).encode()
    req = urllib.request.Request(
        f"{SERVER_URL}/prompt",
        data=data,
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=5) as resp:
        return json.loads(resp.read())


def main():
    try:
        input_data = json.load(sys.stdin)
    except (json.JSONDecodeError, EOFError):
        return

    text = input_data.get("prompt", "")
    if not text or len(text.strip()) < 5:
        return

    if not server_is_running():
        start_server()
        # Don't block on cold start — server will be ready for next prompt
        return

    try:
        result = prompt(text)
    except Exception as e:
        print(f"RAG error: {e}", file=sys.stderr)
        return

    entries = result.get("results", [])
    if not entries:
        return

    # Truncate preview to ~15 words
    def preview(text, max_words=15):
        words = text.split()
        if len(words) <= max_words:
            return text
        return " ".join(words[:max_words]) + "..."

    lines = ["<rag_memory>"]
    lines.append("The following entries were found in your cross-session memory (RAG).")
    lines.append("You MUST either:")
    lines.append("  1. Use relevant matches to inform your response (cite the entry ID), OR")
    lines.append("  2. Briefly explain why these matches are not relevant to the current request.")
    lines.append("To fetch full content, call mcp tool: rag_get(entry_id=ID, max_tokens=200)")
    lines.append("")
    lines.append("Matches:")
    for entry in entries:
        role = entry.get("role", "user")
        lines.append(f"  - (#{entry['id']}) [{role}] {preview(entry['content'])}")
    lines.append("</rag_memory>")
    print("\n".join(lines))


if __name__ == "__main__":
    main()
