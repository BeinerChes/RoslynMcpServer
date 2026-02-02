using System.Text.Json;

namespace SharpOps;

/// <summary>
/// CLI entry point for SharpOps training data generation.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "compile")
        {
            return CompileTest(args.Skip(1).ToArray());
        }

        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  SharpOps <input-path> <output-folder> [options]");
            Console.Error.WriteLine("  SharpOps compile <jsonl-file> [line-number]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Input can be: .sln, .slnx, .csproj, or directory with .csproj files");
            Console.Error.WriteLine("Output folder will contain ProjectName.jsonl for each project");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --min-statements <n>  Minimum statements in method (default: 3)");
            Console.Error.WriteLine("  --max-ops <n>         Maximum ops in sequence (default: 100)");
            Console.Error.WriteLine("  --max-string <n>      Maximum string length (default: 100)");
            Console.Error.WriteLine("  --include-tests       Include test projects");
            Console.Error.WriteLine("  --no-require-doc      Don't require XML documentation");
            return 1;
        }

        var solutionPath = Path.GetFullPath(args[0]);
        var outputPath = Path.GetFullPath(args[1]);

        var options = new TrainingDataGenerator.GenerationOptions();

        // Parse optional arguments
        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--min-statements" when i + 1 < args.Length:
                    options = options with { MinStatements = int.Parse(args[++i]) };
                    break;
                case "--max-ops" when i + 1 < args.Length:
                    options = options with { MaxOps = int.Parse(args[++i]) };
                    break;
                case "--max-string" when i + 1 < args.Length:
                    options = options with { MaxStringLength = int.Parse(args[++i]) };
                    break;
                case "--include-tests":
                    options = options with { IncludeTests = true };
                    break;
                case "--no-require-doc":
                    options = options with { RequireXmlDoc = false };
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 1;
            }
        }

        try
        {
            var generator = new TrainingDataGenerator();
            var result = await generator.GenerateAsync(solutionPath, outputPath, options);

            Console.WriteLine($"Generation complete!");
            Console.WriteLine($"  Total methods scanned:   {result.TotalMethods}");
            Console.WriteLine($"  Extracted samples:       {result.ExtractedMethods}");
            Console.WriteLine($"  Skipped (no XML doc):    {result.SkippedNoDoc}");
            Console.WriteLine($"  Skipped (too short):     {result.SkippedTooShort}");
            Console.WriteLine($"  Skipped (too long):      {result.SkippedTooLong}");
            Console.WriteLine($"  Skipped (long strings):  {result.SkippedLongStrings}");
            Console.WriteLine($"  Skipped (generated):     {result.SkippedGenerated}");
            Console.WriteLine($"  Skipped (tests):         {result.SkippedTests}");
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

    private static int CompileTest(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: SharpOps compile <jsonl-file> [line-number]");
            return 1;
        }

        var jsonlPath = args[0];
        var lineNum = args.Length > 1 ? int.Parse(args[1]) : 1;

        var lines = File.ReadAllLines(jsonlPath);
        if (lineNum < 1 || lineNum > lines.Length)
        {
            Console.Error.WriteLine($"Line {lineNum} out of range (1-{lines.Length})");
            return 1;
        }

        var json = lines[lineNum - 1];
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var opsString = root.GetProperty("output").GetString() ?? "";
        var strings = new List<string>();
        if (root.TryGetProperty("strings", out var stringsEl))
        {
            foreach (var s in stringsEl.EnumerateArray())
            {
                strings.Add(s.GetString() ?? "");
            }
        }

        Console.WriteLine("=== INPUT ===");
        Console.WriteLine(root.GetProperty("input").GetString());
        Console.WriteLine();
        Console.WriteLine("=== OPS ===");
        Console.WriteLine(opsString);
        Console.WriteLine();
        Console.WriteLine("=== STRINGS ===");
        for (int i = 0; i < strings.Count; i++)
        {
            Console.WriteLine($"  ${i}: \"{strings[i]}\"");
        }
        Console.WriteLine();

        try
        {
            var sequence = SharpOpsSequence.ParseOps(opsString, strings);
            var code = SharpOpsCompiler.CompileToString(sequence);
            Console.WriteLine("=== COMPILED C# ===");
            Console.WriteLine(code);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Compile error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
