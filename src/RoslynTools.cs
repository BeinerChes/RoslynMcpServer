using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoslynMcpServer;

/// <summary>
/// Registers all Roslyn-related MCP tools.
/// </summary>
public static class RoslynTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Registers all tools with the MCP server.
    /// </summary>
    public static void RegisterAll(McpServer server)
    {
        RegisterEchoTool(server);
        RegisterGetServerInfoTool(server);
    }

    /// <summary>
    /// A simple echo tool for testing the MCP connection.
    /// </summary>
    private static void RegisterEchoTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_echo",
            new ToolDefinition
            {
                Description = "A simple echo tool for testing. Returns the message you send.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        message = new
                        {
                            type = "string",
                            description = "The message to echo back"
                        }
                    },
                    required = new[] { "message" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var message = args?["message"]?.GetValue<string>() ?? "No message provided";

                var result = new
                {
                    echoedMessage = message,
                    timestamp = DateTime.UtcNow.ToString("O"),
                    serverName = "roslyn-mcp-server"
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
    /// Returns information about the MCP server.
    /// </summary>
    private static void RegisterGetServerInfoTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_get_server_info",
            new ToolDefinition
            {
                Description = "Returns information about the Roslyn MCP server, including version and available capabilities.",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var info = new
                {
                    name = "roslyn-mcp-server",
                    version = "0.1.0",
                    description = "MCP server for C# solution analysis using Roslyn",
                    runtime = Environment.Version.ToString(),
                    os = Environment.OSVersion.ToString(),
                    capabilities = new[]
                    {
                        "Solution analysis (coming soon)",
                        "Symbol finding (coming soon)",
                        "Reference tracking (coming soon)",
                        "Build order analysis (coming soon)"
                    }
                };

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(info, JsonOptions) }
                    }
                };
            });
    }
}
