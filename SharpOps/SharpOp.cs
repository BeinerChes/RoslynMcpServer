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
        var kindName = Kind.ToString().ToUpperInvariant();

        if (SymbolKind.HasValue && Argument != null)
        {
            // Identifier with symbol kind: IDENTIFIERNAME PARAMETER:context
            return $"{kindName} {SymbolKind.Value.ToString().ToUpperInvariant()}:{Argument}";
        }

        if (Argument != null)
        {
            // Argument as-is (already has : for two-part args)
            return $"{kindName} {Argument}";
        }

        // Op without argument: RETURNSTATEMENT, ADDEXPRESSION
        return kindName;
    }

    /// <summary>
    /// Parse from space-separated tokens. Returns the op and number of tokens consumed.
    /// </summary>
    public static (SharpOp op, int consumed) ParseTokens(string[] tokens, int start)
    {
        if (start >= tokens.Length)
            throw new ArgumentException("No tokens to parse");

        var kindToken = tokens[start];

        // SyntaxKinds must be UPPERCASE
        if (!IsUpperCase(kindToken))
            throw new ArgumentException($"Expected UPPERCASE SyntaxKind, got: {kindToken}");

        var kind = Enum.Parse<SyntaxKind>(kindToken, ignoreCase: true);

        // Check if there's a next token
        if (start + 1 >= tokens.Length)
            return (new SharpOp(kind), 1);

        var next = tokens[start + 1];

        // If next token is UPPERCASE, it's the next SyntaxKind - current op has no argument
        if (IsUpperCase(next))
            return (new SharpOp(kind), 1);

        // Parse argument - may contain SYMBOLKIND:Name for identifiers
        if (kind == SyntaxKind.IdentifierName && next.Contains(':'))
        {
            var colonIdx = next.IndexOf(':');
            var symbolKindStr = next[..colonIdx];
            var name = next[(colonIdx + 1)..];
            if (Enum.TryParse<SymbolKind>(symbolKindStr, ignoreCase: true, out var symbolKind))
            {
                return (new SharpOp(kind, name, symbolKind), 2);
            }
        }

        // Single argument
        return (new SharpOp(kind, next), 2);
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


    private static bool IsUpperCase(string token)
    {
        // Too short to be a SyntaxKind (shortest is "AS", "DO", "IF", "IN", "IS")
        if (token.Length < 2)
            return false;
        if (!char.IsLetter(token[0]))
            return false;
        // Tokens with ':' are arguments (FIELD:$0, LOCAL:name), not SyntaxKinds
        if (token.Contains(':'))
            return false;
        // Check if all letters are uppercase
        foreach (var c in token)
        {
            if (char.IsLetter(c) && !char.IsUpper(c))
                return false;
        }
        // Final check: must be a valid SyntaxKind
        return Enum.TryParse<SyntaxKind>(token, ignoreCase: true, out _);
    }
}
