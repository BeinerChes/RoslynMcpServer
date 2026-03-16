"""
Stop hook — saves Claude's response to rag.db via the RAG server.
"""

import json
import sys
import urllib.error
import urllib.request

SERVER_URL = "http://127.0.0.1:9781"


def main():
    try:
        data = json.load(sys.stdin)
    except (json.JSONDecodeError, EOFError):
        return

    message = data.get("last_assistant_message", "")
    if not message or len(message.strip()) < 10:
        return

    # Post to RAG server
    try:
        req = urllib.request.Request(
            f"{SERVER_URL}/add",
            data=json.dumps({"content": message}).encode(),
            headers={"Content-Type": "application/json"},
        )
        urllib.request.urlopen(req, timeout=3)
    except (urllib.error.URLError, OSError):
        pass  # Server not running, skip silently


if __name__ == "__main__":
    main()
