using System.Text.Json;
using Microsoft.ML.Tokenizers;

namespace SharpOps.Inference;

public class SharpOpsTokenizer
{


    private readonly Tokenizer _tokenizer;


    private readonly Dictionary<int, string> _idToToken;


    private readonly Dictionary<string, int> _tokenToId;


    private static readonly Dictionary<char, byte> _unicodeToByte;


    private static readonly Dictionary<byte, char> _byteToUnicode;


    private readonly List<(string token, int id)> _specialTokens;
    public SharpOpsTokenizer(string tokenizerPath)
    {
        // Parse the HuggingFace tokenizer.json to extract vocab and merges
        var json = File.ReadAllText(tokenizerPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var model = root.GetProperty("model");

        // Extract vocab dictionary
        var vocabElement = model.GetProperty("vocab");
        _tokenToId = new Dictionary<string, int>();
        _idToToken = new Dictionary<int, string>();
        foreach (var prop in vocabElement.EnumerateObject())
        {
            var token = prop.Name;
            var id = prop.Value.GetInt32();
            _tokenToId[token] = id;
            _idToToken[id] = token;
        }

        // Extract merges as list of string pairs
        var mergesElement = model.GetProperty("merges");
        var merges = new List<string>();
        foreach (var merge in mergesElement.EnumerateArray())
        {
            // Each merge is an array of two strings: ["token1", "token2"]
            var parts = merge.EnumerateArray().ToArray();
            merges.Add($"{parts[0].GetString()} {parts[1].GetString()}");
        }

        // Create vocab JSON stream
        var vocabJson = JsonSerializer.Serialize(_tokenToId);
        using var vocabStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(vocabJson));

        // Create merges stream (one merge per line)
        var mergesText = string.Join("\n", merges);
        using var mergesStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(mergesText));

        // Create the BPE tokenizer
        _tokenizer = BpeTokenizer.Create(vocabStream, mergesStream);

        // Load ALL added_tokens from tokenizer.json (sorted by length descending for proper matching)
        _specialTokens = new List<(string token, int id)>();
        if (root.TryGetProperty("added_tokens", out var addedTokens))
        {
            foreach (var tok in addedTokens.EnumerateArray())
            {
                var content = tok.GetProperty("content").GetString()!;
                var id = tok.GetProperty("id").GetInt32();
                _specialTokens.Add((content, id));
            }
        }
        _specialTokens.Sort((a, b) => b.token.Length.CompareTo(a.token.Length));

        // Set well-known special token IDs
        PadId = GetTokenId("<|pad|>");
        UnkId = GetTokenId("<|unk|>");
        BosId = GetTokenId("<|bos|>");
        EosId = GetTokenId("<|eos|>");
        OutputId = GetTokenId("<|output|>");
    }


    static SharpOpsTokenizer()
    {
        // Initialize GPT-2 byte-to-unicode mapping
        // This is the bytes_to_unicode() function from HuggingFace tokenizers
        _byteToUnicode = new Dictionary<byte, char>();
        _unicodeToByte = new Dictionary<char, byte>();

        // Printable ASCII characters map to themselves
        var bs = new List<int>();
        bs.AddRange(Enumerable.Range('!', '~' - '!' + 1));
        bs.AddRange(Enumerable.Range('¡', '¬' - '¡' + 1));
        bs.AddRange(Enumerable.Range('®', 'ÿ' - '®' + 1));

        var cs = new List<int>(bs);
        int n = 0;
        for (int b = 0; b < 256; b++)
        {
            if (!bs.Contains(b))
            {
                bs.Add(b);
                cs.Add(256 + n);
                n++;
            }
        }

        for (int i = 0; i < bs.Count; i++)
        {
            _byteToUnicode[(byte)bs[i]] = (char)cs[i];
            _unicodeToByte[(char)cs[i]] = (byte)bs[i];
        }
    }
    public int PadId { get; }


    public int UnkId { get; }


    public int BosId { get; }


    public int EosId { get; }


    public int OutputId { get; }


    public int VocabSize => _tokenToId.Count;


    private int GetTokenId(string token)
    {
        if (_tokenToId.TryGetValue(token, out var id))
            return id;
        throw new InvalidOperationException($"Special token not found in vocabulary: {token}");
    }


    public int[] Encode(string text)
    {
        var result = new List<int>();
        var remaining = text;

        while (remaining.Length > 0)
        {
            // Check for special tokens first
            bool foundSpecial = false;
            foreach (var (token, id) in _specialTokens)
            {
                if (remaining.StartsWith(token))
                {
                    result.Add(id);
                    remaining = remaining.Substring(token.Length);
                    foundSpecial = true;
                    break;
                }
            }

            if (foundSpecial)
                continue;

            // Find the next special token position
            int nextSpecialPos = remaining.Length;
            foreach (var (token, _) in _specialTokens)
            {
                int pos = remaining.IndexOf(token);
                if (pos >= 0 && pos < nextSpecialPos)
                    nextSpecialPos = pos;
            }

            // Encode the segment before the next special token using BPE
            var segment = remaining.Substring(0, nextSpecialPos);
            if (segment.Length > 0)
            {
                // Convert text to bytes, then to unicode using ByteLevel mapping
                var bytes = System.Text.Encoding.UTF8.GetBytes(segment);
                var unicodeChars = new char[bytes.Length];
                for (int i = 0; i < bytes.Length; i++)
                {
                    unicodeChars[i] = _byteToUnicode[bytes[i]];
                }
                var unicodeText = new string(unicodeChars);

                // Use the BPE tokenizer on the unicode text
                var ids = _tokenizer.EncodeToIds(unicodeText);
                result.AddRange(ids);
            }

            remaining = remaining.Substring(nextSpecialPos);
        }

        return result.ToArray();
    }


    public int[] EncodeWithBos(string text)
    {
        var encoded = Encode(text);
        var result = new int[encoded.Length + 1];
        result[0] = BosId;
        Array.Copy(encoded, 0, result, 1, encoded.Length);
        return result;
    }


    public string Decode(int[] ids)
    {
        // Build the unicode string from tokens
        var sb = new System.Text.StringBuilder();
        foreach (var id in ids)
        {
            if (_idToToken.TryGetValue(id, out var token))
            {
                sb.Append(token);
            }
        }

        // Convert unicode to bytes using the ByteLevel mapping
        var bytes = new List<byte>();
        foreach (var c in sb.ToString())
        {
            if (_unicodeToByte.TryGetValue(c, out var b))
            {
                bytes.Add(b);
            }
            else
            {
                // Character maps to itself (ASCII printable)
                bytes.Add((byte)c);
            }
        }

        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }


    public string DecodeOutput(int[] ids)
    {
        // Find the output token position
        var outputPos = Array.IndexOf(ids, OutputId);
        if (outputPos < 0)
            return Decode(ids);

        // Get tokens after <|output|>, excluding <|eos|>
        var outputIds = ids
            .Skip(outputPos + 1)
            .Where(id => id != EosId)
            .ToArray();

        return Decode(outputIds);
    }


    public string? GetToken(int id)
    {
        return _idToToken.TryGetValue(id, out var token) ? token : null;
    }


    public bool IsSpecialToken(int id)
    {
        return id == PadId || id == UnkId || id == BosId || id == EosId || id == OutputId;
    }
}