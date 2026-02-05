using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SharpOps;

// LINQ query compilation methods
public partial class SharpOpsCompiler
{
    private QueryExpressionSyntax CompileQueryExpression()
    {
        var fromOp = Consume();
        if (fromOp.Kind != SyntaxKind.FromClause)
            throw new NotSupportedException($"Expected FromClause, got {fromOp.Kind}");

        var source = CompileExpression();
        var identifier = fromOp.Argument ?? "x";
        var fromClause = FromClause(Identifier(identifier), source);

        // Consume QUERYBODY token if present
        if (!AtEnd && Peek().Kind == SyntaxKind.QueryBody)
            Consume();

        var queryBody = CompileQueryBody();
        return QueryExpression(fromClause, queryBody);
    }

    private QueryBodySyntax CompileQueryBody()
    {
        var clauses = new List<QueryClauseSyntax>();
        SelectOrGroupClauseSyntax? selectOrGroup = null;

        while (!AtEnd)
        {
            var next = Peek();
            if (next.Kind == SyntaxKind.SelectClause)
            {
                Consume();
                selectOrGroup = SelectClause(CompileExpression());
                break;
            }
            else if (next.Kind == SyntaxKind.GroupClause)
            {
                Consume();
                var groupExpr = CompileExpression();
                var byExpr = CompileExpression();
                selectOrGroup = GroupClause(groupExpr, byExpr);
                break;
            }
            else if (next.Kind == SyntaxKind.WhereClause)
            {
                Consume();
                clauses.Add(WhereClause(CompileExpression()));
            }
            else if (next.Kind == SyntaxKind.OrderByClause)
            {
                Consume();
                var orderings = new List<OrderingSyntax>();
                while (!AtEnd && (Peek().Kind == SyntaxKind.AscendingOrdering || Peek().Kind == SyntaxKind.DescendingOrdering))
                {
                    var orderOp = Consume();
                    orderings.Add(Ordering(orderOp.Kind, CompileExpression()));
                }
                if (orderings.Count == 0)
                    orderings.Add(Ordering(SyntaxKind.AscendingOrdering, CompileExpression()));
                clauses.Add(OrderByClause(SeparatedList(orderings)));
            }
            else if (next.Kind == SyntaxKind.LetClause)
            {
                var letOp = Consume();
                clauses.Add(LetClause(Identifier(letOp.Argument ?? "x"), CompileExpression()));
            }
            else
            {
                break;
            }
        }

        selectOrGroup ??= SelectClause(IdentifierName("x"));

        // Handle query continuation (into clause)
        QueryContinuationSyntax? continuation = null;
        if (!AtEnd && Peek().Kind == SyntaxKind.QueryContinuation)
        {
            var contOp = Consume();
            var identifier = contOp.Argument ?? "g";
            var contBody = CompileQueryBody();
            continuation = QueryContinuation(Identifier(identifier), contBody);
        }

        return QueryBody(List(clauses), selectOrGroup, continuation);
    }
}
