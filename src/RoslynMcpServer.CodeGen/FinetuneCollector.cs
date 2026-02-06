using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using SharpOps;

namespace RoslynMcpServer.CodeGen;

/// <summary>
/// Collects fine-tune training data from successful add_member and update_method calls.
/// Runs in the background and never affects tool responses.
/// </summary>
internal static class FinetuneCollector
{
    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Collect a training record for a method. Called as fire-and-forget after successful tool calls.
    /// </summary>
    public static async Task CollectAsync(
        string solutionPath,
        string filePath,
        string typeName,
        string methodName,
        string? comment,
        string? parameterTypes = null)
    {
        try
        {
            SolutionAnalyzerService.EnsureMSBuildRegistered();

            using var workspace = SolutionAnalyzerService.CreateWorkspace();
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Find the type
            var typeSymbols = await SymbolFinder.FindSourceDeclarationsAsync(
                solution,
                name => name.Equals(typeName, StringComparison.Ordinal) ||
                        name.Equals(typeName, StringComparison.OrdinalIgnoreCase),
                SymbolFilter.Type);

            var targetType = typeSymbols.OfType<INamedTypeSymbol>().FirstOrDefault();
            if (targetType == null) return;

            // Find the method
            var methods = targetType.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .ToList();

            IMethodSymbol? targetMethod = null;
            if (methods.Count == 1)
            {
                targetMethod = methods[0];
            }
            else if (methods.Count > 1 && !string.IsNullOrEmpty(parameterTypes))
            {
                targetMethod = methods.FirstOrDefault(m =>
                    GetShortParamTypes(m).Equals(parameterTypes, StringComparison.OrdinalIgnoreCase));
            }

            if (targetMethod == null) return;

            // Get the syntax node
            var location = targetMethod.Locations.FirstOrDefault(l => l.IsInSource);
            if (location?.SourceTree == null) return;

            var root = await location.SourceTree.GetRootAsync();
            var node = root.FindNode(location.SourceSpan);
            while (node != null && node is not MethodDeclarationSyntax)
                node = node.Parent;

            if (node is not MethodDeclarationSyntax methodSyntax) return;

            // Get semantic model
            var document = solution.GetDocument(location.SourceTree);
            if (document == null) return;

            var semanticModel = await document.GetSemanticModelAsync();
            if (semanticModel == null) return;

            // Extract SharpOps
            var sequence = SharpOpsExtractor.Extract(methodSyntax, semanticModel);

            // Extract context
            var context = ContextExtractor.Extract(methodSyntax, targetMethod, semanticModel);

            // Get original C# body
            var originalCSharp = methodSyntax.Body != null
                ? methodSyntax.Body.NormalizeWhitespace().ToFullString()
                : methodSyntax.ExpressionBody?.NormalizeWhitespace().ToFullString() ?? "";

            // Get line number
            var lineSpan = methodSyntax.GetLocation().GetLineSpan();

            // Build the key
            var key = targetMethod.ContainingType != null
                ? $"{targetMethod.ContainingType.ToDisplayString()}.{targetMethod.Name}"
                : targetMethod.ToDisplayString();

            // Make source file relative to solution directory
            var solutionDir = Path.GetDirectoryName(solutionPath) ?? "";
            var relativeSourceFile = Path.GetRelativePath(solutionDir, filePath);

            // Build symbol tables
            var tables = sequence.SymbolTables;

            var record = new Dictionary<string, object?>
            {
                ["key"] = key,
                ["input"] = context.Replace("\r\n", "\n"),
                ["output"] = sequence.SerializeOps(),
                ["originalCSharp"] = originalCSharp.Replace("\r\n", "\n"),
                ["strings"] = sequence.StringTable,
                ["locals"] = tables[SymbolKind.Local],
                ["parameters"] = tables[SymbolKind.Parameter],
                ["fields"] = tables[SymbolKind.Field],
                ["methods"] = tables[SymbolKind.Method],
                ["namedTypes"] = tables[SymbolKind.NamedType],
                ["properties"] = tables[SymbolKind.Property],
                ["comment"] = comment,
                ["sourceFile"] = relativeSourceFile,
                ["line"] = lineSpan.StartLinePosition.Line + 1,
                ["timestamp"] = DateTime.UtcNow.ToString("o")
            };

            // Write to JSONL
            var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
            var outputDir = Path.Combine(AppContext.BaseDirectory, "Models", "finetune", "dataset");
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, $"{solutionName}.jsonl");

            await WriteRecordAsync(outputPath, key, record);

            Console.Error.WriteLine($"[FinetuneCollector] Collected: {key} → {outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FinetuneCollector] Error: {ex.Message}");
        }
    }

    private static async Task WriteRecordAsync(string outputPath, string key, Dictionary<string, object?> record)
    {
        await _writeLock.WaitAsync();
        try
        {
            // Read existing records
            var records = new List<Dictionary<string, object?>>();
            if (File.Exists(outputPath))
            {
                var lines = await File.ReadAllLinesAsync(outputPath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var existing = JsonSerializer.Deserialize<Dictionary<string, object?>>(line, _jsonOptions);
                        if (existing != null)
                        {
                            var existingKey = existing.TryGetValue("key", out var k) ? k?.ToString() : null;
                            if (existingKey != key)
                                records.Add(existing);
                        }
                    }
                    catch
                    {
                        // Skip malformed lines
                    }
                }
            }

            records.Add(record);

            await using var writer = new StreamWriter(outputPath, append: false,
                encoding: new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            foreach (var r in records)
            {
                var json = JsonSerializer.Serialize(r, _jsonOptions);
                await writer.WriteLineAsync(json);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string GetShortParamTypes(IMethodSymbol method)
    {
        return string.Join(", ", method.Parameters.Select(p => p.Type.Name));
    }
}
