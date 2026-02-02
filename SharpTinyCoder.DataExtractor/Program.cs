namespace SharpTinyCoder.DataExtractor;

/// <summary>
/// CLI entry point for extracting training data from C# solutions.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: SharpTinyCoder.DataExtractor <solution-path> <output.jsonl> [options]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --min-lines <n>     Minimum body lines (default: 3)");
            Console.Error.WriteLine("  --max-lines <n>     Maximum body lines (default: 50)");
            Console.Error.WriteLine("  --no-internal       Exclude internal methods");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Example:");
            Console.Error.WriteLine("  SharpTinyCoder.DataExtractor MyApp.sln training-data.jsonl");
            return 1;
        }

        var solutionPath = Path.GetFullPath(args[0]);
        var outputPath = Path.GetFullPath(args[1]);

        var options = new ExtractionOptions();

        // Parse optional arguments
        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--min-lines" when i + 1 < args.Length:
                    options = options with { MinBodyLines = int.Parse(args[++i]) };
                    break;
                case "--max-lines" when i + 1 < args.Length:
                    options = options with { MaxBodyLines = int.Parse(args[++i]) };
                    break;
                case "--no-internal":
                    options = options with { IncludeInternal = false };
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 1;
            }
        }

        try
        {
            var extractor = new TrainingDataExtractor(options);
            var result = await extractor.ExtractAsync(solutionPath, outputPath);

            Console.WriteLine($"Extraction complete!");
            Console.WriteLine($"  Total methods scanned:   {result.TotalMethods}");
            Console.WriteLine($"  Extracted samples:       {result.ExtractedMethods}");
            Console.WriteLine($"  Skipped (no XML doc):    {result.SkippedNoDoc}");
            Console.WriteLine($"  Skipped (body length):   {result.SkippedBodyLength}");
            Console.WriteLine($"  Skipped (generated):     {result.SkippedGenerated}");
            Console.WriteLine($"  Skipped (accessibility): {result.SkippedAccessibility}");
            Console.WriteLine($"  Skipped (no body):       {result.SkippedNoBody}");
            Console.WriteLine($"  Output: {result.OutputFile}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
