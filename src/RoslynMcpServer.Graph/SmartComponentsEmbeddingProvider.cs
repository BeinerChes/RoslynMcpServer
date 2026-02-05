using System.Numerics.Tensors;
using SmartComponents.LocalEmbeddings;

namespace RoslynMcpServer.Graph;

/// <summary>
/// Embedding provider using SmartComponents.LocalEmbeddings (all-MiniLM-L6-v2 model).
/// Zero configuration required - model downloads automatically on first use.
/// </summary>
public sealed class SmartComponentsEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly LocalEmbedder _embedder;

    public SmartComponentsEmbeddingProvider()
    {
        _embedder = new LocalEmbedder();
    }

    /// <inheritdoc />
    public int Dimensions => 384;

    /// <inheritdoc />
    public string ModelName => "all-MiniLM-L6-v2";

    /// <inheritdoc />
    public Task<float[]> EmbedAsync(string text)
    {
        var embedding = _embedder.Embed(text);
        return Task.FromResult(embedding.Values.ToArray());
    }

    /// <inheritdoc />
    public float Similarity(float[] a, float[] b)
    {
        // Compute cosine similarity manually
        return TensorPrimitives.CosineSimilarity(a.AsSpan(), b.AsSpan());
    }

    public void Dispose()
    {
        _embedder.Dispose();
    }
}
