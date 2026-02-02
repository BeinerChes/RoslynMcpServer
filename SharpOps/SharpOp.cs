using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpOps;

/// <summary>
/// Represents a single operation in a SharpOps sequence.
/// All values derived from Roslyn's enums - no hardcoded mappings.
/// </summary>
public readonly struct SharpOp
{
    /// <summary>
    /// The SyntaxKind of this operation (from Roslyn).
    /// </summary>
    public SyntaxKind Kind { get; }

    /// <summary>
    /// Optional argument (literal value, identifier name, child count, string table index).
    /// </summary>
    public string? Argument { get; }

    /// <summary>
    /// For identifiers: the SymbolKind (Field, Parameter, Local, Method, Property, etc.)
    /// </summary>
    public SymbolKind? SymbolKind { get; }

    public SharpOp(SyntaxKind kind, string? argument = null, SymbolKind? symbolKind = null)
    {
        Kind = kind;
        Argument = argument;
        SymbolKind = symbolKind;
    }

    /// <summary>
    /// Serialize to string format. Space-separated tokens.
    /// Examples:
    ///   IfStatement 2
    ///   NumericLiteralExpression 42
    ///   IdentifierName Field _tokens
    ///   StringLiteralExpression $0
    /// </summary>
    public override string ToString()
    {
        var kindName = Kind.ToString();

        if (SymbolKind.HasValue && Argument != null)
        {
            // Identifier with symbol kind: IdentifierName Field _tokens
            return $"{kindName} {SymbolKind.Value} {Argument}";
        }

        if (Argument != null)
        {
            // Multi-part arguments use : internally, convert to spaces
            var spacedArg = Argument.Replace(":", " ");
            return $"{kindName} {spacedArg}";
        }

        // Op without argument: ReturnStatement, AddExpression
        return kindName;
    }

    /// <summary>
    /// Parse from space-separated tokens. Returns the op and number of tokens consumed.
    /// </summary>
    public static (SharpOp op, int consumed) ParseTokens(string[] tokens, int start)
    {
        if (start >= tokens.Length)
            throw new ArgumentException("No tokens to parse");

        var kind = Enum.Parse<SyntaxKind>(tokens[start]);

        // Check if there's a next token
        if (start + 1 >= tokens.Length)
            return (new SharpOp(kind), 1);

        var next = tokens[start + 1];

        // IdentifierName with SymbolKind: 3 tokens (kind, symbolKind, name)
        // Check this FIRST because SymbolKind names (Parameter, Property, Field) are also valid SyntaxKind names
        if (kind == SyntaxKind.IdentifierName && Enum.TryParse<SymbolKind>(next, out var symbolKind))
        {
            if (start + 2 >= tokens.Length)
                throw new ArgumentException($"IdentifierName with SymbolKind {symbolKind} missing name");
            return (new SharpOp(kind, tokens[start + 2], symbolKind), 3);
        }

        // Two-part argument: type + count/name (e.g., "VariableDeclaration var 1" or "DeclarationPattern MyType myVar")
        if (HasTwoPartArgument(kind) && start + 2 < tokens.Length)
        {
            var thirdToken = tokens[start + 2];
            // Check that third token is not a SyntaxKind NAME (which would mean this op has no two-part arg)
            // But allow digits (counts) and identifiers that happen to match SyntaxKind names
            if (char.IsDigit(thirdToken[0]) || !Enum.TryParse<SyntaxKind>(thirdToken, out _))
            {
                return (new SharpOp(kind, $"{next}:{thirdToken}"), 3);
            }
        }

        // Ops that always take an identifier argument (member names, variable names)
        // These should consume the next token even if it happens to match a SyntaxKind name
        if (TakesIdentifierArgument(kind))
        {
            return (new SharpOp(kind, next), 2);
        }

        // If next token is a SyntaxKind NAME (not a number), current op has no argument
        // Numbers can match enum values (e.g., "4" matches SyntaxKind.List), so skip that check for digits
        if (!char.IsDigit(next[0]) && Enum.TryParse<SyntaxKind>(next, out _))
            return (new SharpOp(kind), 1);

        // Single argument
        return (new SharpOp(kind, next), 2);
    }

    private static bool HasTwoPartArgument(SyntaxKind kind)
    {
        return kind is SyntaxKind.ObjectCreationExpression
                    or SyntaxKind.ForEachStatement
                    or SyntaxKind.VariableDeclaration
                    or SyntaxKind.DeclarationExpression
                    or SyntaxKind.DeclarationPattern;
    }

    /// <summary>
    /// Parse from old colon-separated format (for backwards compatibility).
    /// </summary>
    public static SharpOp Parse(string s)
    {
        var parts = s.Split(':');

        var kind = Enum.Parse<SyntaxKind>(parts[0]);

        if (parts.Length == 1)
        {
            return new SharpOp(kind);
        }

        if (parts.Length == 2)
        {
            return new SharpOp(kind, parts[1]);
        }

        if (parts.Length >= 3)
        {
            if (kind == SyntaxKind.IdentifierName && Enum.TryParse<SymbolKind>(parts[1], out var symbolKind))
            {
                var name = string.Join(":", parts.Skip(2));
                return new SharpOp(kind, name, symbolKind);
            }

            return new SharpOp(kind, string.Join(":", parts.Skip(1)));
        }

        throw new FormatException($"Invalid SharpOp format: {s}");
    }


    private static bool TakesIdentifierArgument(SyntaxKind kind)
    {
        // These ops always take an identifier argument (member name, variable name)
        // The argument should be consumed even if it happens to match a SyntaxKind name (like "None")
        return kind is SyntaxKind.SimpleMemberAccessExpression
                    or SyntaxKind.MemberBindingExpression
                    or SyntaxKind.VariableDeclarator;
    }
}
