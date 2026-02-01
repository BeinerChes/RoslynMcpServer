using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definitionArray6 = ["class", "interface", "struct", "record", "enum"];
    private static readonly string[] definitionArray7 = ["public", "internal", "private", "protected"];
    private static readonly string[] definitionArray8 = ["projectName", "typeName"];

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
                    required = new[] { "projectName", "typeName" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    IdempotentHint = false
                }
            },
            HandleAddTypeAsync);
    }

    /// <summary>
    /// Handler for the add_type tool.
    /// </summary>
    private static async Task<object> HandleAddTypeAsync(JsonObject? args)
    {
        var (solutionPath, solutionError) = GetSolutionPathOrError();
        if (solutionError != null) return solutionError;

        if (!TryGetRequiredString(args, "projectName", out var projectName, out var error))
            return error!;

        if (!TryGetRequiredString(args, "typeName", out var typeName, out error))
            return error!;

        var typeKind = args?["typeKind"]?.GetValue<string>();
        var ns = args?["namespace"]?.GetValue<string>();
        var folder = args?["folder"]?.GetValue<string>();
        var accessibility = args?["accessibility"]?.GetValue<string>();
        var baseTypes = args?["baseTypes"]?.GetValue<string>();
        var isPartial = GetOptionalBool(args, "isPartial", false);
        var isSealed = GetOptionalBool(args, "isSealed", false);
        var isStatic = GetOptionalBool(args, "isStatic", false);

        var result = await SolutionAnalyzerService.AddTypeAsync(
            solutionPath!, projectName, typeName, typeKind, ns, folder,
            accessibility, baseTypes, isPartial, isSealed, isStatic);

        return CreateSuccessResponse(result, !result.Success);
    }
}
