using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definition = new[] { "start", "end", "after-fields", "after-constructors", "after-properties", "before-methods" };
    private static readonly string[] definitionArray = new[] { "solutionPath", "typeName", "memberCode" };

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
                    required = new[] { "typeName", "memberCode" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    IdempotentHint = false
                }
            },
            async args =>
            {
                var (solutionPath, solutionError) = GetSolutionPathOrError();
                if (solutionError != null) return solutionError;

                var typeName = args?["typeName"]?.GetValue<string>();
                var memberCode = args?["memberCode"]?.GetValue<string>();
                var insertionPoint = args?["insertionPoint"]?.GetValue<string>();

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

                var result = await SolutionAnalyzerService.AddMemberAsync(
                    solutionPath!,
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
            // Write to exe directory (.roslyn-mcp/ folder)
            var tokenFile = Path.Combine(AppContext.BaseDirectory, fileName);
            File.WriteAllText(tokenFile, token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not write token file {fileName}: {ex.Message}");
        }
    }


    private static string UpdateGitignore(string gitignorePath)
    {
        var entriesToAdd = new List<string>();
        var existingContent = "";

        if (File.Exists(gitignorePath))
        {
            existingContent = File.ReadAllText(gitignorePath);
            var lines = existingContent.Split('\n').Select(l => l.Trim()).ToHashSet();

            // Check if entries already exist (with or without trailing slash)
            if (!lines.Contains(".claude") && !lines.Contains(".claude/"))
            {
                entriesToAdd.Add(".claude/");
            }
            if (!lines.Contains(".roslyn-mcp") && !lines.Contains(".roslyn-mcp/"))
            {
                entriesToAdd.Add(".roslyn-mcp/");
            }

            if (entriesToAdd.Count == 0)
            {
                return "skipped (entries already present)";
            }

            // Append new entries
            var newContent = existingContent.TrimEnd();
            if (!newContent.EndsWith("\n"))
            {
                newContent += "\n";
            }
            newContent += "\n# Claude Code and Roslyn MCP (local configuration)\n";
            newContent += string.Join("\n", entriesToAdd) + "\n";
            File.WriteAllText(gitignorePath, newContent);
            return "updated";
        }
        else
        {
            // Create new .gitignore with entries
            var content = "# Claude Code and Roslyn MCP (local configuration)\n.claude/\n.roslyn-mcp/\n";
            File.WriteAllText(gitignorePath, content);
            return "created";
        }
    }


    private static (string? Path, object? Error) GetSolutionPathOrError()
    {
        if (string.IsNullOrEmpty(_solutionPath))
        {
            return (null, CreateErrorResponse("No solution detected. Run MCP server from solution directory."));
        }
        return (_solutionPath, null);
    }
}
