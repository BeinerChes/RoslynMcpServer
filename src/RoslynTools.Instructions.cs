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
                            description = "Topic: 'code' (C# best practices), 'git' (workflow, branches, commits), 'tdd' (test-driven development), 'pre-pr' (checklist before PR), 'tools' (Roslyn tool preferences)",
                            @enum = new[] { "code", "git", "tdd", "pre-pr", "tools" }
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

                // For git topic, generate a token for hook validation
                string? tokenInfo = null;
                if (topicName == "git")
                {
                    var token = HookTokenService.Instance.GenerateToken("git");
                    // Write token to file for hook to read
                    WriteGitToken(token);
                    tokenInfo = $"\n\n---\n**Hook Token Generated:** Valid for 5 minutes. Token written to `~/.claude/roslyn-git-token`";
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

    private static void WriteGitToken(string token)
    {
        try
        {
            var claudeDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude");
            Directory.CreateDirectory(claudeDir);

            var tokenFile = Path.Combine(claudeDir, "roslyn-git-token");
            File.WriteAllText(tokenFile, token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not write git token file: {ex.Message}");
        }
    }
}
