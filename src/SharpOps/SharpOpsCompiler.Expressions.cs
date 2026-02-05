using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SharpOps;

// Expression compilation methods
public partial class SharpOpsCompiler
{
    private ExpressionSyntax CompileExpression()
    {
        var op = Consume();
        return op.Kind switch
        {
            SyntaxKind.NumericLiteralExpression => LiteralExpression(op.Kind, ParseNum(op.Argument ?? "0")),
            SyntaxKind.StringLiteralExpression => LiteralExpression(op.Kind, Literal(GetStr(op.Argument ?? ""))),
            SyntaxKind.CharacterLiteralExpression => LiteralExpression(op.Kind, Literal(op.Argument?[0] ?? ' ')),
            SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression or
            SyntaxKind.NullLiteralExpression or SyntaxKind.DefaultLiteralExpression => LiteralExpression(op.Kind),
            // Use ParseMemberName to handle both simple and generic names (e.g., Method<T>)
            SyntaxKind.IdentifierName => ParseMemberName(ResolveIdentifier(op)),
            SyntaxKind.SimpleMemberAccessExpression => MemberAccessExpression(op.Kind, CompileExpression(), ParseMemberName(op.Argument ?? "M")),
            SyntaxKind.InvocationExpression => CompileInvoke(op),
            SyntaxKind.ObjectCreationExpression => CompileNew(op),
            SyntaxKind.ImplicitObjectCreationExpression => CompileImplicitNew(op),
            SyntaxKind.ElementAccessExpression => CompileIndex(op),
            SyntaxKind.ConditionalExpression => ConditionalExpression(CompileExpression(), CompileExpression(), CompileExpression()),
            SyntaxKind.CastExpression => CastExpression(ParseTypeName(op.Argument ?? "object"), CompileExpression()),
            SyntaxKind.AwaitExpression => AwaitExpression(CompileExpression()),
            SyntaxKind.ThisExpression => ThisExpression(),
            SyntaxKind.BaseExpression => BaseExpression(),
            SyntaxKind.ThrowExpression => ThrowExpression(CompileExpression()),
            SyntaxKind.ConditionalAccessExpression => ConditionalAccessExpression(CompileExpression(), CompileExpression()),
            SyntaxKind.MemberBindingExpression => MemberBindingExpression(ParseMemberName(op.Argument ?? "M")),
            SyntaxKind.DeclarationExpression => CompileDeclarationExpr(op),
            SyntaxKind.AnonymousObjectCreationExpression => CompileAnonymousObject(op),
            SyntaxKind.TupleExpression => CompileTuple(op),
            SyntaxKind.SimpleLambdaExpression => CompileSimpleLambda(op),
            SyntaxKind.ParenthesizedLambdaExpression => CompileParenthesizedLambda(op),
            SyntaxKind.IsPatternExpression => CompileIsPattern(),
            SyntaxKind.RangeExpression => CompileRange(),
            SyntaxKind.InterpolatedStringExpression => CompileInterpolatedString(),
            SyntaxKind.TypeOfExpression => TypeOfExpression(CompileTypeSyntax()),
            SyntaxKind.ArrayCreationExpression => CompileArrayCreation(),
            SyntaxKind.AsExpression => BinaryExpression(op.Kind, CompileExpression(), CompileTypeSyntax()),
            SyntaxKind.SwitchExpression => CompileSwitchExpression(),
            SyntaxKind.IndexExpression => PrefixUnaryExpression(SyntaxKind.IndexExpression, CompileExpression()),
            SyntaxKind.QueryExpression => CompileQueryExpression(),
            SyntaxKind.ArrayInitializerExpression => CompileArrayInitializer(op),
            SyntaxKind.ImplicitArrayCreationExpression => CompileImplicitArrayCreation(),
            SyntaxKind.DefaultExpression => DefaultExpression(ParseTypeName(op.Argument ?? "object")),
            SyntaxKind.SizeOfExpression => SizeOfExpression(ParseTypeName(op.Argument ?? "int")),
            SyntaxKind.ParenthesizedExpression => ParenthesizedExpression(CompileExpression()),
            SyntaxKind.CheckedExpression => CheckedExpression(SyntaxKind.CheckedExpression, CompileExpression()),
            SyntaxKind.UncheckedExpression => CheckedExpression(SyntaxKind.UncheckedExpression, CompileExpression()),
            SyntaxKind.SuppressNullableWarningExpression => PostfixUnaryExpression(op.Kind, CompileExpression()),
            SyntaxKind.WithExpression => CompileWithExpression(),
            _ when IsBinary(op.Kind) => BinaryExpression(op.Kind, CompileExpression(), CompileExpression()),
            _ when IsUnaryPrefix(op.Kind) => PrefixUnaryExpression(op.Kind, CompileExpression()),
            _ when IsUnaryPostfix(op.Kind) => PostfixUnaryExpression(op.Kind, CompileExpression()),
            _ when IsAssignment(op.Kind) => AssignmentExpression(op.Kind, CompileExpression(), CompileExpression()),
            _ => throw new NotSupportedException($"Expression: {op.Kind}")
        };
    }

    private InvocationExpressionSyntax CompileInvoke(SharpOp op)
    {
        var n = int.Parse(op.Argument ?? "0");
        var expr = CompileExpression();
        var args = new List<ArgumentSyntax>();
        for (int i = 0; i < n; i++) args.Add(CompileArgument());
        return InvocationExpression(expr, ArgumentList(SeparatedList(args)));
    }

    private ArgumentSyntax CompileArgument()
    {
        if (!AtEnd && Peek().Kind == SyntaxKind.NameColon)
        {
            var nameOp = Consume();
            var name = nameOp.Argument ?? "arg";
            var expr = CompileExpression();
            return Argument(NameColon(IdentifierName(name)), default, expr);
        }
        return Argument(CompileExpression());
    }

    private ObjectCreationExpressionSyntax CompileNew(SharpOp op)
    {
        var parts = op.Argument?.Split(':') ?? ["Object", "0"];
        var n = parts.Length > 1 ? int.Parse(parts[1]) : 0;
        var args = new List<ArgumentSyntax>();
        for (int i = 0; i < n; i++) args.Add(Argument(CompileExpression()));

        var result = ObjectCreationExpression(ParseTypeName(parts[0]))
            .WithArgumentList(ArgumentList(SeparatedList(args)));

        if (!AtEnd && Peek().Kind == SyntaxKind.ObjectInitializerExpression)
        {
            Consume();
            var initializers = new List<ExpressionSyntax>();
            while (!AtEnd && Peek().Kind == SyntaxKind.SimpleAssignmentExpression)
                initializers.Add(CompileExpression());
            result = result.WithInitializer(InitializerExpression(SyntaxKind.ObjectInitializerExpression, SeparatedList(initializers)));
        }
        return result;
    }

    private ElementAccessExpressionSyntax CompileIndex(SharpOp op)
    {
        var n = int.Parse(op.Argument ?? "1");
        var expr = CompileExpression();
        var args = new List<ArgumentSyntax>();
        for (int i = 0; i < n; i++) args.Add(Argument(CompileExpression()));
        return ElementAccessExpression(expr, BracketedArgumentList(SeparatedList(args)));
    }

    private RangeExpressionSyntax CompileRange()
    {
        ExpressionSyntax? left = null, right = null;
        if (!AtEnd && IsExpressionKind(Peek().Kind))
            right = CompileExpression();
        return RangeExpression(left, right);
    }

    private DeclarationExpressionSyntax CompileDeclarationExpr(SharpOp op)
    {
        var parts = op.Argument?.Split(':') ?? ["var", "x"];
        var type = ParseTypeName(parts[0]);

        // Check for discard designation
        if (!AtEnd && Peek().Kind == SyntaxKind.DiscardDesignation)
        {
            Consume();
            return DeclarationExpression(type, DiscardDesignation());
        }

        var name = parts.Length > 1 ? parts[1] : "x";
        return DeclarationExpression(type, SingleVariableDesignation(Identifier(name)));
    }

    private TupleExpressionSyntax CompileTuple(SharpOp op)
    {
        var n = int.Parse(op.Argument ?? "2");
        var args = new List<ArgumentSyntax>();
        for (int i = 0; i < n; i++) args.Add(Argument(CompileExpression()));
        return TupleExpression(SeparatedList(args));
    }

    private SimpleLambdaExpressionSyntax CompileSimpleLambda(SharpOp op)
    {
        var param = Parameter(Identifier(op.Argument ?? "x"));
        var body = CompileLambdaBody();
        return SimpleLambdaExpression(param, body);
    }

    private ParenthesizedLambdaExpressionSyntax CompileParenthesizedLambda(SharpOp op)
    {
        var paramNames = (op.Argument ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var parameters = paramNames.Select(n => Parameter(Identifier(n.Trim()))).ToArray();
        var body = CompileLambdaBody();
        return ParenthesizedLambdaExpression(ParameterList(SeparatedList(parameters)), body);
    }

    private CSharpSyntaxNode CompileLambdaBody()
    {
        if (Peek().Kind == SyntaxKind.Block)
            return CompileBlock();
        return CompileExpression();
    }

    private IsPatternExpressionSyntax CompileIsPattern()
    {
        var expr = CompileExpression();
        var pattern = CompilePattern();
        return IsPatternExpression(expr, pattern);
    }

    private AnonymousObjectCreationExpressionSyntax CompileAnonymousObject(SharpOp op)
    {
        var members = new List<AnonymousObjectMemberDeclaratorSyntax>();
        while (!AtEnd && Peek().Kind == SyntaxKind.AnonymousObjectMemberDeclarator)
        {
            Consume();
            NameEqualsSyntax? nameEquals = null;
            if (Peek().Kind == SyntaxKind.NameEquals)
            {
                Consume();
                var name = CompileExpression();
                nameEquals = NameEquals(((IdentifierNameSyntax)name).Identifier.Text);
            }
            var expr = CompileExpression();
            members.Add(nameEquals != null
                ? AnonymousObjectMemberDeclarator(nameEquals, expr)
                : AnonymousObjectMemberDeclarator(expr));
        }
        return AnonymousObjectCreationExpression(SeparatedList(members));
    }

    private InterpolatedStringExpressionSyntax CompileInterpolatedString()
    {
        var contents = new List<InterpolatedStringContentSyntax>();
        while (!AtEnd)
        {
            var next = Peek();
            if (next.Kind == SyntaxKind.InterpolatedStringText)
            {
                Consume();
                var text = GetStr(next.Argument ?? "");
                contents.Add(InterpolatedStringText(Token(TriviaList(), SyntaxKind.InterpolatedStringTextToken, text, text, TriviaList())));
            }
            else if (next.Kind == SyntaxKind.Interpolation)
            {
                Consume();
                var expr = CompileExpression();

                InterpolationAlignmentClauseSyntax? alignment = null;
                if (!AtEnd && Peek().Kind == SyntaxKind.InterpolationAlignmentClause)
                {
                    Consume();
                    alignment = InterpolationAlignmentClause(Token(SyntaxKind.CommaToken), CompileExpression());
                }

                InterpolationFormatClauseSyntax? format = null;
                if (!AtEnd && Peek().Kind == SyntaxKind.InterpolationFormatClause)
                {
                    var formatOp = Consume();
                    var formatText = GetStr(formatOp.Argument ?? "");
                    format = InterpolationFormatClause(Token(SyntaxKind.ColonToken),
                        Token(TriviaList(), SyntaxKind.InterpolatedStringTextToken, formatText, formatText, TriviaList()));
                }
                contents.Add(Interpolation(expr, alignment, format));
            }
            else break;
        }
        return InterpolatedStringExpression(Token(SyntaxKind.InterpolatedStringStartToken), List(contents), Token(SyntaxKind.InterpolatedStringEndToken));
    }

    private SwitchExpressionSyntax CompileSwitchExpression()
    {
        var governingExpr = CompileExpression();
        var arms = new List<SwitchExpressionArmSyntax>();
        while (!AtEnd && Peek().Kind == SyntaxKind.SwitchExpressionArm)
        {
            Consume();
            var pattern = CompilePattern();
            var expression = CompileExpression();
            arms.Add(SwitchExpressionArm(pattern, expression));
        }
        return SwitchExpression(governingExpr, SeparatedList(arms));
    }

    private ArrayCreationExpressionSyntax CompileArrayCreation()
    {
        var typeOp = Consume();
        if (typeOp.Kind != SyntaxKind.ArrayType)
            throw new NotSupportedException($"Expected ArrayType, got {typeOp.Kind}");

        var elementType = CompileTypeSyntax();
        if (!AtEnd && Peek().Kind == SyntaxKind.ArrayRankSpecifier)
        {
            Consume();
            if (!AtEnd && IsExpressionKind(Peek().Kind))
            {
                var sizeExpr = CompileExpression();
                return ArrayCreationExpression(ArrayType(elementType, SingletonList(ArrayRankSpecifier(SingletonSeparatedList(sizeExpr)))));
            }
            return ArrayCreationExpression(ArrayType(elementType, SingletonList(ArrayRankSpecifier())));
        }
        return ArrayCreationExpression(ArrayType(elementType, SingletonList(ArrayRankSpecifier())));
    }

    private InitializerExpressionSyntax CompileArrayInitializer(SharpOp op)
    {
        var count = int.Parse(op.Argument ?? "0");
        var expressions = new List<ExpressionSyntax>();
        for (int i = 0; i < count; i++)
            expressions.Add(CompileExpression());
        return InitializerExpression(SyntaxKind.ArrayInitializerExpression, SeparatedList(expressions));
    }

    private ImplicitArrayCreationExpressionSyntax CompileImplicitArrayCreation()
    {
        var initializer = CompileArrayInitializer(Consume());
        return ImplicitArrayCreationExpression(initializer);
    }

    private TypeSyntax CompileTypeSyntax()
    {
        var op = Consume();
        if (op.Kind == SyntaxKind.IdentifierName)
            return IdentifierName(op.Argument ?? "T");
        if (op.Kind == SyntaxKind.ArrayType)
        {
            var elementType = CompileTypeSyntax();
            if (!AtEnd && Peek().Kind == SyntaxKind.ArrayRankSpecifier)
            {
                Consume();
                if (!AtEnd && Peek().Kind == SyntaxKind.NumericLiteralExpression)
                {
                    var size = Consume();
                    return ArrayType(elementType, SingletonList(
                        ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(
                            LiteralExpression(SyntaxKind.NumericLiteralExpression, ParseNum(size.Argument ?? "0"))))));
                }
                return ArrayType(elementType, SingletonList(ArrayRankSpecifier()));
            }
            return ArrayType(elementType, SingletonList(ArrayRankSpecifier()));
        }
        return ParseTypeName(op.Argument ?? "object");
    }
}
