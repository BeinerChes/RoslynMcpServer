"""
RAG server — loads all-MiniLM-L6-v2 once, serves /prompt over HTTP.
Saves every prompt, returns truncated similar past entries.
Chunks long texts for better embedding quality.
Started lazily by the hook on first prompt, stays alive for the session.
"""

import io
import logging
import os
import re
import sqlite3
import sys
import time
from contextlib import asynccontextmanager
from datetime import datetime
from pathlib import Path

# Suppress all noise BEFORE importing sentence_transformers
os.environ["TOKENIZERS_PARALLELISM"] = "false"
os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["HF_HUB_DISABLE_PROGRESS_BARS"] = "1"
os.environ["TRANSFORMERS_VERBOSITY"] = "error"
os.environ["TRANSFORMERS_NO_ADVISORY_WARNINGS"] = "1"
logging.getLogger("httpx").setLevel(logging.ERROR)
logging.getLogger("sentence_transformers").setLevel(logging.ERROR)
logging.getLogger("transformers").setLevel(logging.ERROR)
logging.getLogger("torch").setLevel(logging.ERROR)
logging.getLogger("huggingface_hub").setLevel(logging.ERROR)

import numpy as np
from fastapi import FastAPI, Request
from pydantic import BaseModel
from sentence_transformers import SentenceTransformer

DB_PATH = Path(__file__).parent / "rag.db"
MODEL_NAME = "all-MiniLM-L6-v2"
PORT = 9781
MAX_TOKENS = 50
CHUNK_SIZE = 100  # ~100 words per chunk (model sweet spot)

model: SentenceTransformer | None = None


def init_db(conn):
    conn.executescript("""
        CREATE TABLE IF NOT EXISTS entries (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            parent_id   INTEGER,
            role        TEXT NOT NULL DEFAULT 'user',
            content     TEXT NOT NULL,
            embedding   BLOB,
            created_at  TEXT DEFAULT (datetime('now')),
            FOREIGN KEY (parent_id) REFERENCES entries(id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS idx_entries_parent ON entries(parent_id);
        CREATE VIRTUAL TABLE IF NOT EXISTS entries_fts USING fts5(
            content, content=entries, content_rowid=id
        );
        CREATE TRIGGER IF NOT EXISTS entries_ai AFTER INSERT ON entries BEGIN
            INSERT INTO entries_fts(rowid, content) VALUES (new.id, new.content);
        END;
        CREATE TRIGGER IF NOT EXISTS entries_ad AFTER DELETE ON entries BEGIN
            INSERT INTO entries_fts(entries_fts, rowid, content) VALUES('delete', old.id, old.content);
        END;
        CREATE TRIGGER IF NOT EXISTS entries_au AFTER UPDATE ON entries BEGIN
            INSERT INTO entries_fts(entries_fts, rowid, content) VALUES('delete', old.id, old.content);
            INSERT INTO entries_fts(rowid, content) VALUES (new.id, new.content);
        END;
    """)


def split_sentences(text):
    """Split text into sentences, preserving sentence boundaries."""
    sentences = re.split(r'(?<=[.!?])\s+', text)
    return [s.strip() for s in sentences if s.strip()]


def chunk_text(text, max_words=CHUNK_SIZE):
    """Split text into chunks of ~max_words at sentence boundaries."""
    words = text.split()
    if len(words) <= max_words:
        return [text]

    sentences = split_sentences(text)
    chunks = []
    current = []
    current_len = 0

    for sentence in sentences:
        sentence_len = len(sentence.split())
        if current_len + sentence_len > max_words and current:
            chunks.append(" ".join(current))
            current = [sentence]
            current_len = sentence_len
        else:
            current.append(sentence)
            current_len += sentence_len

    if current:
        chunks.append(" ".join(current))

    return chunks


def truncate(text, max_tokens=MAX_TOKENS):
    words = text.split()
    if len(words) <= max_tokens:
        return text
    return " ".join(words[:max_tokens]) + "..."


def ascii_safe(text):
    """Replace common Unicode chars with ASCII for clean console output."""
    replacements = {
        "\u2014": "--", "\u2013": "-", "\u2018": "'", "\u2019": "'",
        "\u201c": '"', "\u201d": '"', "\u2026": "...", "\u2192": "->",
        "\u2190": "<-", "\u2022": "*", "\u2713": "[ok]", "\u2717": "[x]",
        "\u00e9": "e", "\u00e8": "e", "\u00e0": "a",
    }
    for u, a in replacements.items():
        text = text.replace(u, a)
    # Strip any remaining non-ASCII
    return text.encode("ascii", errors="replace").decode("ascii")


def banner():
    print("\033[36m")
    print("  +======================================+")
    print("  |        RAG Memory Server              |")
    print("  +======================================+")
    print(f"  |  Model:  {MODEL_NAME:<27s} |")
    print(f"  |  Port:   {PORT:<27d} |")
    print(f"  |  DB:     {DB_PATH.name:<27s} |")
    print("  +======================================+")
    print("\033[0m")


def safe_encode(text):
    """Encode text, ensuring it's a clean string."""
    if not isinstance(text, str):
        text = str(text)
    # Replace null bytes and ensure clean string
    text = text.replace("\x00", " ").strip()
    if not text:
        text = " "
    return model.encode(text)


def save_with_chunks(conn, role, content):
    """Save content, chunking if long. Returns (parent_id, num_chunks)."""
    chunks = chunk_text(content)

    if len(chunks) == 1:
        # Short text — save as single entry with embedding
        emb = safe_encode(content)
        blob = emb.astype(np.float32).tobytes()
        conn.execute(
            "INSERT INTO entries (role, content, embedding) VALUES (?, ?, ?)",
            (role, content, blob),
        )
        conn.commit()
        entry_id = conn.execute("SELECT last_insert_rowid()").fetchone()[0]
        return entry_id, 1

    # Long text — save parent (no embedding), then chunks with embeddings
    conn.execute(
        "INSERT INTO entries (role, content) VALUES (?, ?)",
        (role, content),
    )
    conn.commit()
    parent_id = conn.execute("SELECT last_insert_rowid()").fetchone()[0]

    for chunk in chunks:
        emb = safe_encode(chunk)
        blob = emb.astype(np.float32).tobytes()
        conn.execute(
            "INSERT INTO entries (parent_id, role, content, embedding) VALUES (?, ?, ?, ?)",
            (parent_id, role, chunk, blob),
        )
    conn.commit()

    return parent_id, len(chunks)


@asynccontextmanager
async def lifespan(app: FastAPI):
    global model
    banner()
    print(f"\033[33m  Loading {MODEL_NAME}...\033[0m", flush=True)
    t0 = time.time()
    model = SentenceTransformer(MODEL_NAME)
    elapsed = time.time() - t0
    print(f"\033[32m  Model loaded in {elapsed:.1f}s\033[0m")
    conn = sqlite3.connect(str(DB_PATH))
    init_db(conn)
    count = conn.execute("SELECT COUNT(*) FROM entries WHERE parent_id IS NULL").fetchone()[0]
    conn.close()
    print(f"\033[32m  Database: {count} entries\033[0m")
    print(f"\033[32m  Ready on http://127.0.0.1:{PORT}\033[0m\n")
    yield


app = FastAPI(lifespan=lifespan)


@app.middleware("http")
async def log_requests(request: Request, call_next):
    t0 = time.time()
    response = await call_next(request)
    ms = (time.time() - t0) * 1000
    ts = datetime.now().strftime("%H:%M:%S")
    path = request.url.path
    if path == "/prompt":
        print(f"  \033[90m{ts}\033[0m  \033[35m{path}\033[0m  \033[90m{ms:.0f}ms\033[0m")
    elif path == "/add":
        print(f"  \033[90m{ts}\033[0m  \033[34m{path}\033[0m  \033[90m{ms:.0f}ms\033[0m")
    elif path != "/health":
        print(f"  \033[90m{ts}\033[0m  {path}  \033[90m{ms:.0f}ms\033[0m")
    return response


class PromptRequest(BaseModel):
    prompt: str
    threshold: float = 0.35
    top_k: int = 3


class AddRequest(BaseModel):
    content: str
    role: str = "claude"


def cosine_similarity(a, b):
    norm_a = np.linalg.norm(a)
    norm_b = np.linalg.norm(b)
    if norm_a == 0 or norm_b == 0:
        return 0.0
    return float(np.dot(a, b) / (norm_a * norm_b))


@app.post("/prompt")
def prompt(req: PromptRequest):
    try:
        return _do_prompt(req)
    except Exception as e:
        print(f"  \033[31m  error: {e}\033[0m")
        return {"saved_id": 0, "results": []}


def _do_prompt(req: PromptRequest):
    conn = sqlite3.connect(str(DB_PATH))

    # Save user prompt (with chunking if long)
    new_id, num_chunks = save_with_chunks(conn, "user", req.prompt)

    # Collect IDs to exclude (the entry we just saved + its chunks)
    exclude_ids = {new_id}
    if num_chunks > 1:
        for (cid,) in conn.execute("SELECT id FROM entries WHERE parent_id = ?", (new_id,)):
            exclude_ids.add(cid)

    # --- Vector search: ranked list of (result_id, role, content) ---
    query_emb = safe_encode(req.prompt)
    vector_scored = []
    seen_parents_v = set()
    for id_, parent_id, role, content, emb_blob in conn.execute(
        "SELECT id, parent_id, role, content, embedding FROM entries WHERE embedding IS NOT NULL"
    ):
        if id_ in exclude_ids:
            continue
        emb = np.frombuffer(emb_blob, dtype=np.float32).copy()
        sim = cosine_similarity(query_emb, emb)
        if sim < req.threshold:
            continue
        result_id = parent_id if parent_id else id_
        if result_id in seen_parents_v:
            continue
        seen_parents_v.add(result_id)
        vector_scored.append((sim, result_id, role, content, parent_id))
    vector_scored.sort(reverse=True)
    vector_ranked = [item[1] for item in vector_scored]  # list of result_ids by rank

    # --- FTS search: ranked list of result_ids ---
    fts_ranked = []
    seen_parents_f = set()
    try:
        for id_, parent_id, role, content, rank in conn.execute(
            """
            SELECT e.id, e.parent_id, e.role, e.content, rank
            FROM entries_fts fts JOIN entries e ON e.id = fts.rowid
            WHERE entries_fts MATCH ? ORDER BY rank LIMIT ?
            """,
            (req.prompt, req.top_k * 3),
        ):
            if id_ in exclude_ids:
                continue
            result_id = parent_id if parent_id else id_
            if result_id in seen_parents_f:
                continue
            seen_parents_f.add(result_id)
            fts_ranked.append(result_id)
    except sqlite3.OperationalError:
        pass

    # --- Reciprocal Rank Fusion (k=60) ---
    RRF_K = 60
    rrf_scores = {}
    entry_meta = {}  # result_id -> (role, content, parent_id)

    # Collect metadata from vector results
    for sim, result_id, role, content, parent_id in vector_scored:
        entry_meta[result_id] = (role, content, parent_id)
    # Collect metadata from FTS results (may need DB lookup)
    for result_id in fts_ranked:
        if result_id not in entry_meta:
            row = conn.execute("SELECT role, content, parent_id FROM entries WHERE id = ?", (result_id,)).fetchone()
            if row:
                entry_meta[result_id] = row

    for rank, result_id in enumerate(vector_ranked, start=1):
        rrf_scores[result_id] = rrf_scores.get(result_id, 0.0) + 1.0 / (RRF_K + rank)
    for rank, result_id in enumerate(fts_ranked, start=1):
        rrf_scores[result_id] = rrf_scores.get(result_id, 0.0) + 1.0 / (RRF_K + rank)

    # Build final results sorted by RRF score
    results = []
    for result_id, score in sorted(rrf_scores.items(), key=lambda x: x[1], reverse=True):
        if result_id not in entry_meta:
            continue
        role, content, parent_id = entry_meta[result_id]
        if parent_id:
            parent_row = conn.execute("SELECT content FROM entries WHERE id = ?", (parent_id,)).fetchone()
            preview_content = parent_row[0] if parent_row else content
        else:
            preview_content = content
        results.append({"score": round(score, 4), "id": result_id, "role": role, "content": truncate(preview_content)})

    conn.close()

    top = results[: req.top_k]

    # Log
    preview = ascii_safe(req.prompt[:60]) + ("..." if len(req.prompt) > 60 else "")
    chunks_info = f" ({num_chunks} chunks)" if num_chunks > 1 else ""
    print(f"    \033[33msaved #{new_id}{chunks_info}\033[0m  \033[36m[user]\033[0m  \"{preview}\"")
    if top:
        for r in top:
            snip = ascii_safe(r["content"][:55]) + ("..." if len(r["content"]) > 55 else "")
            role_color = "\033[36m" if r["role"] == "user" else "\033[34m" if r["role"] == "claude" else "\033[35m"
            print(f"    \033[32mmatch\033[0m  {r['score']:.2f}  {role_color}[{r['role']}]\033[0m  \"{snip}\"")
    else:
        print(f"    \033[90mno matches above {req.threshold}\033[0m")

    return {"saved_id": new_id, "results": top}


@app.post("/add")
def add_entry(req: AddRequest):
    conn = sqlite3.connect(str(DB_PATH))
    init_db(conn)

    entry_id, num_chunks = save_with_chunks(conn, req.role, req.content)
    conn.close()

    preview = ascii_safe(req.content[:60]) + ("..." if len(req.content) > 60 else "")
    chunks_info = f" ({num_chunks} chunks)" if num_chunks > 1 else ""
    role_color = "\033[36m" if req.role == "user" else "\033[34m" if req.role == "claude" else "\033[35m"
    print(f"    {role_color}saved #{entry_id}{chunks_info}\033[0m  {role_color}[{req.role}]\033[0m  \"{preview}\"")

    return {"id": entry_id, "chunks": num_chunks, "message": f"Added entry {entry_id}"}


@app.get("/get/{entry_id}")
def get_entry(entry_id: int, max_tokens: int = 500):
    conn = sqlite3.connect(str(DB_PATH))
    row = conn.execute("SELECT role, content, parent_id FROM entries WHERE id = ?", (entry_id,)).fetchone()
    if not row:
        conn.close()
        return {"error": f"Entry {entry_id} not found"}
    role, content, parent_id = row
    # If this is a chunk, return the parent instead
    if parent_id:
        row = conn.execute("SELECT role, content FROM entries WHERE id = ?", (parent_id,)).fetchone()
        if row:
            role, content = row
            entry_id = parent_id
    conn.close()
    words = content.split()
    if len(words) > max_tokens:
        content = " ".join(words[:max_tokens]) + "..."
    return {"id": entry_id, "role": role, "content": content}


@app.get("/health")
def health():
    return {"status": "ok", "model": MODEL_NAME}


if __name__ == "__main__":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", line_buffering=True)
    # Suppress Windows asyncio ConnectionResetError noise
    logging.getLogger("asyncio").setLevel(logging.CRITICAL)
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=PORT, log_level="warning")
