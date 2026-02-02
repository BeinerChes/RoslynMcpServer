using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpTinyCoder.DataExtractor;

/// <summary>
/// Processes methods to extract training samples.
/// </summary>
public sealed class MethodProcessor
{
    private readonly ExtractionOptions _options;

    public MethodProcessor(ExtractionOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Determines if a method should be included for training data extraction.
    /// </summary>
    public (bool include, string? skipReason) ShouldInclude(
        MethodDeclarationSyntax method,
        IMethodSymbol symbol,
        string filePath)
    {
        // Skip generated files
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
            filePath.Contains(Path.DirectorySeparatorChar + "obj" + Path.AltDirectorySeparatorChar))
        {
            return (false, "generated");
        }

        // Must be public or internal
        var accessibility = symbol.DeclaredAccessibility;
        if (accessibility != Accessibility.Public &&
            (accessibility != Accessibility.Internal || !_options.IncludeInternal))
        {
            return (false, "accessibility");
        }

        // Skip abstract, extern, partial without body
        if (symbol.IsAbstract || symbol.IsExtern)
        {
            return (false, "no-body");
        }

        // Must have a body
        if (method.Body == null && method.ExpressionBody == null)
        {
            return (false, "no-body");
        }

        // Check for XML documentation with summary
        if (!HasSummaryDoc(method))
        {
            return (false, "no-doc");
        }

        // Check body length
        var bodyLines = CountBodyLines(method);
        if (bodyLines < _options.MinBodyLines || bodyLines > _options.MaxBodyLines)
        {
            return (false, "body-length");
        }

        return (true, null);
    }

    /// <summary>
    /// Extracts the method body without outer braces.
    /// </summary>
    public string ExtractBody(MethodDeclarationSyntax method)
    {
        if (method.ExpressionBody != null)
        {
            // Expression body: => expression;
            return method.ExpressionBody.Expression.ToFullString().Trim();
        }

        if (method.Body != null)
        {
            // Block body: strip outer braces
            var statements = method.Body.Statements;
            if (statements.Count == 0)
            {
                return "";
            }

            // Get the text between opening and closing braces
            var bodySpan = method.Body.Span;
            var openBrace = method.Body.OpenBraceToken;
            var closeBrace = method.Body.CloseBraceToken;

            // Get statements with original formatting
            var bodyText = string.Join("\n", statements.Select(s => s.ToFullString().TrimEnd()));

            // Normalize indentation - find minimum indentation and remove it
            var lines = bodyText.Split('\n');
            var minIndent = lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.TakeWhile(char.IsWhiteSpace).Count())
                .DefaultIfEmpty(0)
                .Min();

            var normalizedLines = lines.Select(l =>
                string.IsNullOrWhiteSpace(l) ? "" : (l.Length > minIndent ? l[minIndent..] : l.TrimStart()));

            return string.Join("\n", normalizedLines).Trim();
        }

        return "";
    }

    /// <summary>
    /// Extracts the XML documentation comment.
    /// </summary>
    public string ExtractXmlDoc(MethodDeclarationSyntax method)
    {
        var trivia = method.GetLeadingTrivia()
            .FirstOrDefault(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                  t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

        if (trivia == default)
        {
            return "";
        }

        return trivia.ToFullString().Trim();
    }

    /// <summary>
    /// Extracts the method signature (without body).
    /// </summary>
    public string ExtractSignature(MethodDeclarationSyntax method, IMethodSymbol symbol)
    {
        // Build signature from symbol for consistency
        var accessibility = symbol.DeclaredAccessibility.ToString().ToLower();
        var modifiers = new List<string> { accessibility };

        if (symbol.IsStatic) modifiers.Add("static");
        if (symbol.IsAsync) modifiers.Add("async");
        if (symbol.IsVirtual) modifiers.Add("virtual");
        if (symbol.IsOverride) modifiers.Add("override");
        if (symbol.IsSealed && symbol.IsOverride) modifiers.Insert(modifiers.Count - 1, "sealed");

        var returnType = symbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var methodName = symbol.Name;
        var parameters = string.Join(", ", symbol.Parameters.Select(p =>
            $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}"));

        return $"{string.Join(" ", modifiers)} {returnType} {methodName}({parameters})";
    }

    /// <summary>
    /// Extracts all fields from the containing class.
    /// </summary>
    public string ExtractClassFields(IMethodSymbol method, MethodDeclarationSyntax syntax, SemanticModel semanticModel)
    {
        var containingType = method.ContainingType;
        if (containingType == null) return "";

        var body = syntax.Body ?? (SyntaxNode?)syntax.ExpressionBody;
        if (body == null) return "";

        // Find all fields referenced in the method body
        var referencedFields = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);

        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is IFieldSymbol field &&
                SymbolEqualityComparer.Default.Equals(field.ContainingType, containingType))
            {
                referencedFields.Add(field);
            }
        }

        if (referencedFields.Count == 0) return "";

        var fields = referencedFields
            .Where(f => !f.IsImplicitlyDeclared)
            .OrderBy(f => f.Name)
            .Select(f =>
            {
                var mods = new List<string>();
                mods.Add(f.DeclaredAccessibility.ToString().ToLower());
                if (f.IsStatic) mods.Add("static");
                if (f.IsReadOnly) mods.Add("readonly");
                if (f.IsConst) mods.Add("const");

                var type = f.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return $"{string.Join(" ", mods)} {type} {f.Name};";
            });

        return string.Join("\n", fields);
    }

    private bool HasSummaryDoc(MethodDeclarationSyntax method)
    {
        var trivia = method.GetLeadingTrivia()
            .FirstOrDefault(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                  t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

        if (trivia == default) return false;

        var structure = trivia.GetStructure();
        if (structure == null) return false;

        // Check for <summary> element
        return structure.DescendantNodes()
            .OfType<XmlElementSyntax>()
            .Any(e => e.StartTag.Name.LocalName.Text == "summary");
    }

    private int CountBodyLines(MethodDeclarationSyntax method)
    {
        if (method.ExpressionBody != null)
        {
            // Expression bodies count as 1 line
            return 1;
        }

        if (method.Body != null)
        {
            var text = method.Body.ToFullString();
            return text.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));
        }

        return 0;
    }
}
