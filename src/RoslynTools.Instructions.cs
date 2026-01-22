using System.Text.Json;

namespace RoslynMcpServer;

/// <summary>
/// MCP tools for serving development instructions and templates.
/// Issue: #15
/// </summary>
public static partial class RoslynTools
{
    /// <summary>
    /// Returns a CLAUDE.md template for users to copy to their project.
    /// </summary>
    private static void RegisterGetTemplateTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_get_template",
            new ToolDefinition
            {
                Description = "Returns a CLAUDE.md template for C# development. Users copy this to their project. Templates include instructions to call roslyn_get_instructions for detailed guidance.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        template = new
                        {
                            type = "string",
                            description = "Template type: 'minimal' (basic MCP pointers), 'standard' (code + git), 'tdd' (test-driven), 'team' (full workflow with issues)",
                            @enum = new[] { "minimal", "standard", "tdd", "team" }
                        }
                    },
                    required = new[] { "template" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var templateName = args?["template"]?.GetValue<string>()?.ToLowerInvariant() ?? "standard";
                var template = Instructions.Templates.Get(templateName);

                if (template == null)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = $"Error: Unknown template '{templateName}'. Available: {string.Join(", ", Instructions.Templates.Available)}" }
                        },
                        isError = true
                    };
                }

                var result = new
                {
                    template = templateName,
                    availableTemplates = Instructions.Templates.Available,
                    content = template,
                    usage = "Copy the content above to a CLAUDE.md file in your project root."
                };

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) }
                    }
                };
            });
    }

    /// <summary>
    /// Returns specific development instructions on-demand.
    /// </summary>
    private static void RegisterGetInstructionsTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_get_instructions",
            new ToolDefinition
            {
                Description = "Returns specific development instructions for C# projects. Call this when the project's CLAUDE.md directs you to get instructions for a topic.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        topic = new
                        {
                            type = "string",
                            description = "Topic: 'code' (C# best practices), 'git' (workflow, branches, commits), 'plan' (session planning, issue tracking), 'tdd' (test-driven development), 'pre-pr' (checklist before PR), 'tools' (Roslyn tool preferences)",
                            @enum = new[] { "code", "git", "plan", "tdd", "pre-pr", "tools" }
                        },
                        solutionPath = new
                        {
                            type = "string",
                            description = "Optional: Absolute path to the .sln or .slnx solution file. Required for 'tools' topic to generate per-solution token."
                        }
                    },
                    required = new[] { "topic" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var topicName = args?["topic"]?.GetValue<string>()?.ToLowerInvariant() ?? "code";
                var solutionPath = args?["solutionPath"]?.GetValue<string>();
                var instructions = Instructions.Topics.Get(topicName);

                if (instructions == null)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = $"Error: Unknown topic '{topicName}'. Available: {string.Join(", ", Instructions.Topics.Available)}" }
                        },
                        isError = true
                    };
                }

                // For git, plan, and tools topics, generate a token for hook validation
                string? tokenInfo = null;
                if (topicName == "git")
                {
                    var token = HookTokenService.Instance.GenerateToken("git");
                    WriteToken("roslyn-git-token", token);
                    tokenInfo = $"\n\n---\n**Hook Token Generated:** Valid for 5 minutes. Token written to `~/.claude/roslyn-git-token`";
                }
                else if (topicName == "plan")
                {
                    var token = HookTokenService.Instance.GenerateToken("plan");
                    WriteToken("roslyn-plan-token", token);
                    tokenInfo = $"\n\n---\n**Hook Token Generated:** Valid for 10 minutes. Token written to `~/.claude/roslyn-plan-token`";
                }
                else if (topicName == "tools")
                {
                    var token = HookTokenService.Instance.GenerateToken("tools");
                    var tokenFileName = GetToolsTokenFileName(solutionPath);
                    WriteToken(tokenFileName, token);
                    var solutionInfo = string.IsNullOrEmpty(solutionPath) ? " (global)" : $" for {Path.GetFileName(solutionPath)}";
                    tokenInfo = $"\n\n---\n**Hook Token Generated:** Valid for 1 minute{solutionInfo}. Token written to `~/.claude/{tokenFileName}`\nThis token allows Edit/Write operations on .cs files without suggestions.";
                }

                // Return just the instructions text directly for easy consumption
                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = instructions + (tokenInfo ?? "") }
                    }
                };
            });
    }

    private static string GetToolsTokenFileName(string? solutionPath)
    {
        if (string.IsNullOrEmpty(solutionPath))
        {
            return "roslyn-tools-token-global";
        }

        // Use first 8 chars of MD5 hash (same algorithm as hooks)
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hashBytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(solutionPath.ToLowerInvariant()));
        var hashString = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();
        return $"roslyn-tools-token-{hashString}";
    }
}
