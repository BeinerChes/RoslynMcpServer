using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definitionArray32 = new[] { "all", "methods", "properties", "fields", "events", "constructors" };
    private static readonly string[] definitionArray33 = new[] { "typeName" };

    /// <summary>
    /// Gets all members of a type (methods, properties, fields, events, constructors).
    /// </summary>
    private static void RegisterGetTypeMembersTool(McpServer server)
    {
        server.RegisterTool(
            "GetTypeMembers",
            new ToolDefinition
            {
                Description = "Gets all members of a type including methods, properties, fields, events, and constructors. Essential for understanding the structure of large classes without reading the entire file.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
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
                        }
                    },
                    required = new[] { "typeName" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var (solutionPath, solutionError) = GetSolutionPathOrError();
                if (solutionError != null) return solutionError;

                var typeName = args?["typeName"]?.GetValue<string>();

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
                    solutionPath!,
                    typeName,
                    memberKind,
                    includeInherited);

                // Track for visualization sync
                if (result.Success)
                    LastSymbolTracker.Track(solutionPath!, typeName, "type");

                if (!result.Success)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) }
                        },
                        isError = false
                    };
                }

                // Build compact response - group by kind
                var fields = result.Members
                    .Where(m => m.Kind == "Field")
                    .Select(m => FormatMemberCompact(m))
                    .ToList();

                var properties = result.Members
                    .Where(m => m.Kind == "Property")
                    .Select(m => FormatMemberCompact(m))
                    .ToList();

                var methods = result.Members
                    .Where(m => m.Kind == "Method")
                    .Select(m => FormatMemberCompact(m))
                    .ToList();

                var constructors = result.Members
                    .Where(m => m.Kind == "Constructor")
                    .Select(m => FormatMemberCompact(m))
                    .ToList();

                var events = result.Members
                    .Where(m => m.Kind == "Event")
                    .Select(m => FormatMemberCompact(m))
                    .ToList();

                var compactResult = new Dictionary<string, object>
                {
                    ["type"] = result.Type?.FullyQualifiedName ?? typeName,
                    ["count"] = result.TotalMembers
                };

                // Only include non-empty groups
                if (fields.Count > 0) compactResult["fields"] = fields;
                if (properties.Count > 0) compactResult["properties"] = properties;
                if (constructors.Count > 0) compactResult["constructors"] = constructors;
                if (methods.Count > 0) compactResult["methods"] = methods;
                if (events.Count > 0) compactResult["events"] = events;

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(compactResult, JsonOptions) }
                    },
                    isError = false
                };
            });
    }

    private static string FormatMemberCompact(MemberInfo member)
    {
        var sig = member.Signature;

        // For properties, remove the { get; set; } part
        if (member.Kind == "Property")
        {
            var braceIndex = sig.IndexOf('{');
            if (braceIndex > 0)
                sig = sig.Substring(0, braceIndex).Trim();
        }

        return sig;
    }

}
