using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SharpOps;

// Statement compilation methods
public partial class SharpOpsCompiler
{
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
            SyntaxKind.YieldReturnStatement => CompileYieldReturn(),
            SyntaxKind.YieldBreakStatement => CompileYieldBreak(),
            SyntaxKind.DoStatement => CompileDo(),
            SyntaxKind.UsingStatement => CompileUsing(),
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
        var typeName = parts[0];
        var count = parts.Length > 1 ? int.Parse(parts[1]) : 1;

        TypeSyntax type;
        if (!AtEnd && (Peek().Kind == SyntaxKind.ArrayType || Peek().Kind == SyntaxKind.IdentifierName))
            type = CompileTypeSyntax();
        else
            type = ParseTypeName(typeName);

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
        var forOp = Consume(); // Get FORSTATEMENT with incrementor count
        var incCount = int.Parse(forOp.Argument ?? "0");

        VariableDeclarationSyntax? decl = Peek().Kind == SyntaxKind.VariableDeclaration ? CompileVarDecl() : null;
        var cond = IsExpressionKind(Peek().Kind) ? CompileExpression() : null;

        var incs = new List<ExpressionSyntax>();
        for (int i = 0; i < incCount; i++)
            incs.Add(CompileExpression());

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

    private BreakStatementSyntax CompileBreak() { Consume(); return BreakStatement(); }
    private ContinueStatementSyntax CompileContinue() { Consume(); return ContinueStatement(); }

    private YieldStatementSyntax CompileYieldReturn()
    {
        Consume();
        return YieldStatement(SyntaxKind.YieldReturnStatement, CompileExpression());
    }

    private YieldStatementSyntax CompileYieldBreak()
    {
        Consume();
        return YieldStatement(SyntaxKind.YieldBreakStatement);
    }

    private DoStatementSyntax CompileDo()
    {
        Consume();
        var body = CompileStatement();
        var condition = CompileExpression();
        return DoStatement(body, condition);
    }

    private UsingStatementSyntax CompileUsing()
    {
        Consume();
        var decl = CompileVarDecl();
        var body = CompileStatement();
        return UsingStatement(decl, null, body);
    }
}
