# Session & Memory Management

## Memory Tools

| Tool | Persists? | Use For |
|------|-----------|---------|
| TaskCreate | No (session only) | Multi-step tracking |
| GitHub Issue | Yes | Work tracking |
| Plan file | Yes | Complex/debugging work |

## Decision Tree

```
3+ steps? → TaskCreate
Committing? → Need issue number
Tried same thing twice? → Write it down
Complex or multi-session? → Plan file
```

## Triggers: STOP and Write

**Tried same fix twice** → You're looping. Write what you tried.

**Lost track of goal** → Re-read issue, plan file, recent commits.

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
