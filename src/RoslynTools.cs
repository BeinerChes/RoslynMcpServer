using System.Text.Json;
using SharpOps.Inference;

namespace RoslynMcpServer;

/// <summary>
/// Registers all Roslyn-related MCP tools.
/// </summary>
public static partial class RoslynTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static SolutionAnalyzerService? _analyzerService;
    private static SharpOpsService? _sharpOpsService;
    private static string? _sharpOpsError;

    private static SharpOpsService? GetSharpOpsService()
    {
        if (_sharpOpsService != null) return _sharpOpsService;
        if (_sharpOpsError != null) return null;

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var modelPath = Path.Combine(baseDir, "Models", "checkpoint.pt");
            var tokenizerPath = Path.Combine(baseDir, "Models", "tokenizer", "tokenizer.json");

            if (!File.Exists(modelPath))
            {
                _sharpOpsError = $"Model file not found: {modelPath}";
                return null;
            }

            if (!File.Exists(tokenizerPath))
            {
                _sharpOpsError = $"Tokenizer file not found: {tokenizerPath}";
                return null;
            }

            Console.Error.WriteLine($"Loading SharpTinyCoder model from {modelPath}");
            _sharpOpsService = new SharpOpsService(modelPath, tokenizerPath);
            Console.Error.WriteLine("SharpTinyCoder model loaded successfully");
            return _sharpOpsService;
        }
        catch (Exception ex)
        {
            _sharpOpsError = $"Failed to load SharpTinyCoder model: {ex.Message}";
            Console.Error.WriteLine(_sharpOpsError);
            return null;
        }
    }

    /// <summary>
    /// Auto-detected solution path. Set at startup, used by all tools.
    /// Issue: #115
    /// </summary>
    private static string? _solutionPath;

    /// <summary>
    /// Solution directory for relative path calculations.
    /// </summary>
    private static string _solutionDir = "";

    /// <summary>
    /// Registers all tools with the MCP server.
    /// </summary>
    public static void RegisterAll(McpServer server, SolutionAnalyzerService? analyzerService = null)
    {
        _analyzerService = analyzerService ?? new SolutionAnalyzerService();

        // Auto-detect solution path at startup (Issue #115)
        _solutionPath = DetectSolutionPath();
        _solutionDir = _solutionPath != null ? Path.GetDirectoryName(_solutionPath) ?? "" : "";

        if (_solutionPath != null)
        {
            Console.Error.WriteLine($"Auto-detected solution: {Path.GetFileName(_solutionPath)}");
        }
        else
        {
            Console.Error.WriteLine("Warning: No solution file detected. Run from solution directory or use --init.");
        }

        RegisterEchoTool(server);
        RegisterGetServerInfoTool(server);
        RegisterGetProjectsInBuildOrderTool(server);
        RegisterFindSymbolTool(server);
        RegisterGetReferencesTool(server);
        RegisterGetCallersTool(server);
        RegisterGetTypeMembersTool(server);
        RegisterGetMethodBodyTool(server);
        RegisterUpdateMethodTool(server);
        RegisterGetDiagnosticsTool(server);
        RegisterAddMemberTool(server);
        RegisterAddTypeTool(server);
        RegisterDeleteMemberTool(server);
        RegisterApplyCodeFixTool(server);
        RegisterBatchApplyCodeFix(server);
        RegisterRemoveUnnecessaryUsingsTool(server);
        RegisterAddUsingTool(server);
        RegisterRenameSymbolTool(server);
        RegisterExtractMethodTool(server);
        RegisterFindDeadCodeTool(server);
        RegisterGetInstructionsTool(server);

        // Knowledge base tools
        RegisterKnowledgeAddTool(server);
        RegisterKnowledgeSearchTool(server);
        RegisterKnowledgeDeleteTool(server);
        RegisterKnowledgeGetTool(server);

        // Code generation tools

        // Training tools
        RegisterFinetuneTool(server);

        // Usage reporting
        RegisterUsageReportTool(server);
    }

    private static readonly string[] definitionArray11 = new[] { "message" };

    /// <summary>
    /// A simple echo tool for testing the MCP connection.
    /// </summary>
    private static void RegisterEchoTool(McpServer server)
    {
        server.RegisterTool(
            "Echo",
            new ToolDefinition
            {
                Description = "A simple echo tool for testing. Returns the message you send.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        message = new
                        {
                            type = "string",
                            description = "The message to echo back"
                        }
                    },
                    required = definitionArray11
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                var message = args?["message"]?.GetValue<string>() ?? "No message provided";

                var result = new
                {
                    echoedMessage = message,
                    timestamp = DateTime.UtcNow.ToString("O"),
                    serverName = "roslyn-mcp-server"
                };

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) }
                    }
                };
            });
    }

    private static readonly string[] stringArray = new[]
                    {
                        "Solution and project analysis",
                        "Symbol finding with semantic search",
                        "Reference and implementation tracking",
                        "Diagnostics with .NET analyzers (CA* rules)",
                        "Code fixes and batch fixes",
                        "Refactoring (rename, add member, update method)"
                    };

    /// <summary>
    /// Returns information about the MCP server.
    /// </summary>
    private static void RegisterGetServerInfoTool(McpServer server)
    {
        server.RegisterTool(
            "GetServerInfo",
            new ToolDefinition
            {
                Description = "Returns information about the Roslyn MCP server, including version and available capabilities.",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true
                }
            },
            async args =>
            {
                // Try loading analyzers to get diagnostics
                var analyzers = AnalyzerLoader.GetNetAnalyzers();

                var info = new
                {
                    name = "roslyn-mcp-server",
                    version = McpServer.Version,
                    description = "MCP server for C# solution analysis using Roslyn",
                    runtime = Environment.Version.ToString(),
                    os = Environment.OSVersion.ToString(),
                    netAnalyzersLoaded = analyzers.Length,
                    capabilities = stringArray
                };

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(info, JsonOptions) }
                    }
                };
            });
    }

    private static readonly string[] definitionArray12 = new[] { "solutionPath" };

    /// <summary>
    /// Gets all projects in a solution in build order (dependencies first).
    /// </summary>
    private static void RegisterGetProjectsInBuildOrderTool(McpServer server)
    {
        server.RegisterTool(
            "GetProjectsInBuildOrder",
            new ToolDefinition
            {
                Description = "Loads a .NET solution file and returns all projects in build order (dependencies first). Each project includes its name, file path, language, and direct dependencies.",
                InputSchema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>()
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

                var result = await _analyzerService!.GetProjectsInBuildOrderAsync(solutionPath!);

                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) }
                    },
                    isError = false
                };
            });
    }
}
