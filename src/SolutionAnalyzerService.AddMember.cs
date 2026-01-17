using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{


    private static void RegisterFailureHandler(MSBuildWorkspace workspace)
    {
        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            Console.Error.WriteLine($"Workspace warning: {args.Diagnostic.Message}");
        }, null);
    }     /// <summary>
          /// Adds a new member (method, property, field, etc.) to a type.
          /// </summary>
    public async Task<AddMemberResult> AddMemberAsync(
        string solutionPath,
        string typeName,
        string memberCode,
        string? insertionPoint = null)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new AddMemberResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        using var workspace = MSBuildWorkspace.Create();
        RegisterFailureHandler(workspace);

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Find the type by name
            var typeSymbols = await SymbolFinder.FindSourceDeclarationsAsync(
                solution,
                name => name.Equals(typeName, StringComparison.Ordinal) ||
                        name.Equals(typeName, StringComparison.OrdinalIgnoreCase),
                SymbolFilter.Type);

            var targetType = typeSymbols
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault();

            if (targetType == null)
            {
                return new AddMemberResult
                {
                    Success = false,
                    Error = $"Type not found: {typeName}"
                };
            }

            // Get the syntax node for the type
            var location = targetType.Locations.FirstOrDefault(l => l.IsInSource);
            if (location == null)
            {
                return new AddMemberResult
                {
                    Success = false,
                    Error = "Type source location not found (may be in metadata)"
                };
            }

            var syntaxTree = location.SourceTree;
            if (syntaxTree == null)
            {
                return new AddMemberResult
                {
                    Success = false,
                    Error = "Could not get syntax tree for type"
                };
            }

            var root = await syntaxTree.GetRootAsync();
            var typeNode = root.FindNode(location.SourceSpan);

            // Navigate up to the full type declaration
            while (typeNode != null && typeNode is not TypeDeclarationSyntax)
            {
                typeNode = typeNode.Parent;
            }

            if (typeNode is not TypeDeclarationSyntax typeDeclaration)
            {
                return new AddMemberResult
                {
                    Success = false,
                    Error = "Could not locate type declaration syntax node"
                };
            }

            // Parse the new member code
            var parsedMember = ParseMemberCode(memberCode);
            if (parsedMember == null)
            {
                return new AddMemberResult
                {
                    Success = false,
                    Error = "Could not parse member code. Ensure it's a valid method, property, field, or event declaration."
                };
            }

            // Determine insertion point and add the member
            var (newTypeDeclaration, insertedMember) = InsertMember(typeDeclaration, parsedMember, insertionPoint);

            // Replace the type declaration in the syntax tree
            var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration);

            // Format the modified document using Roslyn's Formatter
            var formattedRoot = Formatter.Format(newRoot, workspace);

            // Write the updated file
            var filePath = syntaxTree.FilePath;
            var newText = formattedRoot.ToFullString();
            await File.WriteAllTextAsync(filePath, newText);

            // Get info about the inserted member
            var (memberName, memberKind, signature) = GetMemberInfo(insertedMember);

            // Calculate the line number where the member was inserted
            var insertedInNewRoot = newRoot.DescendantNodes()
                .OfType<MemberDeclarationSyntax>()
                .FirstOrDefault(m => m.ToString().Trim() == insertedMember.ToString().Trim());

            var insertedLine = insertedInNewRoot?.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            Console.Error.WriteLine($"Added {memberKind} '{memberName}' to {typeName} at {filePath}:{insertedLine}");

            return new AddMemberResult
            {
                Success = true,
                FilePath = filePath,
                TypeName = targetType.ToDisplayString(),
                MemberName = memberName,
                MemberKind = memberKind,
                InsertedAtLine = insertedLine,
                Signature = signature
            };
        }
        catch (Exception ex)
        {
            return new AddMemberResult
            {
                Success = false,
                Error = $"Failed to add member: {ex.Message}"
            };
        }
    }

    private static MemberDeclarationSyntax? ParseMemberCode(string memberCode)
    {
        // Try parsing as a class member by wrapping in a temp class
        var wrappedCode = $"class Temp {{ {memberCode} }}";
        var tree = CSharpSyntaxTree.ParseText(wrappedCode);
        var root = tree.GetRoot();

        // Check for parse errors
        var diagnostics = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (diagnostics.Count > 0)
        {
            Console.Error.WriteLine($"Parse errors: {string.Join(", ", diagnostics.Select(d => d.GetMessage()))}");
            return null;
        }

        // Find the member in the temp class
        var tempClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        return tempClass?.Members.FirstOrDefault();
    }

    private static (TypeDeclarationSyntax newType, MemberDeclarationSyntax insertedMember) InsertMember(
        TypeDeclarationSyntax typeDeclaration,
        MemberDeclarationSyntax newMember,
        string? insertionPoint)
    {
        // Add a blank line before the member (Formatter will handle indentation)
        var formattedMember = newMember
            .WithLeadingTrivia(SyntaxFactory.TriviaList(
                SyntaxFactory.CarriageReturnLineFeed,
                SyntaxFactory.CarriageReturnLineFeed));

        var members = typeDeclaration.Members;
        int insertIndex = members.Count; // Default: end

        if (!string.IsNullOrEmpty(insertionPoint))
        {
            insertIndex = insertionPoint.ToLowerInvariant() switch
            {
                "start" => 0,
                "end" => members.Count,
                "after-fields" => GetInsertIndexAfterFields(members),
                "after-constructors" => GetInsertIndexAfterConstructors(members),
                "after-properties" => GetInsertIndexAfterProperties(members),
                "before-methods" => GetInsertIndexBeforeMethods(members),
                _ => members.Count
            };
        }
        else
        {
            // Smart default: insert based on member type
            insertIndex = GetSmartInsertIndex(members, newMember);
        }

        var newMembers = members.Insert(insertIndex, formattedMember);
        var newType = typeDeclaration.WithMembers(newMembers);

        return (newType, formattedMember);
    }

    private static int GetSmartInsertIndex(SyntaxList<MemberDeclarationSyntax> members, MemberDeclarationSyntax newMember)
    {
        // Order: Fields -> Constructors -> Properties -> Methods -> Events
        // Insert the new member with its peers, at the end of that group

        return newMember switch
        {
            FieldDeclarationSyntax => GetInsertIndexAfterFields(members),
            ConstructorDeclarationSyntax => GetInsertIndexAfterConstructors(members),
            PropertyDeclarationSyntax => GetInsertIndexAfterProperties(members),
            MethodDeclarationSyntax => members.Count, // Methods go at end
            EventDeclarationSyntax or EventFieldDeclarationSyntax => members.Count,
            _ => members.Count
        };
    }

    private static int GetInsertIndexAfterFields(SyntaxList<MemberDeclarationSyntax> members)
    {
        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (members[i] is FieldDeclarationSyntax)
                return i + 1;
        }
        return 0; // No fields, insert at start
    }

    private static int GetInsertIndexAfterConstructors(SyntaxList<MemberDeclarationSyntax> members)
    {
        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (members[i] is ConstructorDeclarationSyntax)
                return i + 1;
        }
        return GetInsertIndexAfterFields(members); // No constructors, after fields
    }

    private static int GetInsertIndexAfterProperties(SyntaxList<MemberDeclarationSyntax> members)
    {
        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (members[i] is PropertyDeclarationSyntax)
                return i + 1;
        }
        return GetInsertIndexAfterConstructors(members); // No properties, after constructors
    }

    private static int GetInsertIndexBeforeMethods(SyntaxList<MemberDeclarationSyntax> members)
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] is MethodDeclarationSyntax)
                return i;
        }
        return members.Count; // No methods, insert at end
    }

    private static (string name, string kind, string signature) GetMemberInfo(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax m => (
                m.Identifier.Text,
                "Method",
                $"{m.ReturnType} {m.Identifier}({string.Join(", ", m.ParameterList.Parameters)})"
            ),
            PropertyDeclarationSyntax p => (
                p.Identifier.Text,
                "Property",
                $"{p.Type} {p.Identifier}"
            ),
            FieldDeclarationSyntax f => (
                f.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "unknown",
                "Field",
                $"{f.Declaration.Type} {f.Declaration.Variables.FirstOrDefault()?.Identifier.Text}"
            ),
            ConstructorDeclarationSyntax c => (
                c.Identifier.Text,
                "Constructor",
                $"{c.Identifier}({string.Join(", ", c.ParameterList.Parameters)})"
            ),
            EventDeclarationSyntax e => (
                e.Identifier.Text,
                "Event",
                $"event {e.Type} {e.Identifier}"
            ),
            EventFieldDeclarationSyntax ef => (
                ef.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "unknown",
                "Event",
                $"event {ef.Declaration.Type} {ef.Declaration.Variables.FirstOrDefault()?.Identifier.Text}"
            ),
            _ => ("unknown", "Member", member.ToString().Split('\n')[0].Trim())
        };
    }


    public static HashSet<string> GetFixableDiagnosticIds()
    {
        var providers = GetCodeFixProviders();
        var fixableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            foreach (var id in provider.FixableDiagnosticIds)
            {
                fixableIds.Add(id);
            }
        }
        return fixableIds;
    }
}
