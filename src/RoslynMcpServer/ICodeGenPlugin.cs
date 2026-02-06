namespace RoslynMcpServer;

/// <summary>
/// Optional plugin interface for AI code generation.
/// Implemented by RoslynMcpServer.CodeGen when available.
/// </summary>
public interface ICodeGenPlugin
{
    /// <summary>Auto-generate a method body from signature + context.</summary>
    Task<(string FullMemberCode, bool Failed)> HandleAutoGenerateAsync(
        string solutionPath, string typeName, string memberSignature, string? comment);

    /// <summary>Collect finetune training data for a method.</summary>
    Task CollectFinetuneDataAsync(
        string solutionPath, string filePath, string typeName,
        string methodName, string? comment, string? parameterTypes);

    /// <summary>Register additional MCP tools (e.g. Finetune).</summary>
    void RegisterTools(McpServer server);
}
