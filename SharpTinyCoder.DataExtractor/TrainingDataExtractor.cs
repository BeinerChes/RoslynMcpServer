using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using System.Text;

namespace SharpTinyCoder.DataExtractor;

/// <summary>
/// Main orchestrator for extracting training data from C# solutions.
/// </summary>
public sealed class TrainingDataExtractor
{
    private static bool _msBuildRegistered;
    private static readonly object _lockObject = new();

    private readonly ExtractionOptions _options;
    private readonly MethodProcessor _methodProcessor;

    public TrainingDataExtractor(ExtractionOptions? options = null)
    {
        _options = options ?? new ExtractionOptions();
        _methodProcessor = new MethodProcessor(_options);
    }

    /// <summary>
    /// Ensures MSBuild is registered. Must be called before any Roslyn operations.
    /// </summary>
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

                // Use the newest version available
                var instance = instances.OrderByDescending(i => i.Version).First();
                MSBuildLocator.RegisterInstance(instance);
                Console.Error.WriteLine($"Registered MSBuild: {instance.Name} {instance.Version}");
            }

            _msBuildRegistered = true;
        }
    }

    /// <summary>
    /// Creates an MSBuildWorkspace configured for design-time builds.
    /// </summary>
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
    /// Extracts training data from a solution and writes to JSONL.
    /// </summary>
    public async Task<ExtractionResult> ExtractAsync(string solutionPath, string outputPath)
    {
        EnsureMSBuildRegistered();

        var result = new ExtractionResult { OutputFile = outputPath };

        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");
        }

        Console.Error.WriteLine($"Loading solution: {solutionPath}");

        using var workspace = CreateWorkspace();
        var solution = await workspace.OpenSolutionAsync(solutionPath);

        await using var writer = new JsonlWriter(outputPath);

        foreach (var project in solution.Projects)
        {
            Console.Error.WriteLine($"Processing project: {project.Name}");

            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var document in project.Documents)
            {
                if (document.FilePath == null) continue;

                var syntaxTree = await document.GetSyntaxTreeAsync();
                if (syntaxTree == null) continue;

                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync();

                var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

                foreach (var method in methods)
                {
                    result.TotalMethods++;

                    var symbol = semanticModel.GetDeclaredSymbol(method);
                    if (symbol == null) continue;

                    var (include, skipReason) = _methodProcessor.ShouldInclude(method, symbol, document.FilePath);

                    if (!include)
                    {
                        switch (skipReason)
                        {
                            case "generated": result.SkippedGenerated++; break;
                            case "accessibility": result.SkippedAccessibility++; break;
                            case "no-body": result.SkippedNoBody++; break;
                            case "no-doc": result.SkippedNoDoc++; break;
                            case "body-length": result.SkippedBodyLength++; break;
                        }
                        continue;
                    }

                    var sample = ExtractSample(method, symbol, semanticModel, document.FilePath);
                    await writer.WriteAsync(sample);
                    result.ExtractedMethods++;
                }
            }
        }

        await writer.FlushAsync();

        Console.Error.WriteLine($"Extraction complete: {result.ExtractedMethods} samples written to {outputPath}");

        return result;
    }

    private TrainingSample ExtractSample(
        MethodDeclarationSyntax method,
        IMethodSymbol symbol,
        SemanticModel semanticModel,
        string filePath)
    {
        var xmlDoc = _methodProcessor.ExtractXmlDoc(method);
        var signature = _methodProcessor.ExtractSignature(method, symbol);
        var usedFields = _methodProcessor.ExtractClassFields(symbol, method, semanticModel);
        var body = _methodProcessor.ExtractBody(method);

        var inputBuilder = new StringBuilder();
        inputBuilder.AppendLine("INPUT:");
        inputBuilder.AppendLine(xmlDoc);
        inputBuilder.AppendLine(signature);

        if (!string.IsNullOrWhiteSpace(usedFields))
        {
            inputBuilder.AppendLine();
            inputBuilder.AppendLine("USED FIELDS:");
            inputBuilder.AppendLine(usedFields);
        }

        // Add separator token for clear input/output boundary
        inputBuilder.AppendLine();
        inputBuilder.Append("<|output|>");

        var lineSpan = method.GetLocation().GetLineSpan();

        return new TrainingSample
        {
            Input = inputBuilder.ToString(),
            Output = body,
            SourceFile = filePath,
            Line = lineSpan.StartLinePosition.Line + 1,
            MethodName = symbol.ToDisplayString()
        };
    }
}
