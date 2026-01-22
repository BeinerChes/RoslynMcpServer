using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Adds a new member (method, property, field) to a type.
    /// </summary>
    private static void RegisterAddMemberTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_add_member",
            new ToolDefinition
            {
                Description = "Adds a new member (method, property, field, constructor, event) to a type. Uses Roslyn to parse and insert the member with proper formatting. Smart insertion places members with their peers (fields together, methods together, etc.).",
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
                            description = "Name of the type (class, struct, interface) to add the member to"
                        },
                        memberCode = new
                        {
                            type = "string",
                            description = "The complete source code of the member to add (method, property, field, etc.)"
                        },
                        insertionPoint = new
                        {
                            type = "string",
                            description = "Where to insert: 'start', 'end', 'after-fields', 'after-constructors', 'after-properties', 'before-methods'. Default: smart placement based on member type.",
                            @enum = new[] { "start", "end", "after-fields", "after-constructors", "after-properties", "before-methods" }
                        }
                    },
                    required = new[] { "solutionPath", "typeName", "memberCode" }
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
                var memberCode = args?["memberCode"]?.GetValue<string>();
                var insertionPoint = args?["insertionPoint"]?.GetValue<string>();

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

                if (string.IsNullOrWhiteSpace(memberCode))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: memberCode is required" }
                        },
                        isError = true
                    };
                }

                var result = await _analyzerService!.AddMemberAsync(
                    solutionPath,
                    typeName,
                    memberCode,
                    insertionPoint);

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


    private static void WriteToken(string fileName, string token)
    {
        try
        {
            var claudeDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude");
            Directory.CreateDirectory(claudeDir);

            var tokenFile = Path.Combine(claudeDir, fileName);
            File.WriteAllText(tokenFile, token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not write token file {fileName}: {ex.Message}");
        }
    }
}
