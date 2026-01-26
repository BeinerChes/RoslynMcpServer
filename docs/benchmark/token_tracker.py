#!/usr/bin/env python3
"""
Track token usage from Claude Code JSONL session files.
Designed for benchmarking MCP tools vs native tools.

Usage:
    python token_tracker.py                    # Show session summary
    python token_tracker.py mark               # Get current line number (use before tool call)
    python token_tracker.py since <line>       # Show usage since line number
    python token_tracker.py tool <line>        # Show per-tool breakdown since line
    python token_tracker.py summary            # Full session summary
"""
import json
import sys
from pathlib import Path
from collections import defaultdict

JSONL_PATH = Path.home() / ".claude/projects/C--Users-cbein-source-repos-RoslynMcpServer"

def get_latest_session():
    """Find the most recent session file."""
    files = list(JSONL_PATH.glob("*.jsonl"))
    if not files:
        return None
    return max(files, key=lambda f: f.stat().st_mtime)

def parse_session(session_file=None, start_line=0):
    """Parse session and extract per-message metrics."""
    if session_file is None:
        session_file = get_latest_session()

    if not session_file or not session_file.exists():
        return []

    messages = []
    with open(session_file, 'r', encoding='utf-8') as f:
        for i, line in enumerate(f):
            if i < start_line:
                continue
            try:
                data = json.loads(line)
                msg_type = data.get("type")
                message = data.get("message", {})

                entry = {
                    "line": i,
                    "type": msg_type,
                    "timestamp": data.get("timestamp"),
                }

                # Extract usage if present
                if "usage" in message:
                    usage = message["usage"]
                    entry["usage"] = {
                        "input": usage.get("input_tokens", 0),
                        "output": usage.get("output_tokens", 0),
                        "cache_read": usage.get("cache_read_input_tokens", 0),
                        "cache_create": usage.get("cache_creation_input_tokens", 0),
                    }

                # Extract tool info
                content = message.get("content", [])
                if isinstance(content, list):
                    for item in content:
                        if isinstance(item, dict):
                            if item.get("type") == "tool_use":
                                entry["tool_call"] = item.get("name")
                            elif item.get("type") == "tool_result":
                                entry["tool_result"] = True
                                # Estimate result size from content
                                result_content = item.get("content", "")
                                if isinstance(result_content, str):
                                    entry["result_chars"] = len(result_content)
                            elif item.get("type") == "thinking":
                                entry["has_thinking"] = True

                messages.append(entry)
            except json.JSONDecodeError:
                continue

    return messages

def analyze_tool_usage(messages):
    """Analyze per-tool token usage."""
    tools = defaultdict(lambda: {
        "calls": 0,
        "input_tokens": 0,
        "output_tokens": 0,
        "cache_created": 0,
        "result_chars": 0,
    })

    current_tool = None
    for msg in messages:
        if "tool_call" in msg:
            current_tool = msg["tool_call"]
            tools[current_tool]["calls"] += 1

        if "usage" in msg:
            usage = msg["usage"]
            if current_tool:
                tools[current_tool]["cache_created"] += usage["cache_create"]
                tools[current_tool]["output_tokens"] += usage["output"]

        if "result_chars" in msg and current_tool:
            tools[current_tool]["result_chars"] += msg["result_chars"]

    return dict(tools)

def get_line_count(session_file=None):
    """Get current line count of session file."""
    if session_file is None:
        session_file = get_latest_session()
    if not session_file or not session_file.exists():
        return 0
    with open(session_file, 'r', encoding='utf-8') as f:
        return sum(1 for _ in f)

def format_tokens(n):
    """Format token count with K suffix."""
    if n >= 1000:
        return f"{n/1000:.1f}K"
    return str(n)

def main():
    if len(sys.argv) < 2:
        # Default: show current position
        count = get_line_count()
        session = get_latest_session()
        print(f"Session: {session.name if session else 'None'}")
        print(f"Current line: {count}")
        return

    cmd = sys.argv[1]

    if cmd == "mark":
        count = get_line_count()
        print(f"MARK:{count}")

    elif cmd == "since":
        start = int(sys.argv[2]) if len(sys.argv) > 2 else 0
        messages = parse_session(start_line=start)

        totals = {"input": 0, "output": 0, "cache_read": 0, "cache_create": 0}
        tool_calls = 0
        thinking_blocks = 0

        for msg in messages:
            if "usage" in msg:
                for k, v in msg["usage"].items():
                    totals[k] += v
            if "tool_call" in msg:
                tool_calls += 1
            if msg.get("has_thinking"):
                thinking_blocks += 1

        print(f"=== Tokens since line {start} ===")
        print(f"New context (cache_create): {format_tokens(totals['cache_create'])}")
        print(f"Cached context read:        {format_tokens(totals['cache_read'])}")
        print(f"Output tokens:              {format_tokens(totals['output'])}")
        print(f"Tool calls:                 {tool_calls}")
        print(f"Thinking blocks:            {thinking_blocks}")

    elif cmd == "tool":
        start = int(sys.argv[2]) if len(sys.argv) > 2 else 0
        messages = parse_session(start_line=start)
        tools = analyze_tool_usage(messages)

        print(f"=== Per-Tool Usage since line {start} ===")
        print(f"{'Tool':<40} {'Calls':>6} {'New Context':>12} {'Output':>8}")
        print("-" * 70)

        total_context = 0
        total_output = 0
        total_calls = 0

        for name, stats in sorted(tools.items()):
            print(f"{name:<40} {stats['calls']:>6} {format_tokens(stats['cache_created']):>12} {format_tokens(stats['output_tokens']):>8}")
            total_context += stats['cache_created']
            total_output += stats['output_tokens']
            total_calls += stats['calls']

        print("-" * 70)
        print(f"{'TOTAL':<40} {total_calls:>6} {format_tokens(total_context):>12} {format_tokens(total_output):>8}")

    elif cmd == "summary":
        messages = parse_session()
        tools = analyze_tool_usage(messages)

        print("=== Full Session Summary ===")
        print(f"Total messages: {len(messages)}")
        print()
        print(f"{'Tool':<40} {'Calls':>6} {'New Context':>12}")
        print("-" * 60)

        for name, stats in sorted(tools.items(), key=lambda x: -x[1]['cache_created']):
            if stats['calls'] > 0:
                print(f"{name:<40} {stats['calls']:>6} {format_tokens(stats['cache_created']):>12}")

    else:
        print(f"Unknown command: {cmd}")
        print("Usage: token_tracker.py [mark|since <line>|tool <line>|summary]")

if __name__ == "__main__":
    main()
