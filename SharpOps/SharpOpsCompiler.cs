using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SharpOps;

/// <summary>
/// Compiles SharpOps sequence back to C# code using Roslyn SyntaxFactory.
/// Uses recursive descent parsing of the prefix-ordered ops.
/// </summary>
public class SharpOpsCompiler
{
    private readonly SharpOpsSequence _sequence;
    private int _position;

    private SharpOpsCompiler(SharpOpsSequence sequence)
    {
        _sequence = sequence;
        _position = 0;
    }

    public static BlockSyntax Compile(SharpOpsSequence sequence)
    {
        var compiler = new SharpOpsCompiler(sequence);

        // Handle expression-bodied methods (ArrowExpressionClause)
        if (compiler.Peek().Kind == SyntaxKind.ArrowExpressionClause)
        {
            compiler.Consume(); // consume ArrowExpressionClause
            var expr = compiler.CompileExpression();
            return Block(ReturnStatement(expr));
        }

        return compiler.CompileBlock();
    }

    public static string CompileToString(SharpOpsSequence sequence)
    {
        var block = Compile(sequence);
        return block.NormalizeWhitespace().ToFullString();
    }

    private SharpOp Current => _position < _sequence.Ops.Count ? _sequence.Ops[_position] : default;
    private bool AtEnd => _position >= _sequence.Ops.Count;
    private SharpOp Consume() => _sequence.Ops[_position++];
    private SharpOp Peek() => Current;

    private BlockSyntax CompileBlock()
    {
        var op = Consume();
        var count = int.Parse(op.Argument ?? "0");
        var statements = new List<StatementSyntax>();
        for (int i = 0; i < count; i++)
            statements.Add(CompileStatement());
        return Block(statements);
    }

    private StatementSyntax CompileStatement()
    {
        var op = Peek();
        return op.Kind switch
        {
            SyntaxKind.Block => CompileBlock(),
            SyntaxKind.LocalDeclarationStatement => CompileLocalDeclaration(),
            SyntaxKind.ReturnStatement => CompileReturn(),
            SyntaxKind.IfStatement => CompileIf(),
            SyntaxKind.ForStatement => CompileFor(),
            SyntaxKind.ForEachStatement => CompileForEach(),
            SyntaxKind.WhileStatement => CompileWhile(),
            SyntaxKind.ThrowStatement => CompileThrow(),
            SyntaxKind.TryStatement => CompileTry(),
            SyntaxKind.BreakStatement => CompileBreak(),
            SyntaxKind.ContinueStatement => CompileContinue(),
            SyntaxKind.LockStatement => CompileLock(),
            _ when IsExpressionKind(op.Kind) => ExpressionStatement(CompileExpression()),
            _ => throw new NotSupportedException($"Statement: {op.Kind}")
        };
    }

    private LocalDeclarationStatementSyntax CompileLocalDeclaration()
    {
        var op = Consume();
        var isUsing = op.Argument == "using";
        var decl = CompileVarDecl();
        var stmt = LocalDeclarationStatement(decl);
        return isUsing ? stmt.WithUsingKeyword(Token(SyntaxKind.UsingKeyword)) : stmt;
    }

    private VariableDeclarationSyntax CompileVarDecl()
    {
        var op = Consume();
        var parts = op.Argument?.Split(':') ?? ["var", "1"];
        var type = ParseTypeName(parts[0]);
        var count = parts.Length > 1 ? int.Parse(parts[1]) : 1;
        if (Peek().Kind == SyntaxKind.IdentifierName) Consume();
        var decls = new List<VariableDeclaratorSyntax>();
        for (int i = 0; i < count; i++) decls.Add(CompileVarDeclarator());
        return VariableDeclaration(type, SeparatedList(decls));
    }

    private VariableDeclaratorSyntax CompileVarDeclarator()
    {
        var op = Consume();
        EqualsValueClauseSyntax? init = null;
        if (Peek().Kind == SyntaxKind.EqualsValueClause)
        {
            Consume();
            init = EqualsValueClause(CompileExpression());
        }
        return VariableDeclarator(Identifier(op.Argument ?? "x"), null, init);
    }

    private ReturnStatementSyntax CompileReturn()
    {
        Consume();
        return ReturnStatement(!AtEnd && IsExpressionKind(Peek().Kind) ? CompileExpression() : null);
    }

    private IfStatementSyntax CompileIf()
    {
        var op = Consume();
        var branches = int.Parse(op.Argument ?? "1");
        var cond = CompileExpression();
        var then = CompileStatement();
        ElseClauseSyntax? elseClause = null;
        if (branches > 1)
        {
            if (Peek().Kind == SyntaxKind.ElseClause) Consume();
            elseClause = ElseClause(CompileStatement());
        }
        return IfStatement(cond, then, elseClause);
    }

    private ForStatementSyntax CompileFor()
    {
        Consume();
        VariableDeclarationSyntax? decl = Peek().Kind == SyntaxKind.VariableDeclaration ? CompileVarDecl() : null;
        var cond = IsExpressionKind(Peek().Kind) ? CompileExpression() : null;
        var incs = new List<ExpressionSyntax>();
        while (IsExpressionKind(Peek().Kind) && Peek().Kind != SyntaxKind.Block) incs.Add(CompileExpression());
        return ForStatement(decl, SeparatedList<ExpressionSyntax>(), cond, SeparatedList(incs), CompileStatement());
    }

    private ForEachStatementSyntax CompileForEach()
    {
        var op = Consume();
        var parts = op.Argument?.Split(':') ?? ["var", "item"];
        var type = ParseTypeName(parts[0]);
        var name = parts.Length > 1 ? parts[1] : "item";
        return ForEachStatement(type, Identifier(name), CompileExpression(), CompileStatement());
    }

    private WhileStatementSyntax CompileWhile()
    {
        Consume();
        return WhileStatement(CompileExpression(), CompileStatement());
    }

    private ThrowStatementSyntax CompileThrow()
    {
        Consume();
        return ThrowStatement(!AtEnd && IsExpressionKind(Peek().Kind) ? CompileExpression() : null);
    }

    private TryStatementSyntax CompileTry()
    {
        Consume();
        var block = CompileBlock();
        var catches = new List<CatchClauseSyntax>();
        while (Peek().Kind == SyntaxKind.CatchClause) catches.Add(CompileCatch());
        FinallyClauseSyntax? fin = null;
        if (Peek().Kind == SyntaxKind.FinallyClause) { Consume(); fin = FinallyClause(CompileBlock()); }
        return TryStatement(block, List(catches), fin);
    }

    private CatchClauseSyntax CompileCatch()
    {
        var op = Consume();
        var parts = op.Argument?.Split(':') ?? ["Exception"];
        var decl = CatchDeclaration(ParseTypeName(parts[0]), parts.Length > 1 ? Identifier(parts[1]) : default);
        return CatchClause(decl, null, CompileBlock());
    }

    private LockStatementSyntax CompileLock()
    {
        Consume();
        return LockStatement(CompileExpression(), CompileStatement());
    }

    private BreakStatementSyntax CompileBreak()
    {
        Consume();
        return BreakStatement();
    }

    private ContinueStatementSyntax CompileContinue()
    {
        Consume();
        return ContinueStatement();
    }

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
            SyntaxKind.IdentifierName => IdentifierName(op.Argument ?? "x"),
            SyntaxKind.SimpleMemberAccessExpression => MemberAccessExpression(op.Kind, CompileExpression(), IdentifierName(op.Argument ?? "M")),
            SyntaxKind.InvocationExpression => CompileInvoke(op),
            SyntaxKind.ObjectCreationExpression => CompileNew(op),
            SyntaxKind.ElementAccessExpression => CompileIndex(op),
            SyntaxKind.ConditionalExpression => ConditionalExpression(CompileExpression(), CompileExpression(), CompileExpression()),
            SyntaxKind.CastExpression => CastExpression(ParseTypeName(op.Argument ?? "object"), CompileExpression()),
            SyntaxKind.AwaitExpression => AwaitExpression(CompileExpression()),
            SyntaxKind.ThisExpression => ThisExpression(),
            SyntaxKind.BaseExpression => BaseExpression(),
            SyntaxKind.ThrowExpression => ThrowExpression(CompileExpression()),
            SyntaxKind.ConditionalAccessExpression => ConditionalAccessExpression(CompileExpression(), CompileExpression()),
            SyntaxKind.MemberBindingExpression => MemberBindingExpression(IdentifierName(op.Argument ?? "M")),
            SyntaxKind.DeclarationExpression => CompileDeclarationExpr(op),
            SyntaxKind.AnonymousObjectCreationExpression => CompileAnonymousObject(op),
            SyntaxKind.TupleExpression => CompileTuple(op),
            SyntaxKind.SimpleLambdaExpression => CompileSimpleLambda(op),
            SyntaxKind.ParenthesizedLambdaExpression => CompileParenthesizedLambda(op),
            SyntaxKind.IsPatternExpression => CompileIsPattern(),
            SyntaxKind.RangeExpression => CompileRange(),
            SyntaxKind.InterpolatedStringExpression => CompileInterpolatedString(),
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
        for (int i = 0; i < n; i++) args.Add(Argument(CompileExpression()));
        return InvocationExpression(expr, ArgumentList(SeparatedList(args)));
    }

    private ObjectCreationExpressionSyntax CompileNew(SharpOp op)
    {
        var parts = op.Argument?.Split(':') ?? ["Object", "0"];
        var n = parts.Length > 1 ? int.Parse(parts[1]) : 0;
        var args = new List<ArgumentSyntax>();
        for (int i = 0; i < n; i++) args.Add(Argument(CompileExpression()));

        var result = ObjectCreationExpression(ParseTypeName(parts[0]))
            .WithArgumentList(ArgumentList(SeparatedList(args)));

        // Check for object initializer
        if (!AtEnd && Peek().Kind == SyntaxKind.ObjectInitializerExpression)
        {
            Consume(); // consume ObjectInitializerExpression
            var initializers = new List<ExpressionSyntax>();
            while (!AtEnd && Peek().Kind == SyntaxKind.SimpleAssignmentExpression)
            {
                initializers.Add(CompileExpression());
            }
            result = result.WithInitializer(InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SeparatedList(initializers)));
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
        // Check if next op is an expression (right operand) or something else
        if (!AtEnd && IsExpressionKind(Peek().Kind))
        {
            right = CompileExpression();
        }
        return RangeExpression(left, right);
    }

    private DeclarationExpressionSyntax CompileDeclarationExpr(SharpOp op)
    {
        var parts = op.Argument?.Split(':') ?? ["var", "x"];
        var type = ParseTypeName(parts[0]);
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
        // Lambda body can be expression or block
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

    private PatternSyntax CompilePattern()
    {
        var op = Consume();
        return op.Kind switch
        {
            SyntaxKind.DeclarationPattern => CompileDeclarationPattern(op),
            SyntaxKind.ConstantPattern => ConstantPattern(CompileExpression()),
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

    private AnonymousObjectCreationExpressionSyntax CompileAnonymousObject(SharpOp op)
    {
        var members = new List<AnonymousObjectMemberDeclaratorSyntax>();
        while (!AtEnd && Peek().Kind == SyntaxKind.AnonymousObjectMemberDeclarator)
        {
            Consume(); // consume AnonymousObjectMemberDeclarator
            NameEqualsSyntax? nameEquals = null;
            if (Peek().Kind == SyntaxKind.NameEquals)
            {
                Consume(); // consume NameEquals
                var name = CompileExpression(); // get identifier
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
                contents.Add(InterpolatedStringText(Token(
                    TriviaList(),
                    SyntaxKind.InterpolatedStringTextToken,
                    text,
                    text,
                    TriviaList())));
            }
            else if (next.Kind == SyntaxKind.Interpolation)
            {
                Consume();
                contents.Add(Interpolation(CompileExpression()));
            }
            else
            {
                break;
            }
        }
        return InterpolatedStringExpression(Token(SyntaxKind.InterpolatedStringStartToken), List(contents), Token(SyntaxKind.InterpolatedStringEndToken));
    }

    private string GetStr(string r) => r.StartsWith('$') ? _sequence.GetString(r) : r;
    private static SyntaxToken ParseNum(string v) => int.TryParse(v, out var i) ? Literal(i) :
        long.TryParse(v, out var l) ? Literal(l) : double.TryParse(v, out var d) ? Literal(d) : Literal(int.Parse(v));

    private static bool IsBinary(SyntaxKind k) => k is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or
        SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression or
        SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression or SyntaxKind.LessThanExpression or
        SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression or
        SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression or SyntaxKind.BitwiseAndExpression or
        SyntaxKind.BitwiseOrExpression or SyntaxKind.ExclusiveOrExpression or SyntaxKind.CoalesceExpression or
        SyntaxKind.AsExpression or SyntaxKind.IsExpression;

    private static bool IsUnaryPrefix(SyntaxKind k) => k is SyntaxKind.LogicalNotExpression or SyntaxKind.UnaryMinusExpression or
        SyntaxKind.UnaryPlusExpression or SyntaxKind.BitwiseNotExpression or SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression;

    private static bool IsUnaryPostfix(SyntaxKind k) => k is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression;

    private static bool IsAssignment(SyntaxKind k) => k is SyntaxKind.SimpleAssignmentExpression or SyntaxKind.AddAssignmentExpression or
        SyntaxKind.SubtractAssignmentExpression or SyntaxKind.MultiplyAssignmentExpression or SyntaxKind.DivideAssignmentExpression or
        SyntaxKind.ModuloAssignmentExpression or SyntaxKind.AndAssignmentExpression or SyntaxKind.OrAssignmentExpression or
        SyntaxKind.ExclusiveOrAssignmentExpression or SyntaxKind.CoalesceAssignmentExpression;

    private static bool IsExpressionKind(SyntaxKind k) => k is SyntaxKind.NumericLiteralExpression or SyntaxKind.StringLiteralExpression or
        SyntaxKind.CharacterLiteralExpression or SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression or
        SyntaxKind.NullLiteralExpression or SyntaxKind.DefaultLiteralExpression or SyntaxKind.IdentifierName or
        SyntaxKind.SimpleMemberAccessExpression or SyntaxKind.InvocationExpression or SyntaxKind.ObjectCreationExpression or
        SyntaxKind.ElementAccessExpression or SyntaxKind.ConditionalExpression or SyntaxKind.CastExpression or
        SyntaxKind.AwaitExpression or SyntaxKind.ThisExpression or SyntaxKind.BaseExpression or SyntaxKind.ThrowExpression or
        SyntaxKind.ConditionalAccessExpression or SyntaxKind.RangeExpression or SyntaxKind.InterpolatedStringExpression or
        SyntaxKind.MemberBindingExpression or SyntaxKind.DeclarationExpression or
        SyntaxKind.AnonymousObjectCreationExpression or SyntaxKind.TupleExpression or
        SyntaxKind.SimpleLambdaExpression or SyntaxKind.ParenthesizedLambdaExpression or
        SyntaxKind.IsPatternExpression
        || IsBinary(k) || IsUnaryPrefix(k) || IsUnaryPostfix(k) || IsAssignment(k);
}
