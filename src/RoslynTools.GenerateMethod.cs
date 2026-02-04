using System.Text.Json.Nodes;
using SharpOps.Inference;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static SharpOpsService? _sharpOpsService;
    private static string? _sharpOpsError;

    private static SharpOpsService? GetSharpOpsService()
    {
        if (_sharpOpsService != null) return _sharpOpsService;
        if (_sharpOpsError != null) return null;

        try
        {
            // Look for model files relative to exe
            var baseDir = AppContext.BaseDirectory;
            var modelPath = Path.Combine(baseDir, "Models", "sharptinycoder.onnx");
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

    private static void RegisterGenerateMethodTool(McpServer server)
    {
        server.RegisterTool(
            "GenerateMethod",
            new ToolDefinition
            {
                Description = "Generate a C# method body from a method signature using SharpTinyCoder (4.3M parameter model trained on C# code). Returns SharpOps intermediate format that can be compiled to C#.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        methodSignature = new
                        {
                            type = "string",
                            description = "The full method signature, e.g. \"public void Initialize(AnalysisContext context)\""
                        },
                        description = new
                        {
                            type = "string",
                            description = "Optional natural language description of what the method should do"
                        },
                        fields = new
                        {
                            type = "object",
                            description = "Optional dictionary of field names to types that the method can access, e.g. {\"_db\": \"Database\", \"Rule\": \"DiagnosticDescriptor\"}",
                            additionalProperties = new { type = "string" }
                        },
                        temperature = new
                        {
                            type = "number",
                            description = "Sampling temperature (0.0-2.0). Higher = more creative, lower = more deterministic. Default: 0.0 (greedy decoding)",
                            minimum = 0.0,
                            maximum = 2.0
                        },
                        maxTokens = new
                        {
                            type = "integer",
                            description = "Maximum tokens to generate. Default: 512",
                            minimum = 1,
                            maximum = 2048
                        }
                    },
                    required = new[] { "methodSignature" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    IdempotentHint = true // Greedy decoding is deterministic by default
                }
            },
            HandleGenerateMethodAsync);
    }

    private static Task<object> HandleGenerateMethodAsync(JsonObject? args)
    {
        // Check if model is available
        var service = GetSharpOpsService();
        if (service == null)
        {
            return Task.FromResult<object>(CreateErrorResponse(
                _sharpOpsError ?? "SharpTinyCoder model not available"));
        }

        // Get required parameter
        if (!TryGetRequiredString(args, "methodSignature", out var methodSignature, out var error))
            return Task.FromResult(error!);

        // Get optional parameters
        var description = args?["description"]?.GetValue<string>();
        var temperature = (float)GetOptionalDouble(args, "temperature", 0.0);
        var maxTokens = GetOptionalInt(args, "maxTokens", 512);

        // Parse fields dictionary if provided
        Dictionary<string, string>? fields = null;
        var fieldsNode = args?["fields"];
        if (fieldsNode is JsonObject fieldsObj)
        {
            fields = new Dictionary<string, string>();
            foreach (var prop in fieldsObj)
            {
                if (prop.Value is JsonValue val)
                {
                    fields[prop.Key] = val.GetValue<string>();
                }
            }
        }

        try
        {
            // Generate SharpOps with stats
            var stats = service.GenerateWithStats(
                methodSignature,
                fields,
                description,
                temperature,
                topP: 0.9f,
                maxTokens);

            // Log inference stats
            Logging.ToolCallLogger.Log(new Logging.ToolCallEntry
            {
                Tool = "generate_method_inference",
                Success = true,
                DurationMs = (long)stats.ElapsedMs,
                InputTokens = stats.InputTokens,
                OutputTokens = stats.OutputTokens,
                TokensPerSecond = Math.Round(stats.TokensPerSecond, 1)
            });

            // Try to compile to C# for preview
            string? compiledCSharp = null;
            string? compileError = null;
            try
            {
                var sequence = SharpOps.SharpOpsSequence.ParseOps(stats.Output, null);

                // Build symbol tables from available context
                PopulateSymbolTablesFromContext(sequence, methodSignature, fields);

                compiledCSharp = SharpOps.SharpOpsCompiler.CompileToString(sequence);
            }
            catch (Exception ex)
            {
                compileError = ex.Message;
            }

            return Task.FromResult<object>(CreateSuccessResponse(new
            {
                sharpOps = stats.Output.Trim(),
                compiledCSharp,
                compileError,
                prompt = SharpOpsService.BuildPrompt(methodSignature, fields, description)
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult<object>(CreateErrorResponse($"Generation failed: {ex.Message}"));
        }
    }
}
