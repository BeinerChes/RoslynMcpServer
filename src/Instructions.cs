namespace RoslynMcpServer;

/// <summary>
/// Reads instruction content from external MD files.
/// Files are located in the Instructions folder relative to the executable.
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

        public static string[] Available => ["minimal", "standard", "tdd", "team"];

        public static string? Get(string templateName)
        {
            var name = templateName.ToLowerInvariant();
            if (!Available.Contains(name))
                return null;

            var filePath = Path.Combine(TemplatesPath, $"{name}.md");
            return ReadFile(filePath);
        }
    }

    public static class Topics
    {
        private static readonly string TopicsPath = Path.Combine(BasePath, "Topics");

        public static string[] Available => ["code", "git", "tdd", "pre-pr", "tools"];

        public static string? Get(string topicName)
        {
            var name = topicName.ToLowerInvariant() switch
            {
                "pre-pr" or "prepr" or "pr" => "pre-pr",
                _ => topicName.ToLowerInvariant()
            };

            if (!Available.Contains(name))
                return null;

            var filePath = Path.Combine(TopicsPath, $"{name}.md");
            return ReadFile(filePath);
        }
    }

    private static string? ReadFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Instruction file not found: {filePath}");
            return null;
        }

        try
        {
            return File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error reading instruction file {filePath}: {ex.Message}");
            return null;
        }
    }
}
