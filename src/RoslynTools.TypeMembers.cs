using System.Text.Json;
using RoslynMcpServer.Graph;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definitionArray32 = new[] { "all", "methods", "properties", "fields", "events", "constructors" };
    private static readonly string[] definitionArray33 = new[] { "solutionPath", "typeName" };

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
                            @enum = definitionArray32
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
                    required = definitionArray33
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

                var result = await SolutionAnalyzerService.GetTypeMembersAsync(
                    solutionPath,
                    typeName,
                    memberKind,
                    includeInherited,
                    compact);

                // Track for visualization sync
                if (result.Success)
                    LastSymbolTracker.Track(solutionPath, typeName, "type");

                // Fetch related knowledge entries for the type
                List<object>? knowledge = null;
                if (result.Success && result.Type?.FullyQualifiedName != null)
                {
                    try
                    {
                        var db = await GetKnowledgeDatabaseAsync(solutionPath);
                        // Use fully qualified type name from result
                        var entries = await db.GetEntriesForSymbolAsync(result.Type.FullyQualifiedName);

                        if (entries.Count > 0)
                        {
                            knowledge = entries.Select(e => (object)new
                            {
                                e.Id,
                                e.Category,
                                e.Title,
                                e.Content,
                                e.Confidence
                            }).ToList();
                        }
                    }
                    catch
                    {
                        // Knowledge lookup failure shouldn't break the main functionality
                    }
                }

                // Build response with optional knowledge
                var response = new
                {
                    result.Success,
                    result.Error,
                    result.SolutionPath,
                    result.Type,
                    result.TotalMembers,
                    result.Members,
                    Knowledge = knowledge
                };

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(response, JsonOptions) }
                    },
                    isError = !result.Success
                };
            });
    }
}
