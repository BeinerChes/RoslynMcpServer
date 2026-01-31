using System.Text.Json;

namespace RoslynMcpServer;

/// <summary>
/// MCP tools for serving development instructions and templates.
/// Issue: #15
/// </summary>
public static partial class RoslynTools
{
    private static readonly string[] definitionArray2 = new[] { "minimal", "standard", "tdd", "team" };
    private static readonly string[] definitionArray3 = new[] { "template" };

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
                            @enum = definitionArray2
                        }
                    },
                    required = definitionArray3
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

    private static readonly string[] definitionArray4 = new[] { "code", "git", "plan", "tdd", "pre-pr", "tools" };
    private static readonly string[] definitionArray5 = new[] { "topic" };

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
                            @enum = definitionArray4
                        }
                    },
                    required = definitionArray5
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

                // For git, plan, and tools topics, generate a token for hook validation
                string? tokenInfo = null;

                if (topicName == "git" || topicName == "plan" || topicName == "tools")
                {
                    var token = HookTokenService.Instance.GenerateToken(topicName);
                    var tokenFileName = GetTokenFileName(topicName);
                    WriteToken(tokenFileName, token);

                    var extraInfo = topicName == "tools" ? "\nThis token allows Edit/Write operations on .cs files without suggestions." : "";
                    tokenInfo = $"\n\n---\n**Hook Token Generated:** Valid for 1 minute. Token written to `.roslyn-mcp/{tokenFileName}`{extraInfo}";
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

    private static string GetTokenFileName(string topic)
    {
        // Simple token file name - stored in .roslyn-mcp/ folder (per-solution)
        return $"{topic}-token";
    }

    /// <summary>
    /// Auto-detect solution file from exe directory.
    /// The exe is installed in .roslyn-mcp/, so the solution is in the parent directory.
    /// </summary>
    private static string? DetectSolutionPath()
    {
        var exeDir = AppContext.BaseDirectory;
        var parentDir = Path.GetDirectoryName(exeDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
            return null;

        // Look for .slnx first (newer format), then .sln
        foreach (var ext in new[] { "*.slnx", "*.sln" })
        {
            var files = Directory.GetFiles(parentDir, ext);
            if (files.Length > 0)
                return files[0];
        }

        return null;
    }
}
