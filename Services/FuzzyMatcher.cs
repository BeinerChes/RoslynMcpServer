using RoslynMcpServer.Graph;

namespace RoslynMcpServer.Services;

/// <summary>
/// Provides fuzzy string matching utilities for symbol name suggestions.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Computes the Levenshtein edit distance between two strings (case-insensitive).
    /// </summary>
    public static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var dp = new int[a.Length + 1, b.Length + 1];

        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[a.Length, b.Length];
    }

    /// <summary>
    /// Checks if pattern characters appear in order within target (case-insensitive).
    /// Useful for matching abbreviations like "GTM" against "GetTypeMembers".
    /// </summary>
    public static bool IsSubsequence(string pattern, string target)
    {
        var pi = 0;
        for (var ti = 0; ti < target.Length && pi < pattern.Length; ti++)
        {
            if (char.ToLowerInvariant(pattern[pi]) == char.ToLowerInvariant(target[ti]))
                pi++;
        }
        return pi == pattern.Length;
    }

    /// <summary>
    /// Finds symbols similar to the pattern using Levenshtein distance and subsequence matching.
    /// Deduplicates by Name, applies a subsequence bonus, and filters by distance threshold.
    /// </summary>
    public static List<(string Name, string QualifiedName, int Score)> FindSimilar(
    string pattern, IEnumerable<SymbolRecord> symbols, int maxResults = 5)
    {
        var maxDistance = Math.Max(3, pattern.Length / 3);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(string Name, string QualifiedName, int Score)>();

        foreach (var sym in symbols)
        {
            if (!seen.Add(sym.Name))
                continue;

            var distance = LevenshteinDistance(pattern, sym.Name);
            var isSubseq = IsSubsequence(pattern, sym.Name);
            var isInitials = isSubseq && IsInitialsMatch(pattern, sym.Name);

            int score;
            if (isInitials)
            {
                // Initials match (e.g., "GTM" → "GetTypeMembers"): strong bonus
                score = -pattern.Length;
            }
            else if (isSubseq && distance <= maxDistance)
            {
                // Subsequence + close edit distance: good match
                score = distance - pattern.Length;
            }
            else if (distance <= maxDistance)
            {
                // Edit distance only
                score = distance;
            }
            else if (isSubseq && sym.Name.Length <= pattern.Length * 2)
            {
                // Subsequence with moderate length difference (e.g., "GetTypMembers" → "GetTypeMembersAsync")
                score = distance;
            }
            else
            {
                continue;
            }

            candidates.Add((sym.Name, sym.QualifiedName, score));
        }

        return candidates
            .OrderBy(c => c.Score)
            .ThenBy(c => c.Name)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Checks if the pattern matches the initials (uppercase/word-boundary letters) of the target. For example, "GTM" matches "GetTypeMembers".
    /// </summary>
    /// <param name="pattern"></param>
    /// <param name="target"></param>
    /// <returns></returns>
    public static bool IsInitialsMatch(string pattern, string target)
    {
        // Extract initials: first char + every uppercase letter
        var initials = new List<char>();
        for (var i = 0; i < target.Length; i++)
        {
            if (i == 0 || char.IsUpper(target[i]))
                initials.Add(char.ToLowerInvariant(target[i]));
        }

        if (pattern.Length > initials.Count) return false;

        // Check if pattern matches a prefix of the initials
        for (var i = 0; i < pattern.Length; i++)
        {
            if (char.ToLowerInvariant(pattern[i]) != initials[i])
                return false;
        }
        return true;
    }
}
