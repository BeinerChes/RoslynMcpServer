"""Add an entry to rag.db via the RAG server API."""

import argparse
import json
import urllib.error
import urllib.request

SERVER_URL = "http://127.0.0.1:9781"


def main():
    parser = argparse.ArgumentParser(description="Add a knowledge entry to rag.db")
    parser.add_argument("content", help="The text content to store")
    args = parser.parse_args()

    data = json.dumps({"content": args.content}).encode()
    req = urllib.request.Request(
        f"{SERVER_URL}/add",
        data=data,
        headers={"Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as resp:
            result = json.loads(resp.read())
            print(result["message"])
    except urllib.error.URLError:
        print("Server not running. Start it or send a prompt first.")


if __name__ == "__main__":
    main()
