using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SharpOps;

/// <summary>
/// Compiles SharpOps sequence back to C# code using Roslyn SyntaxFactory.
/// Uses recursive descent parsing of the prefix-ordered ops.
/// Split into partial classes for maintainability.
/// </summary>
public partial class SharpOpsCompiler
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
            compiler.Consume();
            var expr = compiler.CompileExpression();
            return Block(ReturnStatement(expr));
        }

        return compiler.CompileBlock();
    }

    public static string CompileToString(SharpOpsSequence sequence)
    {
        var block = Compile(sequence);
        return block.NormalizeWhitespace().ToFullString().Replace("\r\n", "\n");
    }

    // Parser state
    private SharpOp Current => _position < _sequence.Ops.Count ? _sequence.Ops[_position] : default;
    private bool AtEnd => _position >= _sequence.Ops.Count;


    private string ResolveIdentifier(SharpOp op)
    {
        var arg = op.Argument ?? "x";

        // If it's a positional reference and we have a symbol kind, resolve it
        if (arg.StartsWith('$') && op.SymbolKind.HasValue)
        {
            var index = int.Parse(arg[1..]);
            if (_sequence.SymbolTables.TryGetValue(op.SymbolKind.Value, out var table) && index < table.Count)
                return table[index];
        }

        return arg;
    }
    private SharpOp Consume() => _sequence.Ops[_position++];
    private SharpOp Peek() => Current;

    // String table lookup
    private string GetStr(string r) => r.StartsWith('$') ? _sequence.GetString(r) : r;


    private ImplicitObjectCreationExpressionSyntax CompileImplicitNew(SharpOp op)
    {
        var n = int.Parse(op.Argument ?? "0");
        var args = new List<ArgumentSyntax>();
        for (int i = 0; i < n; i++) args.Add(Argument(CompileExpression()));

        var result = ImplicitObjectCreationExpression()
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


    private WithExpressionSyntax CompileWithExpression()
    {
        var expr = CompileExpression();
        // Consume WithInitializerExpression
        if (!AtEnd && Peek().Kind == SyntaxKind.WithInitializerExpression)
            Consume();
        var initializers = new List<ExpressionSyntax>();
        while (!AtEnd && Peek().Kind == SyntaxKind.SimpleAssignmentExpression)
            initializers.Add(CompileExpression());
        return WithExpression(expr, InitializerExpression(SyntaxKind.WithInitializerExpression, SeparatedList(initializers)));
    }
}
