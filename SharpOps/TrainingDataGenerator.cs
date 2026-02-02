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
        public int MaxOps { get; init; } = int.MaxValue;  // No limit by default
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
    /// Load projects from solution, project file, or directory.
    /// </summary>
    private static async Task<List<Project>> LoadProjectsAsync(string inputPath, MSBuildWorkspace workspace)
    {
        var projects = new List<Project>();

        if (Directory.Exists(inputPath))
        {
            // Directory: find all .csproj files recursively (skip obj folders)
            var csprojFiles = Directory.GetFiles(inputPath, "*.csproj", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .ToArray();
            Console.Error.WriteLine($"Found {csprojFiles.Length} C# projects in {inputPath}");

            foreach (var csproj in csprojFiles)
            {
                try
                {
                    Console.Error.WriteLine($"Loading project: {csproj}");
                    var project = await workspace.OpenProjectAsync(csproj);
                    projects.Add(project);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Failed to load: {ex.Message}");
                }
            }
        }
        else if (inputPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            // Single project
            Console.Error.WriteLine($"Loading project: {inputPath}");
            var project = await workspace.OpenProjectAsync(inputPath);
            projects.Add(project);
        }
        else if (inputPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                 inputPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            // Solution - only C# projects
            Console.Error.WriteLine($"Loading solution: {inputPath}");
            var solution = await workspace.OpenSolutionAsync(inputPath);
            var csharpProjects = solution.Projects.Where(p => p.Language == "C#").ToList();
            Console.Error.WriteLine($"Found {csharpProjects.Count} C# projects (skipped {solution.Projects.Count() - csharpProjects.Count} non-C#)");
            projects.AddRange(csharpProjects);
        }
        else
        {
            throw new ArgumentException($"Unsupported input: {inputPath}. Use .sln, .slnx, .csproj, or directory path.");
        }

        return projects;
    }

    /// <summary>
    /// Generate training data from a solution, project, or directory of projects.
    /// Each project gets its own ProjectName.jsonl file in the output folder.
    /// </summary>
    public async Task<GenerationResult> GenerateAsync(string inputPath, string outputFolder, GenerationOptions? options = null)
    {
        options ??= new GenerationOptions();
        EnsureMSBuildRegistered();

        // Create output folder if needed
        Directory.CreateDirectory(outputFolder);

        var result = new GenerationResult { OutputFile = outputFolder };
        using var workspace = CreateWorkspace();

        // Collect all unique tokens for BPE special tokens
        var allSyntaxKinds = new HashSet<string>();
        var allSymbolKinds = new HashSet<string>();

        // Determine input type and get projects
        var projects = await LoadProjectsAsync(inputPath, workspace);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        foreach (var project in projects)
        {
            // Skip test projects if configured
            if (!options.IncludeTests && IsTestProject(project))
            {
                continue;
            }

            var projectResult = await ProcessProjectAsync(project, outputFolder, options, jsonOptions, allSyntaxKinds, allSymbolKinds);

            // Accumulate stats
            result.TotalMethods += projectResult.TotalMethods;
            result.ExtractedMethods += projectResult.ExtractedMethods;
            result.SkippedNoDoc += projectResult.SkippedNoDoc;
            result.SkippedTooShort += projectResult.SkippedTooShort;
            result.SkippedTooLong += projectResult.SkippedTooLong;
            result.SkippedLongStrings += projectResult.SkippedLongStrings;
            result.SkippedGenerated += projectResult.SkippedGenerated;
            result.SkippedTests += projectResult.SkippedTests;
            result.SkippedNoBody += projectResult.SkippedNoBody;
        }

        // Write special tokens files for BPE tokenizer
        var syntaxKindsPath = Path.Combine(outputFolder, "syntax_kinds.txt");
        var symbolKindsPath = Path.Combine(outputFolder, "symbol_kinds.txt");
        await File.WriteAllLinesAsync(syntaxKindsPath, allSyntaxKinds.OrderBy(x => x));
        await File.WriteAllLinesAsync(symbolKindsPath, allSymbolKinds.OrderBy(x => x));
        Console.Error.WriteLine($"Special tokens: {allSyntaxKinds.Count} SyntaxKinds, {allSymbolKinds.Count} SymbolKinds");

        Console.Error.WriteLine($"Extraction complete: {result.ExtractedMethods} samples in {outputFolder}");

        return result;
    }

    private async Task<GenerationResult> ProcessProjectAsync(
        Project project,
        string outputFolder,
        GenerationOptions options,
        JsonSerializerOptions jsonOptions,
        HashSet<string> allSyntaxKinds,
        HashSet<string> allSymbolKinds)
    {
        var outputPath = Path.Combine(outputFolder, $"{project.Name}.jsonl");
        var result = new GenerationResult { OutputFile = outputPath };

        var compilation = await project.GetCompilationAsync();
        if (compilation == null) return result;

        var samples = new List<object>();

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

                var (sample, sequence) = TryExtractSample(method, semanticModel, document.FilePath, options, result);
                if (sample == null) continue;

                // Collect unique tokens for BPE (UPPERCASE format)
                foreach (var op in sequence!.Ops)
                {
                    allSyntaxKinds.Add(op.Kind.ToString().ToUpperInvariant());
                    if (op.SymbolKind.HasValue)
                        allSymbolKinds.Add(op.SymbolKind.Value.ToString().ToUpperInvariant());
                }

                samples.Add(sample);
                result.ExtractedMethods++;
            }
        }

        // Only write file if we have samples
        if (samples.Count > 0)
        {
            await using var writer = new StreamWriter(outputPath, append: false,
                encoding: new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            foreach (var sample in samples)
            {
                var json = JsonSerializer.Serialize(sample, jsonOptions);
                await writer.WriteLineAsync(json.Replace("\r\n", "\n"));
            }

            Console.Error.WriteLine($"  {project.Name}: {samples.Count} samples → {Path.GetFileName(outputPath)}");
        }
        else
        {
            Console.Error.WriteLine($"  {project.Name}: 0 samples (skipped)");
        }

        return result;
    }

    private (object? sample, SharpOpsSequence? sequence) TryExtractSample(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        string filePath,
        GenerationOptions options,
        GenerationResult result)
    {
        var symbol = semanticModel.GetDeclaredSymbol(method);
        if (symbol == null) return (null, null);

        // Must have a body
        if (method.Body == null && method.ExpressionBody == null)
        {
            result.SkippedNoBody++;
            return (null, null);
        }

        // Check for XML documentation
        if (options.RequireXmlDoc && !HasXmlDoc(method))
        {
            result.SkippedNoDoc++;
            return (null, null);
        }

        // Check statement count (for block bodies)
        if (method.Body != null && method.Body.Statements.Count < options.MinStatements)
        {
            result.SkippedTooShort++;
            return (null, null);
        }

        // Extract SharpOps
        var sequence = SharpOpsExtractor.Extract(method, semanticModel);

        // Check ops count
        if (sequence.Ops.Count > options.MaxOps)
        {
            result.SkippedTooLong++;
            return (null, null);
        }

        // Skip methods with long strings (SQL schemas, config blocks, etc.)
        if (sequence.StringTable.Any(s => s.Length > options.MaxStringLength))
        {
            result.SkippedLongStrings++;
            return (null, null);
        }

        // Extract context
        var context = ContextExtractor.Extract(method, symbol, semanticModel);

        // Get line number
        var lineSpan = method.GetLocation().GetLineSpan();

        var sample = new
        {
            input = context.Replace("\r\n", "\n"),
            output = sequence.SerializeOps(),
            strings = sequence.StringTable,
            sourceFile = filePath,
            line = lineSpan.StartLinePosition.Line + 1,
            method = symbol.ToDisplayString()
        };

        return (sample, sequence);
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
