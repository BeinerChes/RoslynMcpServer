using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpOps.Examples;

public class ExampleAnalyzer : DiagnosticAnalyzer
{


    public static readonly DiagnosticDescriptor Rule = new(
        id: "EX001",
        title: "Example Rule",
        messageFormat: "Found issue: {0}",
        category: "Example",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);


    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);


    public override void Initialize(AnalysisContext context)
    {
        throw new NotImplementedException();
    }


    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        throw new NotImplementedException();
    }
}