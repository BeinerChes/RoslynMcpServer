# Session & Memory Management

## Memory Tools

| Tool | Persists? | Use For |
|------|-----------|---------|
| TaskCreate | No (session only) | Multi-step tracking |
| GitHub Issue | Yes | Work tracking |
| Plan file | Yes | Complex/debugging work |
| Knowledge base | Yes | Lessons, gotchas |

## Decision Tree

```
3+ steps? → TaskCreate
Committing? → Need issue number
Tried same thing twice? → Write it down
Learned something? → roslyn_knowledge_add() NOW
Complex or multi-session? → Plan file
```

## Triggers: STOP and Write

**Tried same fix twice** → You're looping. Write what you tried.

**Lost track of goal** → Re-read issue, plan file, recent commits.

**Learned something non-obvious** → Add to knowledge base immediately:
```
roslyn_knowledge_add(category: "lesson", title: "...", content: "...")
```

## Plan File (when needed)

Location: `.claude/plans/issue-<number>.md`

```markdown
# Issue #<number>: <Title>

## Goal
<Measurable success>

## Progress
- [x] Done
- [ ] Todo

## Tried
1. X → Failed: Y

## Blocked
<Current blocker>
```

## Session Start

1. "What issue are we working on?"
2. Check `.claude/plans/` for existing plan
3. `roslyn_knowledge_search("<task>")` - someone may have documented this
