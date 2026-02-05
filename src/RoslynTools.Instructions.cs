using System.Text.Json;

namespace RoslynMcpServer;

/// <summary>
/// MCP tools for serving development instructions and templates.
/// Issue: #15
/// </summary>
public static partial class RoslynTools
{
    private static readonly string[] RequiredTopic = ["topic"];

    /// <summary>
    /// Returns specific development instructions on-demand.
    /// </summary>
    private static void RegisterGetInstructionsTool(McpServer server)
    {
        var availableTopics = Instructions.Topics.Available;
        var topicDesc = $"Topic name. Available: {string.Join(", ", availableTopics)}";

        server.RegisterTool(
            "GetInstructions",
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
                            description = topicDesc,
                            @enum = availableTopics
                        }
                    },
                    required = RequiredTopic
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var topicName = args?["topic"]?.GetValue<string>()?.ToLowerInvariant() ?? "tools";
                var instructions = Instructions.Topics.Get(topicName);

                if (instructions == null)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = $"Error: Unknown topic '{topicName}'. Available: {string.Join(", ", Instructions.Topics.Available)}" }
                        },
                        isError = false
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
