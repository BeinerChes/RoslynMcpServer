using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoslynMcpServer;

/// <summary>
/// Helper methods to reduce complexity in tool registration handlers.
/// </summary>
public static partial class RoslynTools
{
    // Note: CreateErrorResponse already exists in RoslynTools.Graph.cs

    /// <summary>
    /// Creates a standard success response for tool handlers.
    /// </summary>
    internal static object CreateSuccessResponse(object result, bool isError = false) => new
    {
        content = new[]
        {
            new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) }
        },
        isError
    };

    /// <summary>
    /// Validates that a required string parameter is present.
    /// </summary>
    private static bool TryGetRequiredString(JsonObject? args, string paramName, out string value, out object? errorResponse)
    {
        value = args?[paramName]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            errorResponse = CreateErrorResponse($"Error: {paramName} is required");
            return false;
        }
        errorResponse = null;
        return true;
    }

    /// <summary>
    /// Gets an optional string parameter with a default value.
    /// </summary>
    private static string GetOptionalString(JsonObject? args, string paramName, string defaultValue)
        => args?[paramName]?.GetValue<string>() ?? defaultValue;

    /// <summary>
    /// Gets an optional integer parameter with a default value.
    /// </summary>
    private static int GetOptionalInt(JsonObject? args, string paramName, int defaultValue)
        => args?[paramName]?.GetValue<int>() ?? defaultValue;

    /// <summary>
    /// Gets an optional boolean parameter with a default value.
    /// </summary>
    private static bool GetOptionalBool(JsonObject? args, string paramName, bool defaultValue)
        => args?[paramName]?.GetValue<bool>() ?? defaultValue;

    /// <summary>
    /// Validates that a required integer parameter is present and meets minimum value.
    /// </summary>
    private static bool TryGetRequiredInt(JsonObject? args, string paramName, int minValue, out int value, out object? errorResponse)
    {
        value = args?[paramName]?.GetValue<int>() ?? 0;
        if (value < minValue)
        {
            errorResponse = CreateErrorResponse($"Error: {paramName} must be >= {minValue}");
            return false;
        }
        errorResponse = null;
        return true;
    }

    /// <summary>
    /// Validates that a solution path exists.
    /// </summary>
    private static bool TryValidateSolutionPath(string solutionPath, out object? errorResponse)
    {
        if (!File.Exists(solutionPath))
        {
            errorResponse = CreateErrorResponse($"Error: Solution file not found: {solutionPath}");
            return false;
        }
        errorResponse = null;
        return true;
    }

    /// <summary>
    /// Gets an optional string array from JSON.
    /// </summary>
    private static List<string>? GetOptionalStringArray(JsonObject? args, string paramName)
        => args?[paramName]?.AsArray()?.Select(x => x?.GetValue<string>() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();

    /// <summary>
    /// Gets an optional double parameter with a default value.
    /// </summary>
    private static double GetOptionalDouble(JsonObject? args, string paramName, double defaultValue)
        => args?[paramName]?.GetValue<double>() ?? defaultValue;

    /// <summary>
    /// Parses a symbol kind filter from a string value.
    /// </summary>
    private static SymbolKindFilter ParseSymbolKindFilter(string value) => value.ToLowerInvariant() switch
    {
        "type" => SymbolKindFilter.Type,
        "member" => SymbolKindFilter.Member,
        "namespace" => SymbolKindFilter.Namespace,
        "typeandmember" => SymbolKindFilter.TypeAndMember,
        _ => SymbolKindFilter.All
    };

    /// <summary>
    /// Parses a match type from a string value.
    /// </summary>
    private static MatchType ParseMatchType(string value) => value.ToLowerInvariant() switch
    {
        "exact" => MatchType.Exact,
        "exactignorecase" => MatchType.ExactIgnoreCase,
        "prefix" => MatchType.Prefix,
        "suffix" => MatchType.Suffix,
        _ => MatchType.Contains
    };

}
