"""Debug: dump Stop hook input to a file so we can see the schema."""
import json
import sys
from pathlib import Path

try:
    data = json.load(sys.stdin)
except:
    data = {"error": "no stdin"}

Path(__file__).parent.joinpath("stop_input.json").write_text(
    json.dumps(data, indent=2, default=str), encoding="utf-8"
)
