using System.Text.Json;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    /// <summary>
    /// Creates a new type (class, interface, struct, record, enum) in a project.
    /// </summary>
    private static void RegisterAddTypeTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_add_type",
            new ToolDefinition
            {
                Description = "Creates a new type (class, interface, struct, record, enum) in a project. Automatically determines file location based on project structure and infers namespace from project name and folder path. The file is created with proper formatting.",
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
                        projectName = new
                        {
                            type = "string",
                            description = "Name of the target project (e.g., 'MyApp.Core')"
                        },
                        typeName = new
                        {
                            type = "string",
                            description = "Name of the type to create (e.g., 'UserService')"
                        },
                        typeKind = new
                        {
                            type = "string",
                            description = "Kind of type to create: 'class', 'interface', 'struct', 'record', 'enum'. Default: 'class'",
                            @enum = new[] { "class", "interface", "struct", "record", "enum" }
                        },
                        @namespace = new
                        {
                            type = "string",
                            description = "Namespace for the type. If not specified, inferred from project name + folder path"
                        },
                        folder = new
                        {
                            type = "string",
                            description = "Subfolder within the project (e.g., 'Services/Auth'). Creates directory if needed"
                        },
                        accessibility = new
                        {
                            type = "string",
                            description = "Access modifier: 'public', 'internal', 'private', 'protected'. Default: 'public'",
                            @enum = new[] { "public", "internal", "private", "protected" }
                        },
                        baseTypes = new
                        {
                            type = "string",
                            description = "Comma-separated base class and/or interfaces (e.g., 'BaseClass, IDisposable, IService')"
                        },
                        isPartial = new
                        {
                            type = "boolean",
                            description = "Create as a partial type. Default: false"
                        },
                        isSealed = new
                        {
                            type = "boolean",
                            description = "Create as sealed (classes and records only). Default: false"
                        },
                        isStatic = new
                        {
                            type = "boolean",
                            description = "Create as static (classes only). Default: false"
                        }
                    },
                    required = new[] { "solutionPath", "projectName", "typeName" }
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
                var projectName = args?["projectName"]?.GetValue<string>();
                var typeName = args?["typeName"]?.GetValue<string>();
                var typeKind = args?["typeKind"]?.GetValue<string>();
                var ns = args?["namespace"]?.GetValue<string>();
                var folder = args?["folder"]?.GetValue<string>();
                var accessibility = args?["accessibility"]?.GetValue<string>();
                var baseTypes = args?["baseTypes"]?.GetValue<string>();
                var isPartial = args?["isPartial"]?.GetValue<bool>() ?? false;
                var isSealed = args?["isSealed"]?.GetValue<bool>() ?? false;
                var isStatic = args?["isStatic"]?.GetValue<bool>() ?? false;

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

                if (string.IsNullOrWhiteSpace(projectName))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: projectName is required" }
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

                var result = await _analyzerService!.AddTypeAsync(
                    solutionPath,
                    projectName,
                    typeName,
                    typeKind,
                    ns,
                    folder,
                    accessibility,
                    baseTypes,
                    isPartial,
                    isSealed,
                    isStatic);

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
