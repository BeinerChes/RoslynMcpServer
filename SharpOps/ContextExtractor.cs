using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpOps;

/// <summary>
/// Extracts human-readable context from a method for training input.
/// </summary>
public static class ContextExtractor
{
    /// <summary>
    /// Extract full context string for a method.
    /// Format:
    ///   {summary}
    ///   @param name: description
    ///   @returns: description
    ///
    ///   {signature}
    ///
    ///   FIELDS:
    ///   fieldName: Type
    ///
    ///   &lt;|output|&gt;
    /// </summary>
    public static string Extract(MethodDeclarationSyntax method, IMethodSymbol symbol, SemanticModel semanticModel)
    {
        var sb = new StringBuilder();

        // Extract XML documentation
        var xmlDoc = ExtractXmlDoc(method);
        if (!string.IsNullOrWhiteSpace(xmlDoc))
        {
            sb.AppendLine(xmlDoc);
            sb.AppendLine();
        }

        // Method signature (without body)
        var signature = ExtractSignature(symbol);
        sb.AppendLine(signature);

        // Used fields
        var usedFields = ExtractUsedFields(method, symbol, semanticModel);
        if (!string.IsNullOrWhiteSpace(usedFields))
        {
            sb.AppendLine();
            sb.AppendLine("FIELDS:");
            sb.AppendLine(usedFields);
        }

        // Output marker
        sb.AppendLine();
        sb.Append("<|output|>");

        return sb.ToString();
    }

    /// <summary>
    /// Extract XML documentation without /// prefix.
    /// </summary>
    private static string ExtractXmlDoc(MethodDeclarationSyntax method)
    {
        var trivia = method.GetLeadingTrivia()
            .FirstOrDefault(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                  t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

        if (trivia == default) return "";

        var structure = trivia.GetStructure();
        if (structure == null) return "";

        var sb = new StringBuilder();

        // Find summary element
        var summary = structure.DescendantNodes()
            .OfType<XmlElementSyntax>()
            .FirstOrDefault(e => e.StartTag.Name.LocalName.Text == "summary");

        if (summary != null)
        {
            var summaryText = GetXmlElementText(summary).Trim();
            if (!string.IsNullOrWhiteSpace(summaryText))
            {
                sb.AppendLine(summaryText);
            }
        }

        // Find param elements
        var paramElements = structure.DescendantNodes()
            .OfType<XmlElementSyntax>()
            .Where(e => e.StartTag.Name.LocalName.Text == "param");

        foreach (var param in paramElements)
        {
            var nameAttr = param.StartTag.Attributes
                .OfType<XmlNameAttributeSyntax>()
                .FirstOrDefault();

            var paramName = nameAttr?.Identifier.Identifier.Text ?? "";
            var paramDesc = GetXmlElementText(param).Trim();

            if (!string.IsNullOrWhiteSpace(paramName) && !string.IsNullOrWhiteSpace(paramDesc))
            {
                sb.AppendLine($"@param {paramName}: {paramDesc}");
            }
        }

        // Find returns element
        var returns = structure.DescendantNodes()
            .OfType<XmlElementSyntax>()
            .FirstOrDefault(e => e.StartTag.Name.LocalName.Text == "returns");

        if (returns != null)
        {
            var returnsText = GetXmlElementText(returns).Trim();
            if (!string.IsNullOrWhiteSpace(returnsText))
            {
                sb.AppendLine($"@returns: {returnsText}");
            }
        }

        return sb.ToString().Trim();
    }

    private static string GetXmlElementText(XmlElementSyntax element)
    {
        var textNodes = element.Content
            .OfType<XmlTextSyntax>()
            .SelectMany(t => t.TextTokens)
            .Select(t => t.Text.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t));

        return string.Join(" ", textNodes);
    }

    /// <summary>
    /// Extract method signature.
    /// </summary>
    private static string ExtractSignature(IMethodSymbol symbol)
    {
        var accessibility = symbol.DeclaredAccessibility.ToString().ToLower();
        var modifiers = new List<string> { accessibility };

        if (symbol.IsStatic) modifiers.Add("static");
        if (symbol.IsAsync) modifiers.Add("async");
        if (symbol.IsVirtual) modifiers.Add("virtual");
        if (symbol.IsOverride) modifiers.Add("override");

        var returnType = symbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var methodName = symbol.Name;
        var parameters = string.Join(", ", symbol.Parameters.Select(p =>
            $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}"));

        return $"{string.Join(" ", modifiers)} {returnType} {methodName}({parameters})";
    }

    /// <summary>
    /// Extract only fields that are actually used in the method body.
    /// </summary>
    private static string ExtractUsedFields(MethodDeclarationSyntax method, IMethodSymbol symbol, SemanticModel semanticModel)
    {
        var containingType = symbol.ContainingType;
        if (containingType == null) return "";

        var body = method.Body ?? (SyntaxNode?)method.ExpressionBody;
        if (body == null) return "";

        // Find all fields referenced in the method body
        var referencedFields = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);

        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var identifierSymbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (identifierSymbol is IFieldSymbol field &&
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
                var type = f.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return $"{f.Name}: {type}";
            });

        return string.Join("\n", fields);
    }
}
