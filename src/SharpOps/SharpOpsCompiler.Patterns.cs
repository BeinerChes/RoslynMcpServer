using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SharpOps;

// Pattern compilation methods
public partial class SharpOpsCompiler
{
    private PatternSyntax CompilePattern()
    {
        var op = Consume();
        return op.Kind switch
        {
            SyntaxKind.DeclarationPattern => CompileDeclarationPattern(op),
            SyntaxKind.ConstantPattern => ConstantPattern(CompileExpression()),
            SyntaxKind.NotPattern => UnaryPattern(Token(SyntaxKind.NotKeyword), CompilePattern()),
            SyntaxKind.OrPattern => BinaryPattern(SyntaxKind.OrPattern, CompilePattern(), CompilePattern()),
            SyntaxKind.AndPattern => BinaryPattern(SyntaxKind.AndPattern, CompilePattern(), CompilePattern()),
            SyntaxKind.TypePattern => TypePattern(CompileTypeSyntax()),
            SyntaxKind.DiscardPattern => DiscardPattern(),
            SyntaxKind.VarPattern => VarPattern(SingleVariableDesignation(Identifier(op.Argument ?? "x"))),
            SyntaxKind.RelationalPattern => RelationalPattern(Token(GetRelationalToken(op.Argument)), CompileExpression()),
            _ => throw new NotSupportedException($"Pattern: {op.Kind}")
        };
    }

    private DeclarationPatternSyntax CompileDeclarationPattern(SharpOp op)
    {
        var parts = op.Argument?.Split(':') ?? ["object", "x"];
        var type = ParseTypeName(parts[0]);
        var name = parts.Length > 1 ? parts[1] : "x";
        return DeclarationPattern(type, SingleVariableDesignation(Identifier(name)));
    }

    private static SyntaxKind GetRelationalToken(string? op) => op switch
    {
        "<" => SyntaxKind.LessThanToken,
        "<=" => SyntaxKind.LessThanEqualsToken,
        ">" => SyntaxKind.GreaterThanToken,
        ">=" => SyntaxKind.GreaterThanEqualsToken,
        _ => SyntaxKind.LessThanToken
    };
}
