# Plan: Knowledge Base with Semantic Search (#53)

## Problem Statement
Add a knowledge base system that stores learnings, gotchas, and insights about code symbols. Uses semantic search (embeddings) to find relevant knowledge automatically and integrates into existing Roslyn tools.

## Architecture Decisions
- **Default embedding**: SmartComponents.LocalEmbeddings (zero setup)
- **Three-layer search**: Symbol links → FTS5 → Vector search
- **Knowledge flows TO Claude**: Embed in existing tool responses, not separate queries

## Implementation Tasks

### Phase 1: Core Infrastructure ✅
- [x] Add SmartComponents.LocalEmbeddings NuGet package
- [x] Create `KnowledgeDatabase.cs` with SQLite schema
- [x] Create `IEmbeddingProvider` interface
- [x] Implement `SmartComponentsEmbeddingProvider`

### Phase 2: Basic Tools ✅
- [x] Add `roslyn_knowledge_add` tool
- [x] Add `roslyn_knowledge_search` tool
- [x] Add `roslyn_knowledge_list` tool (view all entries)
- [x] Add `roslyn_knowledge_delete` tool
- [x] Add `roslyn_knowledge_for_symbol` tool

### Phase 3: Integration
- [ ] Integrate into `roslyn_get_method_body`
- [ ] Integrate into `roslyn_get_type_members`
- [ ] Add knowledge flags to `roslyn_find_symbol`

### Phase 4: Advanced Features
- [ ] Add `roslyn_knowledge_context` for task-based retrieval
- [ ] Add `roslyn_knowledge_suggest` for pre-PR insights
- [ ] Add optional Ollama provider

## Completed Fixes

### Phase 1 & 2 Implementation
- Added `SmartComponents.LocalEmbeddings` NuGet package to Graph project
- Created `IEmbeddingProvider.cs` - interface for embedding providers
- Created `SmartComponentsEmbeddingProvider.cs` - default provider using all-MiniLM-L6-v2
- Created `KnowledgeDatabase.cs` - SQLite schema with FTS5 full-text search
- Created `KnowledgeDatabase.Crud.cs` - CRUD operations
- Created `KnowledgeDatabase.Search.cs` - Three-layer search (symbol, FTS5, vector)
- Created `Models.Knowledge.cs` - Data models and categories
- Created `RoslynTools.Knowledge.cs` - Five MCP tools

### New Tools Added
| Tool | Purpose |
|------|---------|
| `roslyn_knowledge_add` | Add knowledge entries with symbol links and tags |
| `roslyn_knowledge_search` | Semantic search using all three layers |
| `roslyn_knowledge_list` | List entries with category/tag filtering |
| `roslyn_knowledge_delete` | Delete entries by ID |
| `roslyn_knowledge_for_symbol` | Get all knowledge linked to a symbol |

## Test Results
- All 74 tests pass
- Build succeeds with no errors or warnings

## Current Status
- Phase 1 & 2 complete
- Ready for real-world testing
- Phase 3 (tool integration) pending

## Next Steps
1. Real-world test the knowledge tools on this solution
2. Implement Phase 3: Integrate knowledge into existing tools
3. Update README.md with new tools

## Workflow Reminder (MANDATORY)
After each fix:
1. Run `roslyn_get_diagnostics` to check for errors
2. **MANDATORY: Re-read and follow CLAUDE.md**
3. **Update this plan**
4. Keep working until issue is resolved
