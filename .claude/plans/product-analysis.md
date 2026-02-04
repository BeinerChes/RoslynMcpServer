# Product Problem Analysis

## Goal: Prioritize features based on real problem validation

---

## 1. ROSLYN MCP SERVER

### The Claimed Problem
"Claude's text search (grep) finds false positives and misses semantic meaning in C# code"

### Evidence FOR (This is a real problem)
- Benchmark: grep ".Save(" returned 155 matches vs 18 actual callers
- Large classes spread across partial files (155 members, 22 files) - grep can't understand structure
- Symbol renames require understanding scope - text replace breaks things
- Dead code detection impossible without call graph

### Evidence AGAINST (Maybe not a problem)
- Benchmark was self-conducted (potential bias)
- Only tested on ONE solution (Atlas3.sln)
- 7 of 11 steps were "roughly equivalent" per README
- Cost was same (~$7-10) - not a cost savings story
- Most devs use IDE refactoring anyway, not Claude for renames

### Critical Questions
- [ ] How often do users actually need solution-wide semantic operations?
- [ ] Do C# devs even use Claude Code, or prefer Rider/VS?
- [ ] What's the activation rate for semantic tools vs simple edits?

### Verdict: **MEDIUM-HIGH VALUE** for specific use cases
- Clear advantage for: callers, impact, dead code, large class navigation
- No advantage for: simple edits, known files, basic search
- **Risk**: Niche audience (C# devs using Claude Code)

---

## 2. HOOKS (Workflow Enforcement)

### The Claimed Problem
"Claude forgets to follow workflows, uses wrong tools for C# files"

### What Hooks Do
- `suggest-roslyn-for-csharp.py` - Nudge to use Roslyn instead of grep
- `suggest-roslyn-for-read.py` - Nudge to use get_method_body instead of Read
- `enforce-git-instructions.py` - Block git commands until instructions read
- `enforce-plan-instructions.py` - Block planning until instructions read

### Evidence FOR (This is a real problem)
- Claude DOES forget context in long sessions
- Claude DOES default to grep for search
- Standardizing workflow reduces errors

### Evidence AGAINST (Maybe this creates problems)
- Adds friction to every operation
- Users who know what they're doing get blocked
- "Enforcement" feels paternalistic
- If Roslyn tools are better, Claude should choose them naturally

### Critical Questions
- [ ] Do users disable hooks? What's the opt-out rate?
- [ ] Are hooks solving Claude's problem or user education problem?
- [ ] Should this be "suggest" only, never "enforce"?

### Verdict: **QUESTIONABLE VALUE**
- Suggests hooks might work (nudges)
- Enforce hooks feel like friction
- **Risk**: Power users hate guardrails

---

## 3. CALL GRAPH / DEAD CODE

### The Claimed Problem
"Can't trace impact of changes or find unused code"

### Evidence FOR (This is a real problem)
- Large codebases accumulate dead code
- Refactoring without impact analysis is risky
- IDEs have this, but Claude doesn't

### Evidence AGAINST (Maybe not a problem)
- Graph analysis found "34 dead methods" - but some are intentional (API endpoints, interface implementations)
- False positives in dead code detection erode trust
- Most dead code removal is low priority in real projects
- Impact analysis is nice-to-have, not blocking

### Critical Questions
- [ ] What's the false positive rate for dead code?
- [ ] Do users actually act on dead code findings?
- [ ] Is impact analysis used before edits, or just for reports?

### Verdict: **LOW-MEDIUM VALUE**
- Nice to have, rarely critical
- Dead code: high noise, low urgency
- Impact analysis: useful but underutilized
- **Risk**: Feature that demos well but isn't used

---

## 4. KNOWLEDGE DATABASE

### The Claimed Problem
"Context lost between sessions, learnings forgotten"

### Evidence FOR (This is a real problem)
- Claude Code sessions DO lose context
- Same mistakes get repeated across sessions
- Onboarding to a codebase is slow every time

### Evidence AGAINST (Maybe not solving it well)
- Knowledge DB has 7 entries after development - is anyone using it?
- Requires manual "add knowledge" calls
- Semantic search quality unknown
- No evidence users actually read knowledge before starting

### Critical Questions
- [ ] How many knowledge entries in real user projects?
- [ ] What's the retrieval hit rate? (User found useful knowledge)
- [ ] Is automatic knowledge capture better than manual?

### Verdict: **HIGH POTENTIAL, LOW CURRENT VALUE**
- The problem is real (context loss)
- Current solution requires too much manual effort
- **Risk**: Right problem, wrong implementation

---

## PRIORITIZATION RECOMMENDATION

| Feature | Problem Real? | Solution Works? | Priority |
|---------|--------------|-----------------|----------|
| Roslyn MCP (semantic) | Yes | Yes (for specific ops) | **HIGH** - core value |
| Knowledge DB | Yes | Weak (manual effort) | **HIGH** - needs improvement |
| Call Graph | Somewhat | Yes (but noisy) | **MEDIUM** - reduce false positives |
| Hooks (suggest) | Yes | Yes | **LOW** - keep but simplify |
| Hooks (enforce) | Debatable | Creates friction | **CUT** or make optional |

---

## COMPETITIVE CONTEXT (To Research)

- **GitHub Copilot**: No semantic C# analysis, pure text
- **Cursor**: Has some code intelligence, unclear depth
- **JetBrains AI**: Built into Rider, has full Roslyn access
- **Continue.dev**: Open source, plugin-based

Key question: Is "Roslyn for Claude Code" differentiated, or will Claude get native semantic understanding?

---

## USER VALIDATION STATUS

**Current state:** Single user (developer), no external validation

**Implication:** All "problems" are hypothesized, not proven

---

## REVISED PRIORITIZATION

Given no external users yet, the priority is:

### 1. REDUCE FRICTION (immediate)
- Remove or disable "enforce" hooks by default
- Make setup simpler (fewer files, less config)
- Goal: First-time users should succeed in 5 minutes

### 2. PROVE CORE VALUE (next)
- Focus messaging on 3 "impossible with grep" operations:
  - Find all callers (not text matches)
  - Rename symbol across solution
  - Find dead code
- Create video demos showing native vs MCP side-by-side
- Goal: Clear "aha moment" in 60 seconds

### 3. SIMPLIFY KNOWLEDGE (later)
- Auto-capture learnings instead of manual
- Or: Cut it entirely until core is validated
- Goal: Don't distract from core value prop

### 4. FIND 5 BETA USERS (critical)
- Post in r/dotnet, r/csharp, Hacker News
- Look for: devs using Claude Code on C# projects
- Ask: "What's frustrating about Claude and C#?"
- Goal: Real pain points from real users

---

## WHAT TO CUT?

| Feature | Keep/Cut | Reasoning |
|---------|----------|-----------|
| Core Roslyn tools (find, callers, rename) | **KEEP** | Core value |
| Enforce hooks | **CUT** | Creates friction |
| Suggest hooks | **KEEP** (optional) | Low friction |
| Call graph | **KEEP** | Enables dead code, impact |
| Knowledge DB | **PAUSE** | Nice-to-have, adds complexity |
| 3D Visualization | **CUT** | Cool demo, zero utility |
| Watson blog skill | **CUT** | Fun but distracting |

---

## NEXT ACTIONS

1. [ ] Remove enforce hooks from default setup
2. [ ] Simplify install to: download, run, done
3. [ ] Create 60-second demo video (grep vs GetCallers)
4. [ ] Post to r/dotnet asking about Claude + C# pain points
5. [ ] Find 5 beta users, get real feedback

---

# INSTALLATION REDESIGN PLAN

## Design Decisions
- **Model**: Per-project install (exe lives in project)
- **Hooks/Skills**: Included but disabled by default (opt-in)
- **Platform**: Windows x64 only (for now)

## New User Experience

### Step 1: Download
```
Download roslyn-mcp-win-x64.zip from GitHub Releases
```

### Step 2: Extract to Project
```
Unzip to: YourSolution/.roslyn-mcp/
```

Result:
```
YourSolution/
├── .roslyn-mcp/
│   ├── roslyn-mcp.exe      # Single self-contained exe
│   ├── hooks/              # Available but not active
│   │   ├── suggest-roslyn-for-csharp.py
│   │   └── suggest-roslyn-for-read.py
│   └── skills/             # Available but not active
│       ├── architect/
│       └── blog/
└── .mcp.json               # Auto-created or user creates
```

### Step 3: Create .mcp.json (minimal)
```json
{
  "mcpServers": {
    "roslyn": {
      "type": "stdio",
      "command": ".roslyn-mcp/roslyn-mcp.exe",
      "args": []
    }
  }
}
```

### Step 4: Done
```bash
claude
/mcp  # Shows "roslyn" connected
```

## Optional: Enable Hooks
User manually creates `.claude/settings.json`:
```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Read",
        "hooks": [{"type": "command", "command": "python .roslyn-mcp/hooks/suggest-roslyn-for-read.py"}]
      }
    ]
  }
}
```

Or run: `roslyn-mcp.exe --enable-hooks` (creates settings.json)

## Optional: Enable Skills
Copy skill folders to `.claude/skills/`:
```powershell
Copy-Item -Recurse .roslyn-mcp/skills/* .claude/skills/
```

Or run: `roslyn-mcp.exe --enable-skills`

## Implementation Tasks

### 1. Build Changes
- [ ] Publish as single-file self-contained exe
- [ ] Target: `win-x64`, `PublishSingleFile=true`, `SelfContained=true`
- [ ] Output: `roslyn-mcp.exe` (~50-80MB with runtime)

### 2. CLI Flags
- [ ] `roslyn-mcp.exe` - Run as MCP server (default)
- [ ] `roslyn-mcp.exe --init` - Create .mcp.json in current dir
- [ ] `roslyn-mcp.exe --enable-hooks` - Create .claude/settings.json with suggest hooks
- [ ] `roslyn-mcp.exe --enable-skills` - Copy skills to .claude/skills/
- [ ] `roslyn-mcp.exe --version` - Show version

### 3. Release Package
- [ ] `roslyn-mcp-win-x64.zip` containing:
  - `roslyn-mcp.exe`
  - `hooks/` folder (optional files)
  - `skills/` folder (optional files)
  - `README.txt` (quick start)

### 4. Update Documentation
- [ ] Simplify README Quick Start
- [ ] Remove setup.ps1 references
- [ ] Document opt-in hooks/skills

## Comparison: Old vs New

| Aspect | Old Setup | New Setup |
|--------|-----------|-----------|
| Prerequisites | .NET SDK, Python | Python (for hooks only) |
| Install | Run setup.ps1 | Extract zip |
| Files created | 10+ files | 1 folder + .mcp.json |
| Hooks | Forced on | Opt-in |
| Skills | Forced on | Opt-in |
| Time to first use | ~5 minutes | ~1 minute |

## Decisions Made

| Question | Decision |
|----------|----------|
| Exe size | ~50-80MB acceptable (self-contained) |
| CLAUDE.md | Yes, create minimal one via --init |
| Database location | `.roslyn-mcp/` folder (with exe) |

## Final Folder Structure

```
YourSolution/
├── .roslyn-mcp/               # Everything in one folder
│   ├── roslyn-mcp.exe         # Self-contained exe
│   ├── graph.db               # Call graph database
│   ├── knowledge.db           # Knowledge base
│   ├── hooks/                 # Available (opt-in)
│   └── skills/                # Available (opt-in)
├── .mcp.json                  # MCP config (created by --init)
├── CLAUDE.md                  # Minimal instructions (created by --init)
└── .claude/                   # Only if hooks/skills enabled
    ├── settings.json          # Hook config
    └── skills/                # Active skills
```

**.gitignore recommendation:**
```
.roslyn-mcp/graph.db
.roslyn-mcp/knowledge.db
```
