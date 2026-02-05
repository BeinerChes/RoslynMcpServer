namespace SharpOps.Examples;

public record TaskItem
{

    /// <summary>
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// </summary>
    public string Title { get; init; } = "";

    /// <summary>
    /// </summary>
    public Priority Priority { get; init; }

    /// <summary>
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}