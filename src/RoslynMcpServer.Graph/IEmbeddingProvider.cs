namespace RoslynMcpServer.Graph;

/// <summary>
/// Interface for generating text embeddings for semantic search.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Gets the number of dimensions in the embedding vector.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Gets the name/identifier of the embedding model.
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    /// <param name="text">The text to embed</param>
    /// <returns>A float array representing the embedding vector</returns>
    Task<float[]> EmbedAsync(string text);

    /// <summary>
    /// Computes the cosine similarity between two embedding vectors.
    /// </summary>
    /// <param name="a">First embedding vector</param>
    /// <param name="b">Second embedding vector</param>
    /// <returns>Similarity score between 0 and 1</returns>
    float Similarity(float[] a, float[] b);
}
