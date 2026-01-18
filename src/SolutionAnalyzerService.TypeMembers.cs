using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcpServer;

public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Gets all members of a type (methods, properties, fields, events, constructors).
    /// </summary>
    public async Task<GetTypeMembersResult> GetTypeMembersAsync(
        string solutionPath,
        string typeName,
        MemberKindFilter memberKind = MemberKindFilter.All,
        bool includeInherited = false,
        bool compact = true)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new GetTypeMembersResult
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
                return new GetTypeMembersResult
                {
                    Success = false,
                    Error = $"Type not found: {typeName}. Try using the exact type name."
                };
            }

            Console.Error.WriteLine($"Getting members of: {targetType.ToDisplayString()}");

            var members = new List<MemberInfo>();

            // Get members from the type itself
            foreach (var member in targetType.GetMembers())
            {
                if (ShouldIncludeMember(member, memberKind))
                {
                    members.Add(CreateMemberInfo(member, compact, inheritedFrom: null));
                }
            }

            // Optionally include inherited members
            if (includeInherited)
            {
                var currentBase = targetType.BaseType;
                while (currentBase != null && currentBase.SpecialType != SpecialType.System_Object)
                {
                    foreach (var member in currentBase.GetMembers())
                    {
                        if (ShouldIncludeMember(member, memberKind) && !IsOverriddenIn(member, targetType))
                        {
                            members.Add(CreateMemberInfo(member, compact, currentBase.Name));
                        }
                    }
                    currentBase = currentBase.BaseType;
                }

                // Include interface members for interfaces
                foreach (var iface in targetType.AllInterfaces)
                {
                    foreach (var member in iface.GetMembers())
                    {
                        if (ShouldIncludeMember(member, memberKind))
                        {
                            var impl = targetType.FindImplementationForInterfaceMember(member);
                            if (impl == null)
                            {
                                members.Add(CreateMemberInfo(member, compact, iface.Name));
                            }
                        }
                    }
                }
            }

            // Sort: constructors first, then by kind, then by name
            members = members
                .OrderBy(m => m.Kind == "Constructor" ? 0 : 1)
                .ThenBy(m => m.Kind)
                .ThenBy(m => m.Name)
                .ToList();

            var location = targetType.Locations.FirstOrDefault();
            var lineSpan = location?.GetLineSpan();

            Console.Error.WriteLine($"Found {members.Count} members");

            return new GetTypeMembersResult
            {
                Success = true,
                SolutionPath = solutionPath,
                Type = new TypeInfo
                {
                    Name = targetType.Name,
                    FullyQualifiedName = targetType.ToDisplayString(),
                    Kind = targetType.TypeKind.ToString()
                },
                TotalMembers = members.Count,
                Members = members
            };
        }
        catch (Exception ex)
        {
            return new GetTypeMembersResult
            {
                Success = false,
                Error = $"Failed to get type members: {ex.Message}"
            };
        }
    }

    private static bool ShouldIncludeMember(ISymbol member, MemberKindFilter filter)
    {
        // Skip compiler-generated members
        if (member.IsImplicitlyDeclared)
            return false;

        // Skip property accessors (they're part of the property)
        if (member is IMethodSymbol method &&
            (method.MethodKind == MethodKind.PropertyGet ||
             method.MethodKind == MethodKind.PropertySet ||
             method.MethodKind == MethodKind.EventAdd ||
             method.MethodKind == MethodKind.EventRemove))
            return false;

        return filter switch
        {
            MemberKindFilter.Methods => member is IMethodSymbol m &&
                m.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation,
            MemberKindFilter.Properties => member is IPropertySymbol,
            MemberKindFilter.Fields => member is IFieldSymbol,
            MemberKindFilter.Events => member is IEventSymbol,
            MemberKindFilter.Constructors => member is IMethodSymbol m &&
                m.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor,
            _ => member is IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol
        };
    }

    private static bool IsOverriddenIn(ISymbol baseMember, INamedTypeSymbol derivedType)
    {
        if (baseMember is not IMethodSymbol baseMethod || !baseMethod.IsVirtual && !baseMethod.IsAbstract)
            return false;

        return derivedType.GetMembers(baseMember.Name)
            .OfType<IMethodSymbol>()
            .Any(m => m.IsOverride);
    }

    private static MemberInfo CreateMemberInfo(ISymbol member, bool compact, string? inheritedFrom)
    {
        var location = member.Locations.FirstOrDefault();
        var lineSpan = location?.GetLineSpan();

        var kind = member switch
        {
            IMethodSymbol m when m.MethodKind == MethodKind.Constructor => "Constructor",
            IMethodSymbol m when m.MethodKind == MethodKind.StaticConstructor => "StaticConstructor",
            IMethodSymbol => "Method",
            IPropertySymbol => "Property",
            IFieldSymbol => "Field",
            IEventSymbol => "Event",
            _ => member.Kind.ToString()
        };

        return new MemberInfo
        {
            Name = member.Name,
            Kind = kind,
            Signature = GetMemberSignature(member),
            FilePath = compact ? null : lineSpan?.Path,
            Line = compact ? null : lineSpan?.StartLinePosition.Line + 1,
            Column = compact ? null : lineSpan?.StartLinePosition.Character + 1,
            Accessibility = compact ? null : member.DeclaredAccessibility.ToString(),
            IsStatic = compact ? null : member.IsStatic,
            IsAbstract = compact ? null : member.IsAbstract,
            IsVirtual = compact ? null : (member as IMethodSymbol)?.IsVirtual ?? (member as IPropertySymbol)?.IsVirtual,
            IsOverride = compact ? null : (member as IMethodSymbol)?.IsOverride ?? (member as IPropertySymbol)?.IsOverride,
            InheritedFrom = inheritedFrom
        };
    }

    private static string GetMemberSignature(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol method => FormatMethodSignature(method),
            IPropertySymbol property => FormatPropertySignature(property),
            IFieldSymbol field => $"{field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {field.Name}",
            IEventSymbol evt => $"event {evt.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {evt.Name}",
            _ => member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
        };
    }

    private static string FormatMethodSignature(IMethodSymbol method)
    {
        var returnType = method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
            ? ""
            : $"{method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} ";

        var parameters = string.Join(", ", method.Parameters.Select(p =>
            $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}"));

        return $"{returnType}{method.Name}({parameters})";
    }

    private static string FormatPropertySignature(IPropertySymbol property)
    {
        var accessors = new List<string>();
        if (property.GetMethod != null) accessors.Add("get");
        if (property.SetMethod != null) accessors.Add("set");

        return $"{property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {property.Name} {{ {string.Join("; ", accessors)}; }}";
    }
}
