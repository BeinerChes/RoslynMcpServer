using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RoslynMcpServer;

/// <summary>
/// MCP server implementation using JSON-RPC 2.0 over stdio.
/// </summary>
public class McpServer
{
    private readonly Dictionary<string, ToolDefinition> _tools = new();
    private readonly Dictionary<string, Func<JsonObject?, Task<object>>> _toolHandlers = new();

    /// <summary>
    /// Gets the server version from assembly metadata.
    /// </summary>
    public static string Version { get; } = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion?.Split('+')[0] ?? "unknown";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Registers a tool with the MCP server.
    /// </summary>
    public void RegisterTool(string name, ToolDefinition definition, Func<JsonObject?, Task<object>> handler)
    {
        _tools[name] = definition;
        _toolHandlers[name] = handler;
        Console.Error.WriteLine($"Registered tool: {name}");
    }

    /// <summary>
    /// Runs the MCP server, reading JSON-RPC messages from stdin and writing responses to stdout.
    /// </summary>
    public async Task RunAsync()
    {
        Console.Error.WriteLine("MCP Server running. Waiting for messages...");

        using var reader = new StreamReader(Console.OpenStandardInput());

        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var response = await ProcessMessageAsync(line);
                if (response is not null)
                {
                    var json = JsonSerializer.Serialize(response, JsonOptions);
                    Console.WriteLine(json);
                    await Console.Out.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing message: {ex.Message}");
            }
        }
    }

    private async Task<object?> ProcessMessageAsync(string message)
    {
        Console.Error.WriteLine($"Received: {message}");

        var request = JsonSerializer.Deserialize<JsonObject>(message, JsonOptions);
        if (request is null)
            return CreateErrorResponse(null, -32700, "Parse error");

        var id = request["id"];
        var method = request["method"]?.GetValue<string>();
        var @params = request["params"]?.AsObject();

        if (method is null)
            return CreateErrorResponse(id, -32600, "Invalid Request: method is required");

        return method switch
        {
            "initialize" => HandleInitialize(id, @params),
            "initialized" => null, // Notification, no response
            "tools/list" => HandleToolsList(id),
            "tools/call" => await HandleToolCallAsync(id, @params),
            "ping" => CreateSuccessResponse(id, new { }),
            _ => CreateErrorResponse(id, -32601, $"Method not found: {method}")
        };
    }

    private static object HandleInitialize(JsonNode? id, JsonObject? @params)
    {
        Console.Error.WriteLine("Handling initialize request");

        return CreateSuccessResponse(id, new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = "roslyn-mcp-server",
                version = Version
            }
        });
    }

    private object HandleToolsList(JsonNode? id)
    {
        Console.Error.WriteLine($"Handling tools/list request. Tools count: {_tools.Count}");

        var tools = _tools.Select(kvp => new
        {
            name = kvp.Key,
            description = kvp.Value.Description,
            inputSchema = kvp.Value.InputSchema,
            annotations = kvp.Value.Annotations
        }).ToArray();

        return CreateSuccessResponse(id, new { tools });
    }

    private async Task<object> HandleToolCallAsync(JsonNode? id, JsonObject? @params)
    {
        var toolName = @params?["name"]?.GetValue<string>();
        var arguments = @params?["arguments"]?.AsObject();

        Console.Error.WriteLine($"Handling tools/call for: {toolName}");

        if (string.IsNullOrEmpty(toolName))
            return CreateErrorResponse(id, -32602, "Invalid params: tool name is required");

        if (!_toolHandlers.TryGetValue(toolName, out var handler))
            return CreateErrorResponse(id, -32602, $"Unknown tool: {toolName}");

        try
        {
            var result = await handler(arguments);
            return CreateSuccessResponse(id, result);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Tool error: {ex.Message}");
            return CreateSuccessResponse(id, new
            {
                content = new[]
                {
                    new { type = "text", text = $"Error: {ex.Message}" }
                },
                isError = true
            });
        }
    }

    private static object CreateSuccessResponse(JsonNode? id, object result) => new
    {
        jsonrpc = "2.0",
        id = id,
        result
    };

    private static object CreateErrorResponse(JsonNode? id, int code, string message) => new
    {
        jsonrpc = "2.0",
        id = id,
        error = new { code, message }
    };
}

/// <summary>
/// Defines an MCP tool.
/// </summary>
public class ToolDefinition
{
    public required string Description { get; init; }
    public required object InputSchema { get; init; }
    public ToolAnnotations? Annotations { get; init; }
}

/// <summary>
/// Tool annotations for MCP.
/// </summary>
public class ToolAnnotations
{
    public bool? ReadOnlyHint { get; init; }
    public bool? DestructiveHint { get; init; }
    public bool? IdempotentHint { get; init; }
    public bool? OpenWorldHint { get; init; }
}
