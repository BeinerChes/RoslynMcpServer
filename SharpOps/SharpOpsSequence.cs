using System.Text;
using System.Text.Json;

namespace SharpOps;

/// <summary>
/// A sequence of SharpOps with a string table for literal strings.
/// </summary>
public class SharpOpsSequence
{
    /// <summary>
    /// The operations in prefix order.
    /// </summary>
    public List<SharpOp> Ops { get; } = new();

    /// <summary>
    /// String table for literal strings. Referenced by index ($0, $1, etc.)
    /// </summary>
    public List<string> StringTable { get; } = new();

    /// <summary>
    /// Add an operation to the sequence.
    /// </summary>
    public void Add(SharpOp op) => Ops.Add(op);

    /// <summary>
    /// Add a string to the string table and return its index reference.
    /// Returns existing index if string already in table.
    /// </summary>
    public string AddString(string value)
    {
        var index = StringTable.IndexOf(value);
        if (index >= 0)
        {
            return $"${index}";
        }

        StringTable.Add(value);
        return $"${StringTable.Count - 1}";
    }

    /// <summary>
    /// Get string from table by index reference ($0, $1, etc.)
    /// </summary>
    public string GetString(string reference)
    {
        if (!reference.StartsWith('$'))
        {
            throw new ArgumentException($"Invalid string reference: {reference}");
        }

        var index = int.Parse(reference[1..]);
        return StringTable[index];
    }

    /// <summary>
    /// Serialize ops to single-line string (space-separated).
    /// </summary>
    public string SerializeOps()
    {
        return string.Join(" ", Ops.Select(op => op.ToString()));
    }

    /// <summary>
    /// Parse ops from single-line string (space-separated tokens).
    /// </summary>
    public static SharpOpsSequence ParseOps(string opsString, IEnumerable<string>? stringTable = null)
    {
        var sequence = new SharpOpsSequence();

        if (stringTable != null)
        {
            sequence.StringTable.AddRange(stringTable);
        }

        var tokens = opsString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var i = 0;
        while (i < tokens.Length)
        {
            var (op, consumed) = SharpOp.ParseTokens(tokens, i);
            sequence.Ops.Add(op);
            i += consumed;
        }

        return sequence;
    }

    /// <summary>
    /// Serialize to JSON for training data output.
    /// </summary>
    public string ToJson()
    {
        var obj = new
        {
            ops = SerializeOps(),
            strings = StringTable
        };

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }
}
