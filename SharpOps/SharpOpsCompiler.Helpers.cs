using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SharpOps;

// Helper methods and SyntaxKind classification
public partial class SharpOpsCompiler
{
    private static SyntaxToken ParseNum(string v) =>
        int.TryParse(v, out var i) ? Literal(i) :
        long.TryParse(v, out var l) ? Literal(l) :
        double.TryParse(v, out var d) ? Literal(d) : Literal(int.Parse(v));

    private static SimpleNameSyntax ParseMemberName(string name)
    {
        var ltIndex = name.IndexOf('<');
        if (ltIndex < 0)
            return IdentifierName(name);

        var baseName = name[..ltIndex];
        var typeArgsStr = name[(ltIndex + 1)..^1];
        var typeArgs = typeArgsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => ParseTypeName(t.Trim()))
            .ToArray();

        return GenericName(Identifier(baseName), TypeArgumentList(SeparatedList(typeArgs)));
    }

    private static bool IsBinary(SyntaxKind k) => k is
        SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or
        SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression or
        SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression or SyntaxKind.LessThanExpression or
        SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression or
        SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression or SyntaxKind.BitwiseAndExpression or
        SyntaxKind.BitwiseOrExpression or SyntaxKind.ExclusiveOrExpression or SyntaxKind.CoalesceExpression or
        SyntaxKind.LeftShiftExpression or SyntaxKind.RightShiftExpression or
        SyntaxKind.AsExpression or SyntaxKind.IsExpression;

    private static bool IsUnaryPrefix(SyntaxKind k) => k is
        SyntaxKind.LogicalNotExpression or SyntaxKind.UnaryMinusExpression or
        SyntaxKind.UnaryPlusExpression or SyntaxKind.BitwiseNotExpression or
        SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression;

    private static bool IsUnaryPostfix(SyntaxKind k) => k is
        SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression;

    private static bool IsAssignment(SyntaxKind k) => k is
        SyntaxKind.SimpleAssignmentExpression or SyntaxKind.AddAssignmentExpression or
        SyntaxKind.SubtractAssignmentExpression or SyntaxKind.MultiplyAssignmentExpression or
        SyntaxKind.DivideAssignmentExpression or SyntaxKind.ModuloAssignmentExpression or
        SyntaxKind.AndAssignmentExpression or SyntaxKind.OrAssignmentExpression or
        SyntaxKind.ExclusiveOrAssignmentExpression or SyntaxKind.CoalesceAssignmentExpression;

    private static bool IsExpressionKind(SyntaxKind k) => k is
        SyntaxKind.NumericLiteralExpression or SyntaxKind.StringLiteralExpression or
        SyntaxKind.CharacterLiteralExpression or SyntaxKind.TrueLiteralExpression or
        SyntaxKind.FalseLiteralExpression or SyntaxKind.NullLiteralExpression or
        SyntaxKind.DefaultLiteralExpression or SyntaxKind.IdentifierName or
        SyntaxKind.SimpleMemberAccessExpression or SyntaxKind.InvocationExpression or
        SyntaxKind.ObjectCreationExpression or SyntaxKind.ImplicitObjectCreationExpression or
        SyntaxKind.ElementAccessExpression or SyntaxKind.ConditionalExpression or
        SyntaxKind.CastExpression or SyntaxKind.AwaitExpression or
        SyntaxKind.ThisExpression or SyntaxKind.BaseExpression or
        SyntaxKind.ThrowExpression or SyntaxKind.ConditionalAccessExpression or
        SyntaxKind.RangeExpression or SyntaxKind.InterpolatedStringExpression or
        SyntaxKind.MemberBindingExpression or SyntaxKind.DeclarationExpression or
        SyntaxKind.AnonymousObjectCreationExpression or SyntaxKind.TupleExpression or
        SyntaxKind.SimpleLambdaExpression or SyntaxKind.ParenthesizedLambdaExpression or
        SyntaxKind.IsPatternExpression or SyntaxKind.TypeOfExpression or
        SyntaxKind.ArrayCreationExpression or SyntaxKind.ArrayInitializerExpression or
        SyntaxKind.IndexExpression or SyntaxKind.QueryExpression or
        SyntaxKind.SwitchExpression or SyntaxKind.DefaultExpression or
        SyntaxKind.ImplicitArrayCreationExpression or SyntaxKind.SuppressNullableWarningExpression or
        SyntaxKind.WithExpression
        || IsBinary(k) || IsUnaryPrefix(k) || IsUnaryPostfix(k) || IsAssignment(k);
}
