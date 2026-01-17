using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Gets all members of a type (methods, properties, fields, events, constructors).
    /// </summary>
    private static void RegisterGetTypeMembersTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_get_type_members",
            new ToolDefinition
            {
                Description = "Gets all members of a type including methods, properties, fields, events, and constructors. Essential for understanding the structure of large classes without reading the entire file.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        solutionPath = new
                        {
                            type = "string",
                            description = "Absolute path to the .sln or .slnx solution file"
                        },
                        typeName = new
                        {
                            type = "string",
                            description = "Name of the type (class, interface, struct) to get members from"
                        },
                        memberKind = new
                        {
                            type = "string",
                            description = "Filter by member kind: 'all', 'methods', 'properties', 'fields', 'events', 'constructors'. Default: 'all'",
                            @enum = new[] { "all", "methods", "properties", "fields", "events", "constructors" }
                        },
                        includeInherited = new
                        {
                            type = "boolean",
                            description = "Include members inherited from base classes. Default: false"
                        },
                        compact = new
                        {
                            type = "boolean",
                            description = "Return minimal fields only (name, kind, signature). Default: true. Set to false for detailed info (filePath, line, accessibility, isStatic, etc.)"
                        }
                    },
                    required = new[] { "solutionPath", "typeName" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var solutionPath = args?["solutionPath"]?.GetValue<string>();
                var typeName = args?["typeName"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(solutionPath))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: solutionPath is required" }
                        },
                        isError = true
                    };
                }

                if (string.IsNullOrWhiteSpace(typeName))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: typeName is required" }
                        },
                        isError = true
                    };
                }

                // Parse optional parameters
                var memberKindStr = args?["memberKind"]?.GetValue<string>() ?? "all";
                var includeInherited = args?["includeInherited"]?.GetValue<bool>() ?? false;
                var compact = args?["compact"]?.GetValue<bool>() ?? true;

                var memberKind = memberKindStr.ToLowerInvariant() switch
                {
                    "methods" => MemberKindFilter.Methods,
                    "properties" => MemberKindFilter.Properties,
                    "fields" => MemberKindFilter.Fields,
                    "events" => MemberKindFilter.Events,
                    "constructors" => MemberKindFilter.Constructors,
                    _ => MemberKindFilter.All
                };

                var result = await _analyzerService!.GetTypeMembersAsync(
                    solutionPath,
                    typeName,
                    memberKind,
                    includeInherited,
                    compact);

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) }
                    },
                    isError = !result.Success
                };
            });
    }
}
