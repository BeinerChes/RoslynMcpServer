using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Finds all implementations of an interface or derived classes of a base class.
    /// </summary>
    public async Task<FindImplementationsResult> FindImplementationsAsync(
        string solutionPath,
        string typeName,
        bool includeBaseType = false,
        int maxResults = 100)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new FindImplementationsResult
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
                return new FindImplementationsResult
                {
                    Success = false,
                    Error = $"Type not found: {typeName}. Try using the exact type name."
                };
            }

            Console.Error.WriteLine($"Finding implementations of: {targetType.ToDisplayString()}");

            var implementations = new List<ImplementationInfo>();

            // Add the base type itself if requested
            if (includeBaseType)
            {
                var location = targetType.Locations.FirstOrDefault();
                var lineSpan = location?.GetLineSpan();

                implementations.Add(new ImplementationInfo
                {
                    Name = targetType.Name,
                    FullyQualifiedName = targetType.ToDisplayString(),
                    Kind = targetType.TypeKind.ToString(),
                    FilePath = lineSpan?.Path,
                    Line = lineSpan?.StartLinePosition.Line + 1,
                    Column = lineSpan?.StartLinePosition.Character + 1,
                    IsBaseType = true,
                    BaseTypes = [],
                    Interfaces = targetType.AllInterfaces
                        .Select(i => i.ToDisplayString())
                        .ToList()
                });
            }

            if (targetType.TypeKind == TypeKind.Interface)
            {
                // Find all implementations of the interface
                var implSymbols = await SymbolFinder.FindImplementationsAsync(
                    targetType, solution);

                foreach (var impl in implSymbols.OfType<INamedTypeSymbol>().Take(maxResults))
                {
                    implementations.Add(CreateImplementationInfo(impl, targetType));
                }
            }
            else if (targetType.TypeKind == TypeKind.Class)
            {
                // Find all derived classes
                var derivedClasses = await SymbolFinder.FindDerivedClassesAsync(
                    targetType, solution);

                foreach (var derived in derivedClasses.Take(maxResults))
                {
                    implementations.Add(CreateImplementationInfo(derived, targetType));
                }
            }

            // Sort by name
            implementations = implementations
                .OrderBy(i => i.IsBaseType ? 0 : 1)
                .ThenBy(i => i.Name)
                .ToList();

            Console.Error.WriteLine($"Found {implementations.Count} implementations");

            return new FindImplementationsResult
            {
                Success = true,
                SolutionPath = solutionPath,
                BaseType = new TypeInfo
                {
                    Name = targetType.Name,
                    FullyQualifiedName = targetType.ToDisplayString(),
                    Kind = targetType.TypeKind.ToString()
                },
                TotalFound = implementations.Count,
                Implementations = implementations.Take(maxResults).ToList()
            };
        }
        catch (Exception ex)
        {
            return new FindImplementationsResult
            {
                Success = false,
                Error = $"Failed to find implementations: {ex.Message}"
            };
        }
    }

    private static ImplementationInfo CreateImplementationInfo(
        INamedTypeSymbol type,
        INamedTypeSymbol targetType)
    {
        var location = type.Locations.FirstOrDefault();
        var lineSpan = location?.GetLineSpan();

        // Get the inheritance chain up to (but not including) System.Object
        var baseTypes = new List<string>();
        var current = type.BaseType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            baseTypes.Add(current.ToDisplayString());
            current = current.BaseType;
        }

        return new ImplementationInfo
        {
            Name = type.Name,
            FullyQualifiedName = type.ToDisplayString(),
            Kind = type.TypeKind.ToString(),
            FilePath = lineSpan?.Path,
            Line = lineSpan?.StartLinePosition.Line + 1,
            Column = lineSpan?.StartLinePosition.Character + 1,
            IsBaseType = false,
            IsAbstract = type.IsAbstract,
            BaseTypes = baseTypes,
            Interfaces = type.AllInterfaces
                .Where(i => SymbolEqualityComparer.Default.Equals(i, targetType) ||
                           i.AllInterfaces.Any(ii => SymbolEqualityComparer.Default.Equals(ii, targetType)))
                .Select(i => i.ToDisplayString())
                .ToList()
        };
    }
}
