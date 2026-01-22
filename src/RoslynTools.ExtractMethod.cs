using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Extracts a code block into a new method using Roslyn data flow analysis.
    /// </summary>
    private static void RegisterExtractMethodTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_extract_method",
            new ToolDefinition
            {
                Description = "Extracts a code block into a new method using Roslyn data flow analysis. Automatically detects parameters (variables that flow in) and return values (variables that flow out). Handles async methods and multiple return values via tuples.",
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
                        filePath = new
                        {
                            type = "string",
                            description = "Absolute path to the source file containing the code to extract"
                        },
                        startLine = new
                        {
                            type = "integer",
                            description = "First line number (1-based) of the code to extract",
                            minimum = 1
                        },
                        endLine = new
                        {
                            type = "integer",
                            description = "Last line number (1-based) of the code to extract",
                            minimum = 1
                        },
                        methodName = new
                        {
                            type = "string",
                            description = "Name for the new extracted method"
                        },
                        accessibility = new
                        {
                            type = "string",
                            description = "Access modifier for the new method. Default: 'private'",
                            @enum = new[] { "private", "internal", "protected", "public" }
                        }
                    },
                    required = new[] { "solutionPath", "filePath", "startLine", "endLine", "methodName" }
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
                var filePath = args?["filePath"]?.GetValue<string>();
                var startLine = args?["startLine"]?.GetValue<int>() ?? 0;
                var endLine = args?["endLine"]?.GetValue<int>() ?? 0;
                var methodName = args?["methodName"]?.GetValue<string>();
                var accessibility = args?["accessibility"]?.GetValue<string>();

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

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: filePath is required" }
                        },
                        isError = true
                    };
                }

                if (startLine < 1)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: startLine must be >= 1" }
                        },
                        isError = true
                    };
                }

                if (endLine < startLine)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: endLine must be >= startLine" }
                        },
                        isError = true
                    };
                }

                if (string.IsNullOrWhiteSpace(methodName))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: methodName is required" }
                        },
                        isError = true
                    };
                }

                var result = await _analyzerService!.ExtractMethodAsync(
                    solutionPath,
                    filePath,
                    startLine,
                    endLine,
                    methodName,
                    accessibility);

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
