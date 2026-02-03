namespace SharpOps.Examples;

public class TestAnalyzer
{


    public static readonly ImmutableHashSet<string> OldNamespaces = ImmutableHashSet.Create("System.Web", "System.Data");


    public static readonly DiagnosticDescriptor Rule = new("TEST001", "Test", "Test: {0}", "Test", DiagnosticSeverity.Warning, true);


    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not UsingDirectiveSyntax usingDirective)
            return;
        if (usingDirective.Name is null)
            return;
        var namespaceName = usingDirective.Name.ToString();
        if (OldNamespaces.Contains(namespaceName))
        {
            var diagnostic = Diagnostic.Create(Rule, usingDirective.Name.GetLocation(), namespaceName);
            context.ReportDiagnostic(diagnostic);
        }
    }
}