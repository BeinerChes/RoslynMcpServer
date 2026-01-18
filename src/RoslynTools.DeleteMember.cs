using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Deletes a member (method, property, field) from a type.
    /// Issue: #39
    /// </summary>
    private static void RegisterDeleteMemberTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_delete_member",
            new ToolDefinition
            {
                Description = "Deletes a member (method, property, field) from a type. Uses Roslyn to precisely locate and remove the member including its attributes and XML documentation. Essential for cleaning up dead code identified by roslyn_find_dead_code.",
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
                            description = "Name of the type (class, struct, interface) containing the member"
                        },
                        memberName = new
                        {
                            type = "string",
                            description = "Name of the member to delete"
                        },
                        memberKind = new
                        {
                            type = "string",
                            description = "Kind of member: 'method', 'property', 'field'. Optional - used to disambiguate when multiple members have the same name.",
                            @enum = new[] { "method", "property", "field" }
                        },
                        parameterTypes = new
                        {
                            type = "string",
                            description = "Parameter types for method overloads, e.g. 'string, int'. Required if multiple method overloads exist."
                        }
                    },
                    required = new[] { "solutionPath", "typeName", "memberName" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    IdempotentHint = false
                }
            },
            async args =>
            {
                var solutionPath = args?["solutionPath"]?.GetValue<string>();
                var typeName = args?["typeName"]?.GetValue<string>();
                var memberName = args?["memberName"]?.GetValue<string>();
                var memberKind = args?["memberKind"]?.GetValue<string>();
                var parameterTypes = args?["parameterTypes"]?.GetValue<string>();

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

                if (string.IsNullOrWhiteSpace(memberName))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: memberName is required" }
                        },
                        isError = true
                    };
                }

                var result = await _analyzerService!.DeleteMemberAsync(
                    solutionPath,
                    typeName,
                    memberName,
                    memberKind,
                    parameterTypes);

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
