using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Creates a new type (class, interface, struct, record, enum) in a project.
    /// </summary>
    public async Task<AddTypeResult> AddTypeAsync(
        string solutionPath,
        string projectName,
        string typeName,
        string? typeKind = null,
        string? ns = null,
        string? folder = null,
        string? accessibility = null,
        string? baseTypes = null,
        bool isPartial = false,
        bool isSealed = false,
        bool isStatic = false)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new AddTypeResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        // Validate type name
        if (!SyntaxFacts.IsValidIdentifier(typeName))
        {
            return new AddTypeResult
            {
                Success = false,
                Error = $"Invalid type name: {typeName}"
            };
        }

        using var workspace = CreateWorkspace();

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Find the project
            var project = solution.Projects.FirstOrDefault(p =>
                p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                var availableProjects = string.Join(", ", solution.Projects.Select(p => p.Name));
                return new AddTypeResult
                {
                    Success = false,
                    Error = $"Project not found: {projectName}. Available projects: {availableProjects}"
                };
            }

            if (string.IsNullOrEmpty(project.FilePath))
            {
                return new AddTypeResult
                {
                    Success = false,
                    Error = "Project file path is not available"
                };
            }

            // Determine the project directory
            var projectDir = Path.GetDirectoryName(project.FilePath)!;

            // Determine target directory (project root + folder)
            var targetDir = projectDir;
            if (!string.IsNullOrEmpty(folder))
            {
                // Normalize folder separators
                folder = folder.Replace('/', Path.DirectorySeparatorChar)
                               .Replace('\\', Path.DirectorySeparatorChar);
                targetDir = Path.Combine(projectDir, folder);
            }

            // Create directory if it doesn't exist
            Directory.CreateDirectory(targetDir);

            // Determine namespace
            var targetNamespace = ns;
            if (string.IsNullOrEmpty(targetNamespace))
            {
                // Default: project name + folder path segments
                targetNamespace = project.Name;
                if (!string.IsNullOrEmpty(folder))
                {
                    var folderParts = folder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    targetNamespace = string.Join(".", new[] { project.Name }.Concat(folderParts));
                }
            }

            // Determine file path
            var filePath = Path.Combine(targetDir, $"{typeName}.cs");

            // Check if file already exists
            if (File.Exists(filePath))
            {
                return new AddTypeResult
                {
                    Success = false,
                    Error = $"File already exists: {filePath}"
                };
            }

            // Generate the type declaration
            var typeDeclaration = GenerateTypeDeclaration(
                typeName,
                typeKind ?? "class",
                accessibility ?? "public",
                baseTypes,
                isPartial,
                isSealed,
                isStatic);

            if (typeDeclaration == null)
            {
                return new AddTypeResult
                {
                    Success = false,
                    Error = $"Invalid type kind: {typeKind}. Valid kinds: class, interface, struct, record, enum"
                };
            }

            // Create the compilation unit with namespace and type
            var namespaceDeclaration = SyntaxFactory.FileScopedNamespaceDeclaration(
                SyntaxFactory.ParseName(targetNamespace))
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typeDeclaration))
                .WithNamespaceKeyword(
                    SyntaxFactory.Token(SyntaxKind.NamespaceKeyword)
                        .WithTrailingTrivia(SyntaxFactory.Space))
                .WithSemicolonToken(
                    SyntaxFactory.Token(SyntaxKind.SemicolonToken)
                        .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed, SyntaxFactory.CarriageReturnLineFeed));

            var compilationUnit = SyntaxFactory.CompilationUnit()
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(namespaceDeclaration))
                .NormalizeWhitespace();

            // Format the code
            var formattedNode = Formatter.Format(compilationUnit, workspace);
            var sourceCode = formattedNode.ToFullString();

            // Write the file
            await File.WriteAllTextAsync(filePath, sourceCode);

            var fullyQualifiedName = $"{targetNamespace}.{typeName}";
            Console.Error.WriteLine($"Created {typeKind ?? "class"} '{fullyQualifiedName}' at {filePath}");

            return new AddTypeResult
            {
                Success = true,
                FilePath = filePath,
                TypeName = typeName,
                FullyQualifiedName = fullyQualifiedName,
                Namespace = targetNamespace,
                TypeKind = typeKind ?? "class",
                ProjectName = project.Name
            };
        }
        catch (Exception ex)
        {
            return new AddTypeResult
            {
                Success = false,
                Error = $"Failed to create type: {ex.Message}"
            };
        }
    }

    private static BaseTypeDeclarationSyntax? GenerateTypeDeclaration(
        string typeName,
        string typeKind,
        string accessibility,
        string? baseTypes,
        bool isPartial,
        bool isSealed,
        bool isStatic)
    {
        // Build modifiers
        var modifiers = new List<SyntaxToken>();

        // Accessibility
        modifiers.Add(accessibility.ToLowerInvariant() switch
        {
            "public" => SyntaxFactory.Token(SyntaxKind.PublicKeyword),
            "internal" => SyntaxFactory.Token(SyntaxKind.InternalKeyword),
            "private" => SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
            "protected" => SyntaxFactory.Token(SyntaxKind.ProtectedKeyword),
            _ => SyntaxFactory.Token(SyntaxKind.PublicKeyword)
        });

        // Static (only for classes)
        if (isStatic && typeKind.ToLowerInvariant() == "class")
        {
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
        }

        // Sealed (only for classes and records)
        if (isSealed && (typeKind.ToLowerInvariant() == "class" || typeKind.ToLowerInvariant() == "record"))
        {
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.SealedKeyword));
        }

        // Partial
        if (isPartial)
        {
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword));
        }

        var modifierList = SyntaxFactory.TokenList(modifiers);

        // Parse base types if provided
        BaseListSyntax? baseList = null;
        if (!string.IsNullOrWhiteSpace(baseTypes))
        {
            var baseTypeNames = baseTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var baseTypeSyntaxes = baseTypeNames
                .Select(name => SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(name)))
                .Cast<BaseTypeSyntax>()
                .ToArray();

            if (baseTypeSyntaxes.Length > 0)
            {
                baseList = SyntaxFactory.BaseList(SyntaxFactory.SeparatedList(baseTypeSyntaxes));
            }
        }

        // Create the type declaration based on kind
        var identifier = SyntaxFactory.Identifier(typeName);

        return typeKind.ToLowerInvariant() switch
        {
            "class" => SyntaxFactory.ClassDeclaration(identifier)
                .WithModifiers(modifierList)
                .WithBaseList(baseList)
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)),

            "interface" => SyntaxFactory.InterfaceDeclaration(identifier)
                .WithModifiers(modifierList)
                .WithBaseList(baseList)
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)),

            "struct" => SyntaxFactory.StructDeclaration(identifier)
                .WithModifiers(modifierList)
                .WithBaseList(baseList)
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)),

            "record" => SyntaxFactory.RecordDeclaration(SyntaxKind.RecordDeclaration, SyntaxFactory.Token(SyntaxKind.RecordKeyword), identifier)
                .WithModifiers(modifierList)
                .WithBaseList(baseList)
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)),

            "enum" => CreateEnumDeclaration(identifier, modifierList, baseList),

            _ => null
        };
    }

    private static EnumDeclarationSyntax CreateEnumDeclaration(
        SyntaxToken identifier,
        SyntaxTokenList modifiers,
        BaseListSyntax? baseList)
    {
        // Enums can only have a single base type (the underlying type)
        BaseListSyntax? enumBaseList = null;
        if (baseList?.Types.Count == 1)
        {
            enumBaseList = baseList;
        }

        return SyntaxFactory.EnumDeclaration(identifier)
            .WithModifiers(modifiers)
            .WithBaseList(enumBaseList)
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken));
    }
}
