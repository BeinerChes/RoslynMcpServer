namespace RoslynMcpServer;

/// <summary>
/// Reads instruction content from external MD files.
/// Files are located in the Instructions folder relative to the executable.
/// Users can add custom topics by adding .md files to Instructions/Topics/.
/// Issues: #15, #17, #19
/// </summary>
public static class Instructions
{
    private static readonly string BasePath = Path.Combine(
        AppContext.BaseDirectory,
        "Instructions");

    public static class Templates
    {
        private static readonly string TemplatesPath = Path.Combine(BasePath, "Templates");

        public static string[] Available => GetAvailableFiles(TemplatesPath);

        public static string? Get(string templateName)
        {
            var filePath = Path.Combine(TemplatesPath, $"{templateName.ToLowerInvariant()}.md");
            return ReadFile(filePath);
        }
    }

    public static class Topics
    {
        private static readonly string TopicsPath = Path.Combine(BasePath, "Topics");

        public static string[] Available => GetAvailableFiles(TopicsPath);

        public static string? Get(string topicName)
        {
            var filePath = Path.Combine(TopicsPath, $"{topicName.ToLowerInvariant()}.md");
            return ReadFile(filePath);
        }
    }

    private static string[] GetAvailableFiles(string path)
    {
        if (!Directory.Exists(path))
            return [];

        return Directory.GetFiles(path, "*.md")
            .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
            .OrderBy(n => n)
            .ToArray();
    }

    private static string? ReadFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            return File.ReadAllText(filePath);
        }
        catch
        {
            return null;
        }
    }
}
