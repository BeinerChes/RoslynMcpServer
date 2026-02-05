# Knowledge Tools

## Overview

The Knowledge tools provide a persistent, searchable knowledge base for storing insights about code patterns, gotchas, debugging tips, and lessons learned during development sessions. Entries are stored in a per-solution SQLite database and can be linked to specific code symbols for automatic retrieval.

Four tools work together:

| Tool | Purpose | Read-only |
|------|---------|-----------|
| **KnowledgeAdd** | Create a new knowledge entry | No |
| **KnowledgeSearch** | Search entries using semantic search | Yes |
| **KnowledgeGet** | Retrieve a single entry by ID with full content | Yes |
| **KnowledgeDelete** | Delete an entry by ID | No |

## KnowledgeAdd

Creates a new knowledge entry in the database.

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `category` | string | Yes | One of: `gotcha`, `pattern`, `architecture`, `debugging`, `performance`, `security`, `testing`, `workaround`, `lesson`, `error-resolution`, `convention`, `instruction` |
| `title` | string | Yes | Short title summarizing the knowledge (1-2 sentences) |
| `content` | string | Yes | Detailed explanation including context and recommendations |
| `symbolLinks` | string[] | No | Qualified symbol names to link to (e.g., `MyNamespace.MyClass.MyMethod`) |
| `tags` | string[] | No | Tags for categorization (e.g., `caching`, `threading`, `ui`) |
| `confidence` | number | No | Confidence level 0.0-1.0. Default: 1.0 |

### Categories

**Code-specific:**
- `gotcha` — Non-obvious pitfalls or traps in code
- `pattern` — Reusable code patterns found in the project
- `architecture` — Architectural decisions and rationale
- `debugging` — Tips for debugging specific areas
- `performance` — Performance insights and optimizations
- `security` — Security considerations
- `testing` — Testing strategies and gotchas
- `workaround` — Workarounds for known issues

**Session learnings:**
- `lesson` — Non-obvious discoveries made during development
- `error-resolution` — Problems encountered and their fixes
- `convention` — Project rules and conventions
- `instruction` — Workflow guidance

### Example

```
KnowledgeAdd(
  category: "gotcha",
  title: "UpdateMethod XML doc overwrites leading trivia",
  content: "AddXmlDocComment must be called AFTER WithLeadingTrivia, not before. Otherwise the XML doc comment replaces any existing leading trivia (whitespace, comments).",
  symbolLinks: ["RoslynMcpServer.RoslynTools.HandleUpdateMethodAsync"],
  tags: ["roslyn", "trivia", "xml-doc"]
)
```

### Response

```json
{
  "success": true,
  "id": 42,
  "message": "Knowledge entry created with ID 42",
  "entry": {
    "Id": 42,
    "Category": "gotcha",
    "Title": "UpdateMethod XML doc overwrites leading trivia",
    "Content": "...",
    "SymbolLinks": ["RoslynMcpServer.RoslynTools.HandleUpdateMethodAsync"],
    "Tags": ["roslyn", "trivia", "xml-doc"],
    "Confidence": 1.0,
    "hasEmbedding": true
  }
}
```

## KnowledgeSearch

Searches the knowledge base using a three-layer ranking system that combines exact symbol matches, full-text search, and vector similarity.

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `query` | string | Yes | Natural language, keywords, or problem description |
| `symbols` | string[] | No | Symbol names for exact match lookup |
| `limit` | integer | No | Max results to return (1-50). Default: 10 |

### Search Layers

Results are ranked by combining three search strategies:

1. **Symbol links** (weight 1.0) — Exact match on linked symbol names. Highest priority.
2. **Full-text search** (weight 0.7) — SQLite FTS5 keyword matching on title and content.
3. **Vector similarity** (weight 0.8) — Cosine similarity on embeddings (requires embedding provider). Finds semantically related entries even without keyword overlap.

Results from all layers are merged, deduplicated by entry ID, and ranked by combined score. The `matchSource` field shows which layers matched (e.g., `symbol+fts`, `fts+vector`, `symbol`).

### Example

```
KnowledgeSearch(
  query: "XML doc comment trivia ordering",
  symbols: ["RoslynMcpServer.RoslynTools.HandleUpdateMethodAsync"]
)
```

### Response

```json
{
  "success": true,
  "query": "XML doc comment trivia ordering",
  "resultCount": 2,
  "results": [
    {
      "Id": 42,
      "Category": "gotcha",
      "Title": "UpdateMethod XML doc overwrites leading trivia",
      "SymbolLinks": ["RoslynMcpServer.RoslynTools.HandleUpdateMethodAsync"],
      "Tags": ["roslyn", "trivia", "xml-doc"],
      "Confidence": 1.0,
      "Score": 1.56,
      "MatchSource": "symbol+fts"
    }
  ]
}
```

Note: Search results return metadata only (no `Content` field). Use **KnowledgeGet** to fetch the full content of a specific entry.

## KnowledgeGet

Retrieves a single knowledge entry by ID with full content. Use after searching to read the complete details.

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | integer | Yes | ID of the knowledge entry to retrieve |

### Example

```
KnowledgeGet(id: 42)
```

### Response

```json
{
  "success": true,
  "entry": {
    "Id": 42,
    "Category": "gotcha",
    "Title": "UpdateMethod XML doc overwrites leading trivia",
    "Content": "AddXmlDocComment must be called AFTER WithLeadingTrivia, not before. Otherwise the XML doc comment replaces any existing leading trivia (whitespace, comments).",
    "SymbolLinks": ["RoslynMcpServer.RoslynTools.HandleUpdateMethodAsync"],
    "Tags": ["roslyn", "trivia", "xml-doc"],
    "Confidence": 1.0,
    "CreatedAt": "2026-02-04T14:30:00Z",
    "UpdatedAt": "2026-02-04T14:30:00Z"
  }
}
```

## KnowledgeDelete

Deletes a knowledge entry by ID. Idempotent — returns an error if the entry doesn't exist but doesn't fail.

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | integer | Yes | ID of the knowledge entry to delete |

### Example

```
KnowledgeDelete(id: 42)
```

### Response

```json
{
  "success": true,
  "message": "Knowledge entry 42 deleted"
}
```

**If not found:**
```json
{
  "error": "Error: Knowledge entry 42 not found"
}
```

## Common Workflows

### 1. Record a bug fix discovery

After fixing a tricky bug, capture the insight:
```
KnowledgeAdd(
  category: "error-resolution",
  title: "LoRA save_py() broken after merge — writes empty ZIP",
  content: "After LoRA weight merge, save_py() writes a 135-byte empty ZIP because...",
  symbolLinks: ["RoslynMcpServer.SharpOps.LoraTrainer.SaveModel"],
  tags: ["lora", "serialization"]
)
```

### 2. Search before working on unfamiliar code

Before modifying a subsystem, check for known gotchas:
```
KnowledgeSearch(query: "finetune training loop memory")
```

### 3. Search + Get full details

```
KnowledgeSearch(query: "tokenizer special tokens")
→ result with Id: 15

KnowledgeGet(id: 15)
→ full content with fix details
```

### 4. Clean up outdated knowledge

```
KnowledgeSearch(query: "old workaround no longer needed")
→ result with Id: 8

KnowledgeDelete(id: 8)
```

## Storage

- **Database:** SQLite, stored at `.roslyn-mcp/knowledge.db` (per-solution)
- **Full-text search:** SQLite FTS5 index on title and content
- **Vector embeddings:** Stored as BLOB, generated by the configured embedding provider (if available)
- **Symbol links:** Stored in a separate table, enabling exact-match lookups by qualified symbol name
