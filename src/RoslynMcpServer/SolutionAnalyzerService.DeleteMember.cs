using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;

namespace RoslynMcpServer;

/// <summary>
/// Delete member functionality.
/// Issue: #39
/// </summary>
public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Deletes a member (method, property, field) from a type.
    /// </summary>
    public static async Task<DeleteMemberResult> DeleteMemberAsync(
        string solutionPath,
        string typeName,
        string memberName,
        string? memberKind = null,
        string? parameterTypes = null)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new DeleteMemberResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        using var workspace = CreateWorkspace();

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
                return new DeleteMemberResult
                {
                    Success = false,
                    Error = $"Type not found: {typeName}"
                };
            }

            // Get the syntax node for the type
            var location = targetType.Locations.FirstOrDefault(l => l.IsInSource);
            if (location == null)
            {
                return new DeleteMemberResult
                {
                    Success = false,
                    Error = "Type source location not found (may be in metadata)"
                };
            }

            var syntaxTree = location.SourceTree;
            if (syntaxTree == null)
            {
                return new DeleteMemberResult
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
                return new DeleteMemberResult
                {
                    Success = false,
                    Error = "Could not locate type declaration syntax node"
                };
            }

            // Find the member to delete
            var (memberToDelete, foundKind, signature) = FindMemberToDelete(
                typeDeclaration, memberName, memberKind, parameterTypes);

            if (memberToDelete == null)
            {
                var error = $"Member '{memberName}' not found in type '{typeName}'";
                if (!string.IsNullOrEmpty(memberKind))
                    error += $" with kind '{memberKind}'";
                if (!string.IsNullOrEmpty(parameterTypes))
                    error += $" with parameters '({parameterTypes})'";

                return new DeleteMemberResult
                {
                    Success = false,
                    Error = error
                };
            }

            // Get the line number before deletion
            var deletedLine = memberToDelete.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            // Remove the member, including leading trivia (attributes, XML docs, blank lines)
            var memberWithTrivia = GetMemberWithLeadingTrivia(memberToDelete, typeDeclaration);
            var newTypeDeclaration = typeDeclaration.RemoveNode(memberWithTrivia, SyntaxRemoveOptions.KeepNoTrivia);

            if (newTypeDeclaration == null)
            {
                return new DeleteMemberResult
                {
                    Success = false,
                    Error = "Failed to remove member from type"
                };
            }

            // Replace the type declaration in the syntax tree
            var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration);

            // Format the modified document
            var formattedRoot = Formatter.Format(newRoot, workspace);

            // Write the updated file
            var filePath = syntaxTree.FilePath;
            var newText = formattedRoot.ToFullString();
            await File.WriteAllTextAsync(filePath, newText);

            Console.Error.WriteLine($"Deleted {foundKind} '{memberName}' from {typeName} at {filePath}:{deletedLine}");

            return new DeleteMemberResult
            {
                Success = true,
                FilePath = filePath,
                TypeName = targetType.ToDisplayString(),
                MemberName = memberName,
                MemberKind = foundKind,
                DeletedAtLine = deletedLine,
                Signature = signature
            };
        }
        catch (Exception ex)
        {
            return new DeleteMemberResult
            {
                Success = false,
                Error = $"Failed to delete member: {ex.Message}"
            };
        }
    }

    private static (MemberDeclarationSyntax? member, string kind, string signature) FindMemberToDelete(
        TypeDeclarationSyntax typeDeclaration,
        string memberName,
        string? memberKind,
        string? parameterTypes)
    {
        var candidates = new List<(MemberDeclarationSyntax member, string kind, string signature)>();

        foreach (var member in typeDeclaration.Members)
        {
            var (name, kind, sig) = GetMemberInfo(member);

            if (!name.Equals(memberName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Filter by kind if specified
            if (!string.IsNullOrEmpty(memberKind))
            {
                var kindMatches = memberKind.ToLowerInvariant() switch
                {
                    "method" => member is MethodDeclarationSyntax,
                    "property" => member is PropertyDeclarationSyntax,
                    "field" => member is FieldDeclarationSyntax,
                    _ => true
                };

                if (!kindMatches) continue;
            }

            // Filter by parameter types if specified (for method overloads)
            if (!string.IsNullOrEmpty(parameterTypes) && member is MethodDeclarationSyntax method)
            {
                var actualParams = GetParameterTypesString(method);
                if (!actualParams.Equals(parameterTypes.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            candidates.Add((member, kind, sig));
        }

        if (candidates.Count == 0)
            return (null, "", "");

        if (candidates.Count == 1)
            return candidates[0];

        // Multiple candidates - need disambiguation
        // Return null to force user to specify memberKind or parameterTypes
        Console.Error.WriteLine($"Multiple members named '{memberName}' found. Specify memberKind or parameterTypes to disambiguate.");
        return (null, "", "");
    }

    private static string GetParameterTypesString(MethodDeclarationSyntax method)
    {
        return string.Join(",", method.ParameterList.Parameters.Select(p =>
        {
            // Get the type name, simplifying generic types
            var typeName = p.Type?.ToString() ?? "";
            return typeName.Replace(" ", "");
        }));
    }

    private static MemberDeclarationSyntax GetMemberWithLeadingTrivia(
        MemberDeclarationSyntax member,
        TypeDeclarationSyntax typeDeclaration)
    {
        // The member itself already includes its attributes and XML docs in the syntax node
        // We just need to return the member as-is; Roslyn's RemoveNode handles trivia
        return member;
    }
}

/// <summary>
/// Result of deleting a member.
/// </summary>
public class DeleteMemberResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? FilePath { get; init; }
    public string? TypeName { get; init; }
    public string? MemberName { get; init; }
    public string? MemberKind { get; init; }
    public int? DeletedAtLine { get; init; }
    public string? Signature { get; init; }
}
