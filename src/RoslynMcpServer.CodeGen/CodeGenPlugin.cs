using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpOps.Inference;
using RoslynSymbolKind = Microsoft.CodeAnalysis.SymbolKind;

namespace RoslynMcpServer.CodeGen;

/// <summary>
/// Implements the ICodeGenPlugin interface using SharpTinyCoder for AI code generation.
/// Loaded at runtime via assembly scanning when RoslynMcpServer.CodeGen.dll is present.
/// </summary>
public class CodeGenPlugin : ICodeGenPlugin
{
    private SharpOpsService? _sharpOpsService;
    private string? _sharpOpsError;

    private SharpOpsService? GetSharpOpsService()
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

    internal void ReloadSharpOpsModel()
    {
        var old = _sharpOpsService;
        _sharpOpsService = null;
        _sharpOpsError = null;
        old?.Dispose();
    }

    public async Task<(string FullMemberCode, bool Failed)> HandleAutoGenerateAsync(
        string solutionPath,
        string typeName,
        string memberSignature,
        string? comment)
    {
        var service = GetSharpOpsService();
        if (service == null)
        {
            var stubCode = memberSignature + "\n{ throw new NotImplementedException(); }";
            return (stubCode, true);
        }

        try
        {
            var solution = await SolutionAnalyzerService.LoadSolutionAsync(solutionPath);
            if (solution == null)
            {
                var stubCode = memberSignature + "\n{ throw new NotImplementedException(); }";
                return (stubCode, true);
            }

            // Find the type symbol
            INamedTypeSymbol? typeSymbol = null;
            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var syntaxTree in compilation.SyntaxTrees)
                {
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    var root = await syntaxTree.GetRootAsync();

                    foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(typeDecl);
                        if (symbol != null &&
                            (symbol.Name == typeName || symbol.ToDisplayString().EndsWith("." + typeName)))
                        {
                            typeSymbol = (INamedTypeSymbol)symbol;
                            break;
                        }
                    }
                    if (typeSymbol != null) break;
                }
                if (typeSymbol != null) break;
            }

            // Extract fields from the type for context
            Dictionary<string, string>? fields = null;
            if (typeSymbol != null)
            {
                fields = new Dictionary<string, string>();
                foreach (var member in typeSymbol.GetMembers()
                    .Where(m => (m is IFieldSymbol f && !f.IsImplicitlyDeclared) ||
                                (m is IPropertySymbol p && !p.IsImplicitlyDeclared))
                    .OrderBy(m => m.Name))
                {
                    if (member is IFieldSymbol field)
                        fields[field.Name] = field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    else if (member is IPropertySymbol prop)
                        fields[prop.Name] = prop.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                }
            }

            // Generate with greedy decoding (temperature=0) for deterministic output
            var sharpOps = service.GenerateSharpOps(
                memberSignature,
                fields?.Count > 0 ? fields : null,
                comment,
                temperature: 0f,
                topP: 0.9f,
                maxTokens: 512);

            // Try to compile to C#
            try
            {
                var sequence = SharpOps.SharpOpsSequence.ParseOps(sharpOps, null);

                if (typeSymbol != null)
                {
                    PopulateSymbolTablesFromType(sequence, memberSignature, typeSymbol);
                }
                else
                {
                    PopulateSymbolTablesFromContext(sequence, memberSignature, fields);
                }

                var compiledBody = SharpOps.SharpOpsCompiler.CompileToString(sequence);
                var fullCode = memberSignature + "\n" + compiledBody;

                // Validate: parse the generated code to ensure it's valid C#
                var wrappedCode = $"class _Validate {{ {fullCode} }}";
                var parseTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(wrappedCode);
                var parseErrors = parseTree.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToList();

                if (parseErrors.Count > 0)
                {
                    var stubCode = memberSignature + "\n{ throw new NotImplementedException(); }";
                    return (stubCode, true);
                }

                return (fullCode, false);
            }
            catch
            {
                var stubCode = memberSignature + "\n{ throw new NotImplementedException(); }";
                return (stubCode, true);
            }
        }
        catch
        {
            var stubCode = memberSignature + "\n{ throw new NotImplementedException(); }";
            return (stubCode, true);
        }
    }

    public Task CollectFinetuneDataAsync(
        string solutionPath, string filePath, string typeName,
        string methodName, string? comment, string? parameterTypes)
    {
        return FinetuneCollector.CollectAsync(solutionPath, filePath, typeName, methodName, comment, parameterTypes);
    }

    public void RegisterTools(McpServer server)
    {
        CodeGenPluginFinetune.Register(this, server);
    }

    private static void PopulateSymbolTablesFromContext(
        SharpOps.SharpOpsSequence sequence,
        string methodSignature,
        Dictionary<string, string>? fields)
    {
        var parenStart = methodSignature.IndexOf('(');
        var parenEnd = methodSignature.LastIndexOf(')');
        if (parenStart >= 0 && parenEnd > parenStart)
        {
            var paramSection = methodSignature[(parenStart + 1)..parenEnd].Trim();
            if (paramSection.Length > 0)
            {
                var paramTable = sequence.SymbolTables[RoslynSymbolKind.Parameter];
                foreach (var param in paramSection.Split(','))
                {
                    var parts = param.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        paramTable.Add(parts[^1]);
                    }
                }
            }
        }

        if (fields != null && fields.Count > 0)
        {
            var fieldTable = sequence.SymbolTables[RoslynSymbolKind.Field];
            foreach (var name in fields.Keys.OrderBy(k => k))
            {
                fieldTable.Add(name);
            }
        }
    }

    private static void PopulateSymbolTablesFromType(
        SharpOps.SharpOpsSequence sequence,
        string methodSignature,
        INamedTypeSymbol typeSymbol)
    {
        var parenStart = methodSignature.IndexOf('(');
        var parenEnd = methodSignature.LastIndexOf(')');
        if (parenStart >= 0 && parenEnd > parenStart)
        {
            var paramSection = methodSignature[(parenStart + 1)..parenEnd].Trim();
            if (paramSection.Length > 0)
            {
                var paramTable = sequence.SymbolTables[RoslynSymbolKind.Parameter];
                foreach (var param in paramSection.Split(','))
                {
                    var parts = param.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        paramTable.Add(parts[^1]);
                    }
                }
            }
        }

        var fieldTable = sequence.SymbolTables[RoslynSymbolKind.Field];
        foreach (var member in typeSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => !f.IsImplicitlyDeclared)
            .OrderBy(f => f.Name))
        {
            fieldTable.Add(member.Name);
        }

        var propTable = sequence.SymbolTables[RoslynSymbolKind.Property];
        foreach (var member in typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsImplicitlyDeclared)
            .OrderBy(p => p.Name))
        {
            propTable.Add(member.Name);
        }

        var methodTable = sequence.SymbolTables[RoslynSymbolKind.Method];
        foreach (var name in typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == Microsoft.CodeAnalysis.MethodKind.Ordinary && !m.IsImplicitlyDeclared)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n))
        {
            methodTable.Add(name);
        }

        var typeTable = sequence.SymbolTables[RoslynSymbolKind.NamedType];
        typeTable.Add(typeSymbol.Name);
    }
}
