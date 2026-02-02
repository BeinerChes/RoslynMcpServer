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
    /// Serialize to string format. All names from Roslyn's ToString().
    /// Examples:
    ///   IfStatement:2
    ///   NumericLiteralExpression:42
    ///   IdentifierName:Field:_tokens
    ///   StringLiteralExpression:$0
    /// </summary>
    public override string ToString()
    {
        var kindName = Kind.ToString();

        if (SymbolKind.HasValue && Argument != null)
        {
            // Identifier with symbol kind: IdentifierName:Field:_tokens
            return $"{kindName}:{SymbolKind.Value}:{Argument}";
        }

        if (Argument != null)
        {
            // Op with argument: NumericLiteralExpression:42, Block:3
            return $"{kindName}:{Argument}";
        }

        // Op without argument: ReturnStatement, AddExpression
        return kindName;
    }

    /// <summary>
    /// Parse from string format. Uses Roslyn's Enum.Parse.
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
            // Only IdentifierName has SymbolKind: IdentifierName:Field:_tokens
            // Other ops with 3+ parts just have multi-colon arguments: VariableDeclaration:var:1
            if (kind == SyntaxKind.IdentifierName && Enum.TryParse<SymbolKind>(parts[1], out var symbolKind))
            {
                var name = string.Join(":", parts.Skip(2));
                return new SharpOp(kind, name, symbolKind);
            }

            // Not an identifier, treat everything after first colon as argument
            return new SharpOp(kind, string.Join(":", parts.Skip(1)));
        }

        throw new FormatException($"Invalid SharpOp format: {s}");
    }
}
