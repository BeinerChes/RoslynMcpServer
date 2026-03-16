"""
RAG MCP Server — exposes RAG memory as MCP tools.
Thin wrapper over the RAG HTTP server (port 9781).
"""

import json
import urllib.error
import urllib.request

from mcp.server.fastmcp import FastMCP

SERVER_URL = "http://127.0.0.1:9781"

mcp = FastMCP("rag", log_level="WARNING")


def _post(path, data):
    req = urllib.request.Request(
        f"{SERVER_URL}{path}",
        data=json.dumps(data).encode(),
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=5) as resp:
        return json.loads(resp.read())


def _get(path):
    with urllib.request.urlopen(f"{SERVER_URL}{path}", timeout=5) as resp:
        return json.loads(resp.read())


@mcp.tool()
def rag_get(entry_id: int, max_tokens: int = 500) -> str:
    """Fetch full content of a RAG memory entry by ID.
    Use this when you see a relevant match in <rag_memory> and need the full context.
    """
    try:
        result = _get(f"/get/{entry_id}?max_tokens={max_tokens}")
        if "error" in result:
            return result["error"]
        return f"[{result['role']}] (#{result['id']})\n{result['content']}"
    except urllib.error.URLError:
        return "RAG server not running."


@mcp.tool()
def rag_search(query: str, top_k: int = 3, threshold: float = 0.35) -> str:
    """Search RAG memory for entries similar to the query.
    Returns matched entries with scores. Use rag_get to fetch full content.
    """
    try:
        result = _post("/prompt", {
            "prompt": query,
            "top_k": top_k,
            "threshold": threshold,
        })
        entries = result.get("results", [])
        if not entries:
            return "No matches found."
        lines = []
        for e in entries:
            lines.append(f"- (#{e['id']}) [{e['role']}] score={e['score']} | {e['content']}")
        return "\n".join(lines)
    except urllib.error.URLError:
        return "RAG server not running."


@mcp.tool()
def rag_add(content: str, role: str = "claude") -> str:
    """Save content to RAG memory. Use role='user' for user context, 'claude' for your own notes."""
    try:
        result = _post("/add", {"content": content, "role": role})
        return f"Saved as entry #{result['id']} ({result['chunks']} chunks)"
    except urllib.error.URLError:
        return "RAG server not running."


if __name__ == "__main__":
    mcp.run(transport="stdio")
