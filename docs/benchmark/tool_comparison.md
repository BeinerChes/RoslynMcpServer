# Roslyn MCP Tool Comparison: RoslynMcpServer vs SharpLens

| RoslynMcpServer | SharpLens | Description |
|-----------------|-----------|-------------|
| **NAVIGATION & DISCOVERY** | | |
| `roslyn_find_symbol` | `search_symbols` | Find types, methods, properties by name pattern |
| `roslyn_get_references` | `find_references` | Find all usages of a symbol across solution |
| `roslyn_get_callers` | `find_callers` | Find all call sites of a method |
| `roslyn_get_implementations` | `find_implementations` | Find interface implementations or derived classes |
| `roslyn_get_type_members` | `get_type_members` | List all members of a class (methods, properties, fields) |
| `roslyn_get_method_body` | `get_method_source` | Get full source code of a specific method |
| — | `get_symbol_info` | Get semantic info at cursor position (type, modifiers, docs) |
| — | `go_to_definition` | Jump to symbol definition location |
| — | `get_type_hierarchy` | Full inheritance tree (base classes + derived) |
| — | `get_base_types` | Get all base classes and interfaces |
| — | `get_derived_types` | Get all subclasses |
| — | `semantic_query` | Advanced filter (async, visibility, static, etc.) |
| — | `get_type_members_batch` | Get members of multiple types in one call |
| — | `get_method_signature` | Get detailed method signature info |
| — | `get_method_source_batch` | Get source of multiple methods in one call |
| **ANALYSIS** | | |
| `roslyn_get_diagnostics` | `get_diagnostics` | Get compiler errors and warnings |
| `roslyn_graph_impact` | `analyze_change_impact` | Analyze blast radius if a symbol changes |
| `roslyn_find_dead_code` | `find_unused_code` | Find methods/properties with no callers |
| `roslyn_query_graph` (callees) | `get_outgoing_calls` | What does this method call? |
| — | `analyze_data_flow` | Track variable assignments and reads |
| — | `analyze_control_flow` | Analyze branching and reachability |
| — | `check_type_compatibility` | Check if type A can be assigned to type B |
| — | `validate_code` | Compile check without writing to disk |
| — | `get_complexity_metrics` | Cyclomatic complexity, cognitive complexity, LOC |
| **REFACTORING** | | |
| `roslyn_rename_symbol` | `rename_symbol` | Safe rename across entire solution |
| `roslyn_extract_method` | `extract_method` | Extract code block into new method with data flow |
| `roslyn_remove_unnecessary_usings` | `organize_usings` | Remove unused using directives |
| `roslyn_apply_code_fix` | `apply_code_fix` | Apply Roslyn's suggested fix for a diagnostic |
| — | `organize_usings_batch` | Batch organize usings across multiple files |
| — | `change_signature` | Add, remove, or reorder method parameters |
| — | `extract_interface` | Generate interface from class |
| — | `extract_variable` | Extract expression into a variable |
| — | `inline_variable` | Inline a temporary variable |
| — | `encapsulate_field` | Convert field to property |
| — | `implement_missing_members` | Generate interface member stubs |
| — | `generate_constructor` | Generate constructor from fields/properties |
| — | `get_code_actions_at_position` | Get all available refactorings at cursor |
| — | `apply_code_action_by_title` | Apply any refactoring by its title |
| **CODE GENERATION** | | |
| — | `add_null_checks` | Generate ArgumentNullException guards |
| — | `generate_equality_members` | Generate Equals, GetHashCode, operators |
| **CODE MODIFICATION** | | |
| `roslyn_add_member` | — | Add method/property/field to a type |
| `roslyn_add_type` | — | Create new class/interface/struct/record/enum |
| `roslyn_delete_member` | — | Delete a member from a type |
| `roslyn_update_method` | — | Replace a method's implementation |
| `roslyn_batch_apply_code_fixes` | — | Fix all diagnostics of a specific type at once |
| **CALL GRAPH** | | |
| `roslyn_graph_status` | — | Check if call graph database exists |
| `roslyn_graph_analyze` | — | Build/update persistent call graph database |
| `roslyn_query_graph` | — | Query callers/callees with recursive depth |
| **COMPOUND TOOLS** | | |
| — | `get_type_overview` | Full type info in one call (members + hierarchy + diagnostics) |
| — | `analyze_method` | Method signature + callers + callees + location |
| — | `get_file_overview` | File summary with types and diagnostics |
| — | `get_instantiation_options` | How to create/instantiate a type |
| **INFRASTRUCTURE** | | |
| `roslyn_echo` | `health_check` | Test server connectivity |
| `roslyn_get_server_info` | `health_check` | Server version and capabilities |
| `roslyn_get_projects_in_build_order` | `get_project_structure` | List projects in dependency order |
| — | `load_solution` | Explicitly load a solution file |
| — | `dependency_graph` | Project dependency visualization |
| — | `sync_documents` | Sync in-memory docs after external changes |
| **KNOWLEDGE BASE** | | |
| `roslyn_knowledge_add` | — | Save gotchas, patterns, insights linked to symbols |
| `roslyn_knowledge_search` | — | Semantic search past learnings |
| `roslyn_knowledge_list` | — | List knowledge entries by category/tag |
| `roslyn_knowledge_get` | — | Get full content of a knowledge entry |
| `roslyn_knowledge_delete` | — | Delete a knowledge entry |
| `roslyn_knowledge_for_symbol` | — | Get all knowledge linked to a specific symbol |
| **WORKFLOW & SETUP** | | |
| `roslyn_get_template` | — | Get CLAUDE.md template for projects |
| `roslyn_get_instructions` | — | Get topic-specific development instructions |
| `roslyn_setup_hooks` | — | Set up hooks and CLAUDE.md in a project |

---

## Summary

| Category | RoslynMcpServer | SharpLens |
|----------|-----------------|-----------|
| Navigation & Discovery | 6 | 14 |
| Analysis | 4 | 9 |
| Refactoring | 4 | 14 |
| Code Generation | 0 | 2 |
| **Code Modification** | **5** | **0** |
| **Call Graph DB** | **3** | **0** |
| Compound Tools | 0 | 4 |
| Infrastructure | 3 | 6 |
| **Knowledge Base** | **6** | **0** |
| **Workflow & Setup** | **3** | **0** |
| **TOTAL** | **34** | **49** |

---

## Key Differences

**SharpLens strengths:**
- More analysis tools (data flow, control flow, complexity)
- More refactoring options (change signature, extract interface, etc.)
- Batch operations for efficiency

**RoslynMcpServer strengths:**
- Can actually WRITE code (add/update/delete members and types)
- Persistent call graph database
- Knowledge base for cross-session learning
- Workflow tools for Claude Code integration
