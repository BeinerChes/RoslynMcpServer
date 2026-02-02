using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpOps;

/// <summary>
/// Extracts SharpOps sequence from a method body.
/// Walks Roslyn AST in prefix order, emitting operations.
/// </summary>
public class SharpOpsExtractor : CSharpSyntaxWalker
{
    private readonly SemanticModel _semanticModel;
    private readonly SharpOpsSequence _sequence = new();

    public SharpOpsExtractor(SemanticModel semanticModel) : base(SyntaxWalkerDepth.Node)
    {
        _semanticModel = semanticModel;
    }

    /// <summary>
    /// Extract SharpOps from a method body.
    /// </summary>
    public static SharpOpsSequence Extract(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        var extractor = new SharpOpsExtractor(semanticModel);

        if (method.Body != null)
        {
            extractor.Visit(method.Body);
        }
        else if (method.ExpressionBody != null)
        {
            extractor.Visit(method.ExpressionBody);
        }

        return extractor._sequence;
    }

    public override void Visit(SyntaxNode? node)
    {
        if (node == null) return;

        // Skip nodes we don't want to emit
        if (ShouldSkip(node))
        {
            // Still visit children
            base.Visit(node);
            return;
        }

        // Emit operation for this node
        var op = CreateOp(node);
        if (op.HasValue)
        {
            _sequence.Add(op.Value);
        }

        // Visit children
        base.Visit(node);
    }

    private bool ShouldSkip(SyntaxNode node)
    {
        // Skip these structural nodes - they're implied by their parent or not meaningful
        return node switch
        {
            // Argument lists - count is on invocation/tuple
            ArgumentListSyntax => true,
            ArgumentSyntax => true,

            // Parameter lists - we're extracting body, not signature (also for lambdas)
            ParameterListSyntax => true,
            ParameterSyntax => true,

            // Bracketed lists
            BracketedArgumentListSyntax => true,

            // Type syntax - skip in type contexts, but keep for expressions (e.g., string.IsNullOrEmpty)
            PredefinedTypeSyntax pt when pt.Parent is not MemberAccessExpressionSyntax => true,

            // Expression statement wrapper - expression is enough
            ExpressionStatementSyntax => true,

            // Parenthesized - just for precedence, compiler handles
            ParenthesizedExpressionSyntax => true,

            // Omitted parts
            OmittedArraySizeExpressionSyntax => true,

            // Skip member access name - already captured in parent's argument
            IdentifierNameSyntax id when id.Parent is MemberAccessExpressionSyntax ma && ma.Name == id => true,

            // Skip member binding name in conditional access - name is in MemberBindingExpression argument
            IdentifierNameSyntax id2 when id2.Parent is MemberBindingExpressionSyntax => true,

            // Skip generic type names and type arguments - type is captured in parent's argument
            GenericNameSyntax => true,
            TypeArgumentListSyntax => true,

            // Skip type identifiers in object creation - type is in ObjectCreationExpression argument
            IdentifierNameSyntax id3 when id3.Parent is ObjectCreationExpressionSyntax => true,

            // Skip type identifiers in cast expressions - type is in CastExpression argument
            IdentifierNameSyntax id4 when id4.Parent is CastExpressionSyntax => true,

            // Skip qualified names (e.g., System.String) - we just use the full name
            QualifiedNameSyntax => true,

            // Skip identifiers that are part of qualified names
            IdentifierNameSyntax id7 when id7.Parent is QualifiedNameSyntax => true,

            // Skip type identifier in foreach - type is in ForEachStatement argument
            IdentifierNameSyntax id5 when id5.Parent is ForEachStatementSyntax fe && fe.Type == id5 => true,

            // Skip declaration expression/pattern children - captured in argument
            SingleVariableDesignationSyntax => true,
            IdentifierNameSyntax id6 when id6.Parent is DeclarationExpressionSyntax => true,
            IdentifierNameSyntax id8 when id8.Parent is DeclarationPatternSyntax => true,

            _ => false
        };
    }

    private SharpOp? CreateOp(SyntaxNode node)
    {
        var kind = node.Kind();

        return node switch
        {
            // Block - emit with statement count
            BlockSyntax block => new SharpOp(kind, block.Statements.Count.ToString()),

            // Literals - emit with value
            LiteralExpressionSyntax literal => CreateLiteralOp(literal),

            // Identifiers - emit with symbol kind and name
            IdentifierNameSyntax identifier => CreateIdentifierOp(identifier),

            // Invocation - emit with argument count
            InvocationExpressionSyntax invocation =>
                new SharpOp(kind, invocation.ArgumentList.Arguments.Count.ToString()),

            // Object creation - emit with type name and argument count
            ObjectCreationExpressionSyntax creation =>
                new SharpOp(kind, $"{creation.Type}:{creation.ArgumentList?.Arguments.Count ?? 0}"),

            // If statement - emit with branch count (1 = no else, 2 = has else)
            IfStatementSyntax ifStmt =>
                new SharpOp(kind, (ifStmt.Else != null ? 2 : 1).ToString()),

            // For/while - just emit kind
            ForStatementSyntax or WhileStatementSyntax =>
                new SharpOp(kind),

            // ForEach - emit with type:varname
            ForEachStatementSyntax forEach =>
                new SharpOp(kind, $"{forEach.Type}:{forEach.Identifier.Text}"),

            // Return/throw - just emit kind
            ReturnStatementSyntax or ThrowStatementSyntax =>
                new SharpOp(kind),

            // Assignment expressions - emit with operator
            AssignmentExpressionSyntax assignment =>
                new SharpOp(kind),

            // Binary expressions - just emit kind (operands follow)
            BinaryExpressionSyntax => new SharpOp(kind),

            // Unary expressions
            PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax =>
                new SharpOp(kind),

            // Member access
            MemberAccessExpressionSyntax memberAccess =>
                new SharpOp(kind, memberAccess.Name.Identifier.Text),

            // Member binding in conditional access (?.Member)
            MemberBindingExpressionSyntax memberBinding =>
                new SharpOp(kind, memberBinding.Name.Identifier.Text),

            // Conditional access (?.)
            ConditionalAccessExpressionSyntax => new SharpOp(kind),

            // Element access (indexer)
            ElementAccessExpressionSyntax elementAccess =>
                new SharpOp(kind, elementAccess.ArgumentList.Arguments.Count.ToString()),

            // Conditional expression (ternary)
            ConditionalExpressionSyntax => new SharpOp(kind),

            // Cast expression
            CastExpressionSyntax cast =>
                new SharpOp(kind, cast.Type.ToString()),

            // Await expression
            AwaitExpressionSyntax => new SharpOp(kind),

            // Declaration expression (out var x) - emit with type:name
            DeclarationExpressionSyntax declExpr =>
                new SharpOp(kind, $"{declExpr.Type}:{GetDesignationName(declExpr.Designation)}"),

            // Anonymous object creation
            AnonymousObjectCreationExpressionSyntax => new SharpOp(kind),

            // Anonymous object member declarator
            AnonymousObjectMemberDeclaratorSyntax => new SharpOp(kind),

            // Name equals (in anonymous objects and initializers)
            NameEqualsSyntax => new SharpOp(kind),

            // Object/collection initializer
            InitializerExpressionSyntax => new SharpOp(kind),

            // Tuple expression - emit with element count
            TupleExpressionSyntax tuple =>
                new SharpOp(kind, tuple.Arguments.Count.ToString()),

            // Predefined type as expression (e.g., string in string.IsNullOrEmpty)
            PredefinedTypeSyntax predef =>
                new SharpOp(SyntaxKind.IdentifierName, predef.Keyword.Text, SymbolKind.NamedType),

            // Pattern matching (is expression)
            IsPatternExpressionSyntax => new SharpOp(kind),
            DeclarationPatternSyntax declPattern =>
                new SharpOp(kind, $"{declPattern.Type}:{GetDesignationName(declPattern.Designation)}"),
            ConstantPatternSyntax => new SharpOp(kind),

            // Lambda expressions
            SimpleLambdaExpressionSyntax simple =>
                new SharpOp(kind, simple.Parameter.Identifier.Text),
            ParenthesizedLambdaExpressionSyntax paren =>
                new SharpOp(kind, string.Join(",", paren.ParameterList.Parameters.Select(p => p.Identifier.Text))),

            // Variable declaration
            VariableDeclarationSyntax varDecl =>
                new SharpOp(kind, $"{varDecl.Type}:{varDecl.Variables.Count}"),

            // Variable declarator
            VariableDeclaratorSyntax varDecltor =>
                new SharpOp(kind, varDecltor.Identifier.Text),

            // Local declaration statement - check for using modifier
            LocalDeclarationStatementSyntax localDecl =>
                new SharpOp(kind, localDecl.UsingKeyword != default ? "using" : null),

            // Else clause
            ElseClauseSyntax => new SharpOp(kind),

            // Throw expression (not statement)
            ThrowExpressionSyntax => new SharpOp(kind),

            // Default/other nodes - just emit kind
            _ => new SharpOp(kind)
        };
    }

    private SharpOp CreateLiteralOp(LiteralExpressionSyntax literal)
    {
        var kind = literal.Kind();

        return kind switch
        {
            SyntaxKind.StringLiteralExpression =>
                new SharpOp(kind, _sequence.AddString(literal.Token.ValueText)),

            SyntaxKind.NumericLiteralExpression =>
                new SharpOp(kind, literal.Token.ValueText),

            SyntaxKind.CharacterLiteralExpression =>
                new SharpOp(kind, literal.Token.ValueText),

            SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression =>
                new SharpOp(kind),

            SyntaxKind.NullLiteralExpression =>
                new SharpOp(kind),

            SyntaxKind.DefaultLiteralExpression =>
                new SharpOp(kind),

            _ => new SharpOp(kind, literal.Token.ValueText)
        };
    }

    private SharpOp CreateIdentifierOp(IdentifierNameSyntax identifier)
    {
        var symbolInfo = _semanticModel.GetSymbolInfo(identifier);
        var symbol = symbolInfo.Symbol;

        if (symbol == null)
        {
            // Unresolved identifier - just use name
            return new SharpOp(SyntaxKind.IdentifierName, identifier.Identifier.Text);
        }

        // Use Roslyn's SymbolKind directly
        return new SharpOp(SyntaxKind.IdentifierName, identifier.Identifier.Text, symbol.Kind);
    }

    private static string GetDesignationName(VariableDesignationSyntax designation)
    {
        return designation switch
        {
            SingleVariableDesignationSyntax single => single.Identifier.Text,
            DiscardDesignationSyntax => "_",
            _ => "x"
        };
    }
}
