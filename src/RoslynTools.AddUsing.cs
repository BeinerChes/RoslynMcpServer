using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Adds a using directive to a C# file.
    /// </summary>
    private static void RegisterAddUsingTool(McpServer server)
    {
        server.RegisterTool(
            "AddUsing",
            new ToolDefinition
            {
                Description = "Adds a using directive to a C# file. Locates the file by type name or file path. Inserts the using in sorted order. Returns success even if the using already exists (idempotent).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new
                        {
                            type = "string",
                            description = "Name of a type in the target file. Used to locate the file to add the using to."
                        },
                        usingDirective = new
                        {
                            type = "string",
                            description = "The namespace to add, e.g. 'System.Text.Json' or 'using System.Text.Json;' (both formats accepted)"
                        },
                        filePath = new
                        {
                            type = "string",
                            description = "Optional: file path instead of type name. Supports partial match."
                        }
                    },
                    required = new[] { "usingDirective" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var (solutionPath, solutionError) = GetSolutionPathOrError();
                if (solutionError != null) return solutionError;

                var typeName = args?["typeName"]?.GetValue<string>();
                var usingDirective = args?["usingDirective"]?.GetValue<string>();
                var filePath = args?["filePath"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(usingDirective))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: usingDirective is required" }
                        },
                        isError = true
                    };
                }

                if (string.IsNullOrWhiteSpace(typeName) && string.IsNullOrWhiteSpace(filePath))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: either typeName or filePath is required" }
                        },
                        isError = true
                    };
                }

                var result = await SolutionAnalyzerService.AddUsingAsync(
                    solutionPath!,
                    typeName ?? "",
                    usingDirective,
                    filePath);

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
