# GetInstructions

## Description

GetInstructions is a documentation delivery tool that provides development instructions for C# projects using the Roslyn MCP server. Unlike traditional documentation that requires navigating files or searching through README files, GetInstructions delivers topic-specific instructions directly in response to tool calls.

The tool serves curated instruction topics stored as markdown files in `Instructions/Topics/`. Each topic covers a specific aspect of development workflow: tool usage, git conventions, or session management. The tool is designed to be called at session start or whenever developers need guidance on established patterns.

The tool handles:
- Dynamic discovery of available topics from the filesystem
- Validated topic lookup with helpful error messages
- Hook token generation for git, plan, and tools topics (enables bypass of .cs file restrictions)
- Direct markdown content delivery without additional file navigation

## Comparison with Native Claude Code Tools

### vs Read
- **Read** requires knowing the exact file path (`Instructions/Topics/tools.md`)
- **GetInstructions** uses semantic topic names (`topic: "tools"`)
- **Read** returns line-numbered content (adds parsing overhead)
- **GetInstructions** returns clean markdown ready for consumption
- **Read** doesn't validate or suggest alternatives for incorrect paths
- **GetInstructions** provides enum validation and helpful error messages listing available topics
- **Read** doesn't trigger related functionality
- **GetInstructions** generates hook tokens for topics that need them (git, plan, tools)

### vs Glob + Read
For discovering what topics are available:
- **Glob pattern** (`Instructions/Topics/*.md`) + **Read** requires 2+ operations
- **GetInstructions** with invalid topic returns available topics in the error message (1 operation)

### When to use GetInstructions
- At session start to load development conventions (CLAUDE.md directs: "Call GetInstructions for git, plan, tools")
- When you need git workflow instructions before committing
- When you need a reminder of available Roslyn tools vs native tool mappings
- When you need session/memory management guidance
- When you need a hook bypass token for .cs file operations

## Real-World Example

### Scenario
Developer starting work on RoslynMcpServer needs to understand:
1. Available Roslyn tools and when to use them vs native tools
2. Git commit workflow and branch naming conventions
3. Session management and when to use TaskCreate vs GitHub issues

**Test project:** RoslynMcpServer itself (actual testing, not estimates)

### Approach 1: Using Native Tools

**Step 1:** Discover available topics
```
Glob(pattern: "Instructions/Topics/*.md")
```
Returns: `["Instructions/Topics/git.md", "Instructions/Topics/plan.md", "Instructions/Topics/tools.md"]`
~150 tokens (file paths)

**Step 2:** Read all three topics
```
Read(file_path: "D:\repos\RoslynMcpServer\Instructions\Topics\tools.md")
Read(file_path: "D:\repos\RoslynMcpServer\Instructions\Topics\git.md")
Read(file_path: "D:\repos\RoslynMcpServer\Instructions\Topics\plan.md")
```
Returns: Line-numbered content for all three files
- tools.md: ~1,200 tokens (90 lines)
- git.md: ~650 tokens (48 lines)
- plan.md: ~800 tokens (59 lines)

**Step 3:** Manually track that you need a hook bypass token (if editing .cs files)
No automated support - developer must remember to get token

**Total: ~2,800 tokens, 4 operations (Glob + 3× Read)**

### Approach 2: Using GetInstructions (Roslyn)

**Step 1:** Request instructions by topic name
```
GetInstructions(topic: "tools")
GetInstructions(topic: "git")
GetInstructions(topic: "plan")
```
Returns: Clean markdown content plus hook token notification
- tools.md: ~1,200 tokens + ~50 tokens token notice
- git.md: ~650 tokens + ~50 tokens token notice
- plan.md: ~800 tokens + ~50 tokens token notice

Hook tokens automatically generated and written to `.roslyn-mcp/` directory.

**Total: ~2,800 tokens, 3 operations**

### Comparison Summary

| Aspect | Native (Glob + Read) | GetInstructions (Roslyn) |
|--------|---------------------|--------------------------|
| **Token usage** | ~2,800 tokens | ~2,800 tokens |
| **Operations** | 4 (1 Glob + 3 Read) | 3 (3 GetInstructions) |
| **Path knowledge required** | Yes (full path to Instructions/Topics/) | No (topic name only) |
| **Content format** | Line-numbered | Clean markdown |
| **Topic discovery** | Manual Glob | Enum validation with error listing |
| **Hook token generation** | Manual | Automatic for git/plan/tools |
| **Error handling** | File not found | Helpful error with available topics |

**Key advantages:**
- **Semantic interface**: `topic: "tools"` vs `file_path: "D:\...\Topics\tools.md"`
- **Automatic hook token generation**: No need to remember separate token acquisition
- **Better error messages**: Lists available topics instead of generic file not found
- **Cleaner consumption**: No line numbers to strip

**When Read is better:**
- Reading non-instruction files (configs, data, etc.)
- When you need line numbers for navigation
- When reading files outside the instruction topic system

## How It Works

### Topic Discovery

The tool scans `Instructions/Topics/` directory at startup to build the list of available topics:

```csharp
var topicFiles = Directory.GetFiles(TopicsPath, "*.md");
return topicFiles.Select(f => Path.GetFileNameWithoutExtension(f)).ToArray();
```

This creates an enum constraint in the tool schema, providing IDE autocomplete and validation.

### Topic Lookup

When called, the tool:
1. Normalizes the topic name to lowercase
2. Constructs the file path: `Instructions/Topics/{topic}.md`
3. Reads the file content
4. Returns error if topic doesn't exist (lists all available topics)

### Hook Token Generation

For git, plan, and tools topics, the tool generates a time-limited (1 minute) token that allows bypassing .cs file hooks:

```csharp
if (topicName == "git" || topicName == "plan" || topicName == "tools")
{
    var token = HookTokenService.Instance.GenerateToken(topicName);
    WriteToken($"{topicName}-token", token);
}
```

The token is written to `.roslyn-mcp/{topic}-token` and can be read by hooks to allow Edit/Write operations on .cs files without Roslyn tool suggestions.

### Invocation

**Basic usage:**
```
GetInstructions(topic: "tools")
```

**Available topics:**
- `tools` - Roslyn tool mappings, auto-generation workflow, gotchas
- `git` - Git workflow, commit conventions, branch naming
- `plan` - Session management, TaskCreate, knowledge base, plan files

**Invalid topic:**
```
GetInstructions(topic: "invalid")
```
Returns: `"Error: Unknown topic 'invalid'. Available: git, plan, tools"`

### Response Format

**Success:**
```json
{
  "content": [
    {
      "type": "text",
      "text": "# Roslyn MCP Tools\n\n...\n\n---\n**Hook Token Generated:** Valid for 1 minute. Token written to `.roslyn-mcp/tools-token`\nThis token allows Edit/Write operations on .cs files without suggestions."
    }
  ]
}
```

**Error:**
```json
{
  "content": [
    {
      "type": "text",
      "text": "Error: Unknown topic 'xyz'. Available: git, plan, tools"
    }
  ],
  "isError": false
}
```

Note: Even errors return `isError: false` because the tool successfully executed - it just couldn't find the requested topic.

### Common Workflows

**1. Session Start (as directed by CLAUDE.md)**
```
GetInstructions(topic: "plan")   # Memory/session management guidance
GetInstructions(topic: "tools")  # Tool mappings + bypass token
GetInstructions(topic: "git")    # Git workflow reference
```

**2. Before Committing Code**
```
GetInstructions(topic: "git")    # Refresh commit conventions
# Token allows git commit workflow without hook interference
```

**3. When Unsure Which Tool to Use**
```
GetInstructions(topic: "tools")  # Get Roslyn vs native tool mappings
# Token allows experimenting with Edit/Write on .cs files
```

**4. When Planning Complex Work**
```
GetInstructions(topic: "plan")   # When to use TaskCreate vs issues vs plan files
```

### Adding New Topics

To add a new instruction topic:

1. Create `Instructions/Topics/{topic-name}.md`
2. The tool auto-discovers it on next server restart
3. No code changes needed - the tool scans the directory dynamically

Topic files should be:
- Concise (developers read these frequently)
- Actionable (commands, not theory)
- Reference material (not tutorials)

### When Read is Better

Use Read instead of GetInstructions for:
- Reading non-instruction files (README.md, CHANGELOG.md, etc.)
- When you need line numbers for navigation or editing
- When reading generated content or logs
- When the file path is already known and validated
- For files outside the `Instructions/Topics/` system

GetInstructions is specifically designed for the curated instruction topic system used by Roslyn MCP server projects.
