using System.Text.Json;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace SharpOps;

/// <summary>
/// Generates JSONL training data from C# solutions.
/// </summary>
public class TrainingDataGenerator
{
    private static bool _msBuildRegistered;
    private static readonly object _lockObject = new();

    /// <summary>
    /// Options for generation.
    /// </summary>
    public record GenerationOptions
    {
        public int MinStatements { get; init; } = 3;
        public int MaxOps { get; init; } = 100;
        public int MaxStringLength { get; init; } = 100;  // Skip methods with strings > this
        public bool IncludeTests { get; init; } = false;
        public bool RequireXmlDoc { get; init; } = true;
    }

    /// <summary>
    /// Result statistics.
    /// </summary>
    public record GenerationResult
    {
        public int TotalMethods { get; set; }
        public int ExtractedMethods { get; set; }
        public int SkippedNoDoc { get; set; }
        public int SkippedTooShort { get; set; }
        public int SkippedTooLong { get; set; }
        public int SkippedLongStrings { get; set; }
        public int SkippedGenerated { get; set; }
        public int SkippedTests { get; set; }
        public int SkippedNoBody { get; set; }
        public required string OutputFile { get; init; }
    }

    private static void EnsureMSBuildRegistered()
    {
        if (_msBuildRegistered) return;

        lock (_lockObject)
        {
            if (_msBuildRegistered) return;

            if (!MSBuildLocator.IsRegistered)
            {
                var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
                if (instances.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No MSBuild instances found. Please install Visual Studio or the .NET SDK.");
                }

                var instance = instances.OrderByDescending(i => i.Version).First();
                MSBuildLocator.RegisterInstance(instance);
                Console.Error.WriteLine($"Registered MSBuild: {instance.Name} {instance.Version}");
            }

            _msBuildRegistered = true;
        }
    }

    private static MSBuildWorkspace CreateWorkspace()
    {
        var properties = new Dictionary<string, string>
        {
            { "DesignTimeBuild", "true" },
            { "BuildingInsideVisualStudio", "true" },
            { "ProvideCommandLineArgs", "true" }
        };

        var workspace = MSBuildWorkspace.Create(properties);
        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                Console.Error.WriteLine($"Workspace failure: {args.Diagnostic.Message}");
            }
        });

        return workspace;
    }

    /// <summary>
    /// Generate training data from a solution.
    /// </summary>
    public async Task<GenerationResult> GenerateAsync(string solutionPath, string outputPath, GenerationOptions? options = null)
    {
        options ??= new GenerationOptions();
        EnsureMSBuildRegistered();

        var result = new GenerationResult { OutputFile = outputPath };

        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");
        }

        Console.Error.WriteLine($"Loading solution: {solutionPath}");

        using var workspace = CreateWorkspace();
        var solution = await workspace.OpenSolutionAsync(solutionPath);

        await using var writer = new StreamWriter(outputPath, append: false,
            encoding: new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        foreach (var project in solution.Projects)
        {
            Console.Error.WriteLine($"Processing project: {project.Name}");

            // Skip test projects if configured
            if (!options.IncludeTests && IsTestProject(project))
            {
                continue;
            }

            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var document in project.Documents)
            {
                if (document.FilePath == null) continue;

                // Skip generated files
                if (IsGeneratedFile(document.FilePath))
                {
                    continue;
                }

                var syntaxTree = await document.GetSyntaxTreeAsync();
                if (syntaxTree == null) continue;

                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync();

                var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

                foreach (var method in methods)
                {
                    result.TotalMethods++;

                    var sample = TryExtractSample(method, semanticModel, document.FilePath, options, result);
                    if (sample == null) continue;

                    var json = JsonSerializer.Serialize(sample, jsonOptions);
                    await writer.WriteLineAsync(json.Replace("\r\n", "\n"));
                    result.ExtractedMethods++;
                }
            }
        }

        Console.Error.WriteLine($"Extraction complete: {result.ExtractedMethods} samples written to {outputPath}");

        return result;
    }

    private object? TryExtractSample(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        string filePath,
        GenerationOptions options,
        GenerationResult result)
    {
        var symbol = semanticModel.GetDeclaredSymbol(method);
        if (symbol == null) return null;

        // Must have a body
        if (method.Body == null && method.ExpressionBody == null)
        {
            result.SkippedNoBody++;
            return null;
        }

        // Check for XML documentation
        if (options.RequireXmlDoc && !HasXmlDoc(method))
        {
            result.SkippedNoDoc++;
            return null;
        }

        // Check statement count (for block bodies)
        if (method.Body != null && method.Body.Statements.Count < options.MinStatements)
        {
            result.SkippedTooShort++;
            return null;
        }

        // Extract SharpOps
        var sequence = SharpOpsExtractor.Extract(method, semanticModel);

        // Check ops count
        if (sequence.Ops.Count > options.MaxOps)
        {
            result.SkippedTooLong++;
            return null;
        }

        // Skip methods with long strings (SQL schemas, config blocks, etc.)
        if (sequence.StringTable.Any(s => s.Length > options.MaxStringLength))
        {
            result.SkippedLongStrings++;
            return null;
        }

        // Extract context
        var context = ContextExtractor.Extract(method, symbol, semanticModel);

        // Get line number
        var lineSpan = method.GetLocation().GetLineSpan();

        return new
        {
            input = context.Replace("\r\n", "\n"),
            output = sequence.SerializeOps(),
            strings = sequence.StringTable,
            sourceFile = filePath,
            line = lineSpan.StartLinePosition.Line + 1,
            method = symbol.ToDisplayString()
        };
    }

    private static bool HasXmlDoc(MethodDeclarationSyntax method)
    {
        var trivia = method.GetLeadingTrivia()
            .FirstOrDefault(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                  t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

        if (trivia == default) return false;

        var structure = trivia.GetStructure();
        if (structure == null) return false;

        return structure.DescendantNodes()
            .OfType<XmlElementSyntax>()
            .Any(e => e.StartTag.Name.LocalName.Text == "summary");
    }

    private static bool IsGeneratedFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               filePath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar);
    }

    private static bool IsTestProject(Project project)
    {
        var name = project.Name.ToLower();
        return name.Contains("test") || name.Contains("spec");
    }
}
