using RoslynSymbolKind = Microsoft.CodeAnalysis.SymbolKind;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynMcpServer.Graph;

namespace RoslynMcpServer;

public static partial class RoslynTools
{
    private static readonly string[] definition = new[] { "start", "end", "after-fields", "after-constructors", "after-properties", "before-methods" };
    private static readonly string[] definitionArray = new[] { "solutionPath", "typeName", "memberCode" };

    /// <summary>
    /// Adds a new member (method, property, field) to a type.
    /// </summary>
    private static void RegisterAddMemberTool(McpServer server)
    {
        server.RegisterTool(
            "AddMember",
            new ToolDefinition
            {
                Description = "Adds a new member (method, property, field, constructor, event) to a type. Uses Roslyn to parse and insert the member with proper formatting. Smart insertion places members with their peers (fields together, methods together, etc.). For methods, set auto=true and provide only the method signature as memberCode — the built-in SharpTinyCoder model will generate the body. If generation fails, a NotImplementedException stub is inserted; use UpdateMethod to provide your implementation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new
                        {
                            type = "string",
                            description = "Name of the type (class, struct, interface) to add the member to"
                        },
                        memberCode = new
                        {
                            type = "string",
                            description = "The complete source code of the member to add (method, property, field, etc.)"
                        },
                        insertionPoint = new
                        {
                            type = "string",
                            description = "Where to insert: 'start', 'end', 'after-fields', 'after-constructors', 'after-properties', 'before-methods'. Default: smart placement based on member type.",
                            @enum = new[] { "start", "end", "after-fields", "after-constructors", "after-properties", "before-methods" }
                        },
                        comment = new
                        {
                            type = "string",
                            description = "Plain text description of the member. Generates XML doc comment with <summary>, <param>, and <returns> tags. Public members get stub XML docs even without this parameter."
                        },
                        auto = new
                        {
                            type = "boolean",
                            description = "When true, memberCode should be just the method signature (no body). The built-in SharpTinyCoder model generates the body automatically. If generation fails, a NotImplementedException stub is inserted. Always try auto=true first for methods."
                        }
                    },
                    required = new[] { "typeName", "memberCode" }
                },
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    IdempotentHint = false
                }
            },
            async args =>
            {
                var (solutionPath, solutionError) = GetSolutionPathOrError();
                if (solutionError != null) return solutionError;

                var typeName = args?["typeName"]?.GetValue<string>();
                var memberCode = args?["memberCode"]?.GetValue<string>();
                var insertionPoint = args?["insertionPoint"]?.GetValue<string>();
                var comment = args?["comment"]?.GetValue<string>();
                var auto = GetOptionalBool(args, "auto", false);

                if (string.IsNullOrWhiteSpace(typeName))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: typeName is required" }
                        },
                        isError = false
                    };
                }

                if (string.IsNullOrWhiteSpace(memberCode))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: memberCode is required" }
                        },
                        isError = false
                    };
                }

                // Auto-generate mode: memberCode is just the signature, model generates body
                bool autoGenerationFailed = false;
                string? generatedCode = null;
                if (auto)
                {
                    var autoResult = await HandleAutoGenerateAsync(solutionPath!, typeName, memberCode, comment);
                    memberCode = autoResult.FullMemberCode;
                    autoGenerationFailed = autoResult.Failed;
                    // Only return generatedCode when model succeeded — stub code is not useful
                    if (!autoGenerationFailed)
                    {
                        generatedCode = autoResult.FullMemberCode;
                    }
                }

                var result = await SolutionAnalyzerService.AddMemberAsync(
                    solutionPath!,
                    typeName,
                    memberCode,
                    insertionPoint,
                    comment,
                    skipFinetuneCollection: auto);

                // Enrich result with auto-generation info
                if (auto)
                {
                    result = new AddMemberResult
                    {
                        Success = result.Success,
                        Error = result.Error,
                        FilePath = result.FilePath,
                        TypeName = result.TypeName,
                        MemberName = result.MemberName,
                        MemberKind = result.MemberKind,
                        InsertedAtLine = result.InsertedAtLine,
                        Signature = result.Signature,
                        AutoGenerated = true,
                        AutoGenerationFailed = autoGenerationFailed,
                        GeneratedCode = generatedCode
                    };
                }

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


    private static void WriteToken(string fileName, string token)
    {
        try
        {
            // Write to exe directory (.roslyn-mcp/ folder)
            var tokenFile = Path.Combine(AppContext.BaseDirectory, fileName);
            File.WriteAllText(tokenFile, token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not write token file {fileName}: {ex.Message}");
        }
    }


    private static string UpdateGitignore(string gitignorePath)
    {
        var entriesToAdd = new List<string>();
        var existingContent = "";

        if (File.Exists(gitignorePath))
        {
            existingContent = File.ReadAllText(gitignorePath);
            var lines = existingContent.Split('\n').Select(l => l.Trim()).ToHashSet();

            // Check if entries already exist (with or without trailing slash)
            if (!lines.Contains(".claude") && !lines.Contains(".claude/"))
            {
                entriesToAdd.Add(".claude/");
            }
            if (!lines.Contains(".roslyn-mcp") && !lines.Contains(".roslyn-mcp/"))
            {
                entriesToAdd.Add(".roslyn-mcp/");
            }

            if (entriesToAdd.Count == 0)
            {
                return "skipped (entries already present)";
            }

            // Append new entries
            var newContent = existingContent.TrimEnd();
            if (!newContent.EndsWith("\n"))
            {
                newContent += "\n";
            }
            newContent += "\n# Claude Code and Roslyn MCP (local configuration)\n";
            newContent += string.Join("\n", entriesToAdd) + "\n";
            File.WriteAllText(gitignorePath, newContent);
            return "updated";
        }
        else
        {
            // Create new .gitignore with entries
            var content = "# Claude Code and Roslyn MCP (local configuration)\n.claude/\n.roslyn-mcp/\n";
            File.WriteAllText(gitignorePath, content);
            return "created";
        }
    }


    private static (string? Path, object? Error) GetSolutionPathOrError()
    {
        if (string.IsNullOrEmpty(_solutionPath))
        {
            return (null, CreateErrorResponse("No solution detected. Run MCP server from solution directory."));
        }
        return (_solutionPath, null);
    }


    private static async Task<Microsoft.CodeAnalysis.SyntaxNode?> GetCachedSyntaxRootAsync(
            Microsoft.CodeAnalysis.Solution solution, string filePath)
    {
        try
        {
            var document = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == filePath);

            if (document == null) return null;

            return await document.GetSyntaxRootAsync();
        }
        catch
        {
            return null;
        }
    }


    private static bool HasAttributesInSyntaxRoot(Microsoft.CodeAnalysis.SyntaxNode syntaxRoot, int line)
    {
        try
        {
            // Find the node at the given line (0-based in Roslyn)
            var lineSpan = syntaxRoot.SyntaxTree.GetText().Lines[line - 1];
            var node = syntaxRoot.FindNode(lineSpan.Span);

            // Walk up to find a property declaration
            while (node != null)
            {
                if (node is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax propDecl)
                {
                    return propDecl.AttributeLists.Count > 0;
                }
                node = node.Parent;
            }

            return false;
        }
        catch
        {
            return false; // If we can't check, assume no attributes
        }
    }


    private static bool IsPureModelClassInSyntaxRoot(Microsoft.CodeAnalysis.SyntaxNode syntaxRoot, string typeName)
    {
        try
        {
            // Find the class declaration
            var classDecl = syntaxRoot.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == typeName);

            if (classDecl == null) return false;

            // Check members: a pure model class has only auto-properties (no backing logic)
            var members = classDecl.Members;

            foreach (var member in members)
            {
                switch (member)
                {
                    case Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax prop:
                        // Auto-properties have no body (just { get; set; })
                        if (prop.ExpressionBody != null) return false; // => expression body
                        if (prop.AccessorList != null)
                        {
                            foreach (var accessor in prop.AccessorList.Accessors)
                            {
                                // Auto-property accessors have no body
                                if (accessor.Body != null || accessor.ExpressionBody != null)
                                    return false;
                            }
                        }
                        break;

                    case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax:
                        // Methods (other than property accessors) disqualify pure model
                        return false;

                    case Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax ctor:
                        // Allow parameterless constructors with no/empty body
                        if (ctor.ParameterList.Parameters.Count > 0) return false;
                        if (ctor.Body != null && ctor.Body.Statements.Count > 0) return false;
                        if (ctor.ExpressionBody != null) return false;
                        break;

                    case Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax:
                        // Fields are OK (backing fields)
                        break;

                    case Microsoft.CodeAnalysis.CSharp.Syntax.IndexerDeclarationSyntax:
                    case Microsoft.CodeAnalysis.CSharp.Syntax.EventDeclarationSyntax:
                    case Microsoft.CodeAnalysis.CSharp.Syntax.OperatorDeclarationSyntax:
                        // These disqualify pure model
                        return false;
                }
            }

            return true; // Only auto-properties and allowed members
        }
        catch
        {
            return false; // If we can't check, assume not a pure model
        }
    }


    public class GraphFreshnessResult
    {
        public bool WasFresh { get; set; }
        public int StaleFilesFound { get; set; }
        public int FilesReanalyzed { get; set; }
        public List<string>? ReanalyzedFiles { get; set; }
        public string? Error { get; set; }
    }


    private static async Task<GraphFreshnessResult> EnsureGraphFreshAsync(
        GraphDatabase db,
        long solutionId,
        string solutionPath)
    {
        var result = new GraphFreshnessResult { WasFresh = true };

        var solutionDir = Path.GetDirectoryName(solutionPath);
        if (string.IsNullOrEmpty(solutionDir))
        {
            return result;
        }

        // Compute current file hashes using relative paths
        var currentFileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var absolutePath in Directory.EnumerateFiles(solutionDir, "*.cs", SearchOption.AllDirectories))
        {
            if (absolutePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                absolutePath.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
                absolutePath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            {
                continue;
            }

            var relativePath = absolutePath.Substring(solutionDir.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            try
            {
                currentFileHashes[relativePath] = GraphDatabase.ComputeFileHash(absolutePath);
            }
            catch
            {
                // Skip files that can't be read
            }
        }

        // Clean up symbols from deleted files
        var existingFiles = new HashSet<string>(currentFileHashes.Keys, StringComparer.OrdinalIgnoreCase);
        await db.CleanupSymbolsFromDeletedFilesAsync(solutionId, existingFiles);

        // Find stale files
        var staleFiles = await db.GetStaleSymbolFilesAsync(solutionId, currentFileHashes);

        var filesToRefresh = staleFiles
            .Where(f => currentFileHashes.ContainsKey(f))
            .ToList();

        if (filesToRefresh.Count == 0)
        {
            return result;
        }

        result.WasFresh = false;
        result.StaleFilesFound = filesToRefresh.Count;

        if (_analyzerService == null)
        {
            return result;
        }

        var roslynSolution = await SolutionAnalyzerService.LoadSolutionAsync(solutionPath);
        if (roslynSolution == null)
        {
            return result;
        }

        // Phase 1: Delete all stale symbols first.
        // Must happen before reanalysis to avoid _symbolCache returning
        // stale IDs that were deleted by a later iteration (FK violation).
        foreach (var relativePath in filesToRefresh)
        {
            await db.DeleteSymbolsByFileAsync(solutionId, relativePath);
        }

        // Phase 2: Reanalyze all stale files with clean state.
        var analyzer = new GraphAnalyzer(db);
        var reanalyzedFiles = new List<string>();

        foreach (var relativePath in filesToRefresh)
        {
            var absolutePath = Path.Combine(solutionDir, relativePath);
            var document = roslynSolution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => string.Equals(d.FilePath, absolutePath, StringComparison.OrdinalIgnoreCase));

            if (document != null)
            {
                await analyzer.AnalyzeDocumentAsync(document, solutionId, solutionDir);
                reanalyzedFiles.Add(Path.GetFileName(relativePath));
            }
        }

        result.FilesReanalyzed = reanalyzedFiles.Count;
        result.ReanalyzedFiles = reanalyzedFiles.Count > 0 ? reanalyzedFiles : null;

        return result;
    }


    private static async Task<Microsoft.CodeAnalysis.ISymbol?> FindSymbolByQualifiedNameAsync(
            Microsoft.CodeAnalysis.Solution solution, string qualifiedName)
    {
        // Parse the qualified name to extract type and member
        var lastDot = qualifiedName.LastIndexOf('.');
        if (lastDot < 0) return null;

        var memberName = qualifiedName.Substring(lastDot + 1);
        var containingTypeName = qualifiedName.Substring(0, lastDot);

        // Handle method parameters in qualified name (e.g., "Type.Method(param1, param2)")
        var parenIndex = memberName.IndexOf('(');
        if (parenIndex > 0)
        {
            memberName = memberName.Substring(0, parenIndex);
        }

        // Find the containing type first
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            // Try to find the type
            var typeSymbol = compilation.GetTypeByMetadataName(containingTypeName);
            if (typeSymbol == null)
            {
                // Try with nested type format
                typeSymbol = compilation.GetTypeByMetadataName(containingTypeName.Replace('.', '+'));
            }

            if (typeSymbol != null)
            {
                // Find the member in the type
                var members = typeSymbol.GetMembers(memberName);
                if (members.Length > 0)
                {
                    return members[0]; // Return first match
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Populate symbol tables on a SharpOpsSequence from context available during generation. Parameters are extracted from the method signature, fields from the fields dictionary.
    /// </summary>
    /// <param name="sequence"></param>
    /// <param name="methodSignature"></param>
    /// <param name="fields"></param>

    private static void PopulateSymbolTablesFromContext(
        SharpOps.SharpOpsSequence sequence,
        string methodSignature,
        Dictionary<string, string>? fields)
    {
        // Extract parameter names from signature: "public int Add(int a, int b)" -> ["a", "b"]
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

        // Fields: ordered by name to match ContextExtractor.ExtractUsedFields ordering
        if (fields != null && fields.Count > 0)
        {
            var fieldTable = sequence.SymbolTables[RoslynSymbolKind.Field];
            foreach (var name in fields.Keys.OrderBy(k => k))
            {
                fieldTable.Add(name);
            }
        }
    }

    /// <summary>
    /// Populate symbol tables from a full type symbol (used by SmartGenerateMethod).
    /// </summary>
    /// <param name="sequence"></param>
    /// <param name="methodSignature"></param>
    /// <param name="typeSymbol"></param>

    private static void PopulateSymbolTablesFromType(
        SharpOps.SharpOpsSequence sequence,
        string methodSignature,
        INamedTypeSymbol typeSymbol)
    {
        // Parameters from signature
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

        // Fields: ordered by name to match ContextExtractor ordering
        var fieldTable = sequence.SymbolTables[RoslynSymbolKind.Field];
        foreach (var member in typeSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => !f.IsImplicitlyDeclared)
            .OrderBy(f => f.Name))
        {
            fieldTable.Add(member.Name);
        }

        // Properties: ordered by name
        var propTable = sequence.SymbolTables[RoslynSymbolKind.Property];
        foreach (var member in typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsImplicitlyDeclared)
            .OrderBy(p => p.Name))
        {
            propTable.Add(member.Name);
        }

        // Methods: ordered by name
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

        // Named types: add containing type
        var typeTable = sequence.SymbolTables[RoslynSymbolKind.NamedType];
        typeTable.Add(typeSymbol.Name);
    }

    private static async Task<(string FullMemberCode, bool Failed)> HandleAutoGenerateAsync(
        string solutionPath,
        string typeName,
        string memberSignature,
        string? comment)
    {
        // Try to load the model
        var service = GetSharpOpsService();
        if (service == null)
        {
            var stubCode = memberSignature + "\n{ throw new NotImplementedException(); }";
            return (stubCode, true);
        }

        try
        {
            // Load solution and find the type
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
                // Sort alphabetically to match ContextExtractor and PopulateSymbolTablesFromType ordering
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
}
