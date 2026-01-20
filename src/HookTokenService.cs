using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RoslynMcpServer;

/// <summary>
/// Service for generating and validating hook tokens.
/// Tokens are used to verify that roslyn_get_instructions was called before git operations.
/// </summary>
public class HookTokenService
{
    private static readonly Lazy<HookTokenService> _instance = new(() => new HookTokenService());
    public static HookTokenService Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, TokenInfo> _tokens = new();
    private readonly Dictionary<string, TimeSpan> _tokenExpirations = new()
    {
        ["git"] = TimeSpan.FromMinutes(5),
        ["plan"] = TimeSpan.FromMinutes(10)
    };
    private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(5);
    private readonly byte[] _secretKey;

    private HookTokenService()
    {
        // Generate a random secret key for this session
        _secretKey = RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>
    /// Generates a new token for the specified topic.
    /// </summary>
    public string GenerateToken(string topic)
    {
        var tokenId = Guid.NewGuid().ToString("N")[..16];
        var timestamp = DateTimeOffset.UtcNow;
        var info = new TokenInfo(topic, timestamp);

        _tokens[tokenId] = info;

        // Clean up old tokens
        CleanupExpiredTokens();

        // Create signed token: tokenId.timestamp.signature
        var payload = $"{tokenId}.{timestamp.ToUnixTimeSeconds()}.{topic}";
        var signature = ComputeSignature(payload);

        return $"{payload}.{signature}";
    }

    /// <summary>
    /// Validates a token and returns the result.
    /// </summary>
    public TokenValidationResult ValidateToken(string token, string expectedTopic)
    {
        if (string.IsNullOrEmpty(token))
            return new TokenValidationResult(false, "Token is empty");

        var parts = token.Split('.');
        if (parts.Length != 4)
            return new TokenValidationResult(false, "Invalid token format");

        var tokenId = parts[0];
        var timestampStr = parts[1];
        var topic = parts[2];
        var signature = parts[3];

        // Verify signature
        var payload = $"{tokenId}.{timestampStr}.{topic}";
        var expectedSignature = ComputeSignature(payload);
        if (signature != expectedSignature)
            return new TokenValidationResult(false, "Invalid signature");

        // Verify timestamp
        if (!long.TryParse(timestampStr, out var unixTime))
            return new TokenValidationResult(false, "Invalid timestamp");

        var tokenTime = DateTimeOffset.FromUnixTimeSeconds(unixTime);
        var age = DateTimeOffset.UtcNow - tokenTime;

        var expiration = _tokenExpirations.GetValueOrDefault(expectedTopic, _defaultExpiration);
        if (age > expiration)
            return new TokenValidationResult(false, $"Token expired ({age.TotalMinutes:F1} minutes old, max {expiration.TotalMinutes} minutes)");

        // Verify topic
        if (!string.Equals(topic, expectedTopic, StringComparison.OrdinalIgnoreCase))
            return new TokenValidationResult(false, $"Token topic mismatch (expected '{expectedTopic}', got '{topic}')");

        // Verify token exists in our registry (prevents replay with forged tokens)
        if (!_tokens.TryGetValue(tokenId, out var info))
            return new TokenValidationResult(false, "Token not found in registry");

        return new TokenValidationResult(true, "Valid", topic, tokenTime, age);
    }

    private string ComputeSignature(string payload)
    {
        using var hmac = new HMACSHA256(_secretKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash)[..22]; // Truncate for shorter token
    }

    private void CleanupExpiredTokens()
    {
        var maxExpiration = _tokenExpirations.Values.Max();
        var cutoff = DateTimeOffset.UtcNow - maxExpiration - TimeSpan.FromMinutes(1);
        var expiredKeys = _tokens
            .Where(kvp => kvp.Value.CreatedAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _tokens.TryRemove(key, out _);
        }
    }

    private record TokenInfo(string Topic, DateTimeOffset CreatedAt);
}

public record TokenValidationResult(
    bool IsValid,
    string Message,
    string? Topic = null,
    DateTimeOffset? CreatedAt = null,
    TimeSpan? Age = null);
