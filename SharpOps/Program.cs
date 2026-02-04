using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

        if (args.Length >= 1 && args[0] == "validate")
        {
            return ValidateResults(args.Skip(1).ToArray());
        }

        if (args.Length >= 1 && args[0] == "debug")
        {
            return DebugParse(args.Skip(1).ToArray());
        }

        if (args.Length >= 1 && args[0] == "export-tokens")
        {
            return ExportTokens(args.Skip(1).ToArray());
        }

        if (args.Length >= 1 && args[0] == "generate")
        {
            return GenerateTest(args.Skip(1).ToArray());
        }

        if (args.Length >= 1 && args[0] == "tokenize")
        {
            return TokenizerTest(args.Skip(1).ToArray());
        }

        if (args.Length >= 1 && args[0] == "benchmark")
        {
            return RunBenchmark(args.Skip(1).ToArray());
        }

        if (args.Length >= 1 && args[0] == "roundtrip")
        {
            return RoundtripTest(args.Skip(1).ToArray());
        }

        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  SharpOps <input-path> <output-folder> [options]");
            Console.Error.WriteLine("  SharpOps compile <jsonl-file> [line-number]");
            Console.Error.WriteLine("  SharpOps roundtrip <input-folder> <output-json> [--max <n>]");
            Console.Error.WriteLine("  SharpOps validate <validation_results.json>");
            Console.Error.WriteLine("  SharpOps debug <ops-string>");
            Console.Error.WriteLine("  SharpOps export-tokens <output-file>");
            Console.Error.WriteLine("  SharpOps generate <method-signature> [--model <path>] [--tokenizer <path>]");
            Console.Error.WriteLine("  SharpOps tokenize <text> [--tokenizer <path>]");
            Console.Error.WriteLine("  SharpOps benchmark [--model <path>] [--tokenizer <path>]");
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

    private static int ValidateResults(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: SharpOps validate <validation_results.json>");
            return 1;
        }

        var inputPath = args[0];
        var json = File.ReadAllText(inputPath);

        using var doc = JsonDocument.Parse(json);
        var results = new List<Dictionary<string, object?>>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var entry = new Dictionary<string, object?>
            {
                ["index"] = item.GetProperty("index").GetInt32(),
                ["input"] = item.GetProperty("input").GetString(),
                ["expected"] = item.GetProperty("expected").GetString(),
                ["generated"] = item.GetProperty("generated").GetString(),
                ["match"] = item.GetProperty("match").GetBoolean()
            };

            // Try to compile the generated ops
            var generated = item.GetProperty("generated").GetString() ?? "";

            // Add leading space if missing (for BPE tokenization)
            if (!generated.StartsWith(" "))
                generated = " " + generated;

            try
            {
                var sequence = SharpOpsSequence.ParseOps(generated);
                var code = SharpOpsCompiler.CompileToString(sequence);
                entry["compiled"] = code;
                entry["compileError"] = null;
            }
            catch (Exception ex)
            {
                entry["compiled"] = null;
                entry["compileError"] = ex.Message;
            }

            results.Add(entry);
        }

        // Write back
        var outputPath = Path.Combine(
            Path.GetDirectoryName(inputPath) ?? ".",
            Path.GetFileNameWithoutExtension(inputPath) + "_compiled.json");

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(results, options));

        // Summary
        var compiled = results.Count(r => r["compiled"] != null);
        var failed = results.Count(r => r["compileError"] != null);
        Console.WriteLine($"Validated {results.Count} results:");
        Console.WriteLine($"  Compiled successfully: {compiled}");
        Console.WriteLine($"  Failed to compile: {failed}");
        Console.WriteLine($"  Output: {outputPath}");

        return 0;
    }


    private static int DebugParse(string[] args)
    {
        var input = string.Join(" ", args);
        Console.WriteLine($"Input: {input}");
        Console.WriteLine();

        var sequence = SharpOpsSequence.ParseOps(input);
        Console.WriteLine($"Parsed {sequence.Ops.Count} ops:");
        foreach (var op in sequence.Ops)
        {
            Console.WriteLine($"  Kind={op.Kind}, Arg={op.Argument}, SymbolKind={op.SymbolKind}");
        }
        Console.WriteLine();

        var code = SharpOpsCompiler.CompileToString(sequence);
        Console.WriteLine("=== COMPILED ===");
        Console.WriteLine(code);
        return 0;
    }


    private static int ExportTokens(string[] args)
    {
        // Get all SyntaxKind values that we use in SharpOps
        var usedKinds = new HashSet<SyntaxKind>
        {
            // Statements
            SyntaxKind.Block, SyntaxKind.LocalDeclarationStatement, SyntaxKind.ExpressionStatement,
            SyntaxKind.ReturnStatement, SyntaxKind.ThrowStatement, SyntaxKind.IfStatement,
            SyntaxKind.ElseClause, SyntaxKind.WhileStatement, SyntaxKind.ForStatement,
            SyntaxKind.ForEachStatement, SyntaxKind.BreakStatement, SyntaxKind.ContinueStatement,
            SyntaxKind.TryStatement, SyntaxKind.CatchClause, SyntaxKind.FinallyClause,
            SyntaxKind.LockStatement, SyntaxKind.UsingStatement,
            
            // Expressions
            SyntaxKind.IdentifierName, SyntaxKind.NumericLiteralExpression, SyntaxKind.StringLiteralExpression,
            SyntaxKind.CharacterLiteralExpression, SyntaxKind.TrueLiteralExpression, SyntaxKind.FalseLiteralExpression,
            SyntaxKind.NullLiteralExpression, SyntaxKind.DefaultLiteralExpression,
            SyntaxKind.InvocationExpression, SyntaxKind.ObjectCreationExpression,
            SyntaxKind.SimpleMemberAccessExpression, SyntaxKind.ElementAccessExpression,
            SyntaxKind.ConditionalExpression, SyntaxKind.ConditionalAccessExpression,
            SyntaxKind.MemberBindingExpression, SyntaxKind.CastExpression, SyntaxKind.AwaitExpression,
            SyntaxKind.ThisExpression, SyntaxKind.BaseExpression, SyntaxKind.ThrowExpression,
            SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.AnonymousObjectCreationExpression, SyntaxKind.TupleExpression,
            SyntaxKind.DeclarationExpression, SyntaxKind.InterpolatedStringExpression,
            SyntaxKind.RangeExpression,
            
            // Patterns
            SyntaxKind.IsPatternExpression, SyntaxKind.DeclarationPattern, SyntaxKind.ConstantPattern,
            SyntaxKind.NotPattern,
            
            // Binary operators
            SyntaxKind.AddExpression, SyntaxKind.SubtractExpression, SyntaxKind.MultiplyExpression,
            SyntaxKind.DivideExpression, SyntaxKind.ModuloExpression,
            SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression,
            SyntaxKind.LessThanExpression, SyntaxKind.LessThanOrEqualExpression,
            SyntaxKind.GreaterThanExpression, SyntaxKind.GreaterThanOrEqualExpression,
            SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression,
            SyntaxKind.BitwiseAndExpression, SyntaxKind.BitwiseOrExpression, SyntaxKind.ExclusiveOrExpression,
            SyntaxKind.LeftShiftExpression, SyntaxKind.RightShiftExpression,
            SyntaxKind.CoalesceExpression, SyntaxKind.AsExpression, SyntaxKind.IsExpression,
            
            // Unary operators
            SyntaxKind.UnaryMinusExpression, SyntaxKind.UnaryPlusExpression,
            SyntaxKind.LogicalNotExpression, SyntaxKind.BitwiseNotExpression,
            SyntaxKind.PreIncrementExpression, SyntaxKind.PreDecrementExpression,
            SyntaxKind.PostIncrementExpression, SyntaxKind.PostDecrementExpression,
            
            // Assignment
            SyntaxKind.SimpleAssignmentExpression, SyntaxKind.AddAssignmentExpression,
            SyntaxKind.SubtractAssignmentExpression, SyntaxKind.MultiplyAssignmentExpression,
            SyntaxKind.DivideAssignmentExpression, SyntaxKind.ModuloAssignmentExpression,
            SyntaxKind.AndAssignmentExpression, SyntaxKind.OrAssignmentExpression,
            SyntaxKind.ExclusiveOrAssignmentExpression, SyntaxKind.LeftShiftAssignmentExpression,
            SyntaxKind.RightShiftAssignmentExpression, SyntaxKind.CoalesceAssignmentExpression,
            
            // Declarations
            SyntaxKind.VariableDeclaration, SyntaxKind.VariableDeclarator, SyntaxKind.EqualsValueClause,
            SyntaxKind.Argument, SyntaxKind.ArrowExpressionClause
        };

        // Convert to UPPERCASE strings
        var tokens = usedKinds
            .Select(k => k.ToString().ToUpperInvariant())
            .OrderBy(s => s)
            .ToList();

        if (args.Length >= 1)
        {
            File.WriteAllLines(args[0], tokens);
            Console.WriteLine($"Exported {tokens.Count} tokens to {args[0]}");
        }
        else
        {
            foreach (var token in tokens)
            {
                Console.WriteLine(token);
            }
            Console.WriteLine($"Total: {tokens.Count} tokens");
        }

        return 0;
    }


    private static int GenerateTest(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: SharpOps generate <method-signature> [--model <path>] [--tokenizer <path>] [--debug] [--nobos]");
            return 1;
        }

        var methodSignature = args[0];
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "sharptinycoder.onnx");
        var tokenizerPath = Path.Combine(AppContext.BaseDirectory, "Models", "tokenizer", "tokenizer.json");
        var debug = false;
        var noBos = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--model" when i + 1 < args.Length:
                    modelPath = args[++i];
                    break;
                case "--tokenizer" when i + 1 < args.Length:
                    tokenizerPath = args[++i];
                    break;
                case "--debug":
                    debug = true;
                    break;
                case "--nobos":
                    noBos = true;
                    break;
            }
        }

        Console.Error.WriteLine($"Model: {modelPath}");
        Console.Error.WriteLine($"Tokenizer: {tokenizerPath}");
        Console.Error.WriteLine($"NoBos: {noBos}");
        Console.Error.WriteLine();

        var prompt = SharpOps.Inference.SharpOpsService.BuildPrompt(methodSignature);
        Console.Error.WriteLine("=== PROMPT ===");
        Console.Error.WriteLine(prompt.Replace("\n", "\\n").Replace("\r", "\\r"));
        Console.Error.WriteLine();

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var inference = new SharpOps.Inference.SharpOpsInference(modelPath, tokenizerPath);
            var loadTime = sw.ElapsedMilliseconds;

            sw.Restart();
            string sharpOps;
            if (noBos)
                sharpOps = inference.GenerateNoBos(prompt);
            else if (debug)
                sharpOps = inference.GenerateDebug(prompt);
            else
                sharpOps = inference.Generate(prompt);
            var genTime = sw.ElapsedMilliseconds;

            Console.WriteLine("=== GENERATED SHARPOPS ===");
            Console.WriteLine(sharpOps);
            Console.WriteLine();

            try
            {
                var sequence = SharpOpsSequence.ParseOps(sharpOps, null);

                // Populate parameter names from signature
                var parenStart = methodSignature.IndexOf('(');
                var parenEnd = methodSignature.LastIndexOf(')');
                if (parenStart >= 0 && parenEnd > parenStart)
                {
                    var paramSection = methodSignature[(parenStart + 1)..parenEnd].Trim();
                    if (paramSection.Length > 0)
                    {
                        var paramTable = sequence.SymbolTables[Microsoft.CodeAnalysis.SymbolKind.Parameter];
                        foreach (var param in paramSection.Split(','))
                        {
                            var parts = param.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                paramTable.Add(parts[^1]);
                            }
                        }
                    }
                }

                var compiled = SharpOpsCompiler.CompileToString(sequence);

                Console.WriteLine("=== COMPILED C# ===");
                Console.WriteLine(compiled);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Compilation failed: {ex.Message}");
            }

            Console.Error.WriteLine();
            Console.Error.WriteLine($"Load time: {loadTime}ms");
            Console.Error.WriteLine($"Generation time: {genTime}ms");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }


    private static int TokenizerTest(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: SharpOps tokenize <text> [--tokenizer <path>]");
            return 1;
        }

        var text = args[0];
        var tokenizerPath = Path.Combine(AppContext.BaseDirectory, "Models", "tokenizer", "tokenizer.json");

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--tokenizer" && i + 1 < args.Length)
                tokenizerPath = args[++i];
        }

        var tokenizer = new SharpOps.Inference.SharpOpsTokenizer(tokenizerPath);

        Console.WriteLine($"Text: {text}");
        Console.WriteLine($"VocabSize: {tokenizer.VocabSize}");
        Console.WriteLine();

        var ids = tokenizer.Encode(text);
        Console.WriteLine($"Token IDs ({ids.Length}): {string.Join(", ", ids)}");
        Console.WriteLine();

        Console.WriteLine("Tokens:");
        foreach (var id in ids)
        {
            var token = tokenizer.GetToken(id);
            Console.WriteLine($"  {id}: '{token}'");
        }
        Console.WriteLine();

        var decoded = tokenizer.Decode(ids);
        Console.WriteLine($"Decoded: {decoded}");
        Console.WriteLine($"Match: {text == decoded}");

        return 0;
    }


    private static int RunBenchmark(string[] args)
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "sharptinycoder.onnx");
        var tokenizerPath = Path.Combine(AppContext.BaseDirectory, "tokenizer.json");

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--model" when i + 1 < args.Length:
                    modelPath = args[++i];
                    break;
                case "--tokenizer" when i + 1 < args.Length:
                    tokenizerPath = args[++i];
                    break;
            }
        }

        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"Model not found: {modelPath}");
            return 1;
        }

        if (!File.Exists(tokenizerPath))
        {
            Console.Error.WriteLine($"Tokenizer not found: {tokenizerPath}");
            return 1;
        }

        Inference.SharpOpsInference.RunBenchmark(modelPath, tokenizerPath);
        return 0;
    }


    private static int RoundtripTest(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: SharpOps roundtrip <input-folder> <output-json> [--max <n>]");
            return 1;
        }

        var inputFolder = args[0];
        var outputJson = args[1];
        var maxSamples = 100;

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--max" && i + 1 < args.Length)
                maxSamples = int.Parse(args[++i]);
        }

        var jsonlFiles = Directory.GetFiles(inputFolder, "*.jsonl");
        var results = new List<object>();
        var succeeded = 0;
        var failed = 0;

        foreach (var jsonlFile in jsonlFiles)
        {
            if (results.Count >= maxSamples) break;

            var lines = File.ReadAllLines(jsonlFile);
            foreach (var line in lines)
            {
                if (results.Count >= maxSamples) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    var input = root.GetProperty("input").GetString() ?? "";
                    var sharpOps = root.GetProperty("output").GetString() ?? "";
                    var originalCSharp = root.TryGetProperty("originalCSharp", out var origProp)
                        ? origProp.GetString() ?? ""
                        : "";
                    var strings = root.TryGetProperty("strings", out var strProp)
                        ? strProp.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                        : new List<string>();

                    // Load symbol tables
                    List<string> GetList(string name) => root.TryGetProperty(name, out var prop)
                        ? prop.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                        : new List<string>();

                    var locals = GetList("locals");
                    var parameters = GetList("parameters");
                    var fields = GetList("fields");
                    var methods = GetList("methods");
                    var namedTypes = GetList("namedTypes");
                    var properties = GetList("properties");

                    // Try to compile back to C#
                    string compiledCSharp;
                    string? error = null;
                    try
                    {
                        var sequence = SharpOpsSequence.ParseOps(sharpOps, strings);
                        // Populate symbol tables for resolving positional refs
                        sequence.SymbolTables[SymbolKind.Local].AddRange(locals);
                        sequence.SymbolTables[SymbolKind.Parameter].AddRange(parameters);
                        sequence.SymbolTables[SymbolKind.Field].AddRange(fields);
                        sequence.SymbolTables[SymbolKind.Method].AddRange(methods);
                        sequence.SymbolTables[SymbolKind.NamedType].AddRange(namedTypes);
                        sequence.SymbolTables[SymbolKind.Property].AddRange(properties);
                        compiledCSharp = SharpOpsCompiler.CompileToString(sequence);
                        succeeded++;
                    }
                    catch (Exception ex)
                    {
                        compiledCSharp = "";
                        error = ex.Message;
                        failed++;
                    }

                    results.Add(new
                    {
                        input,
                        originalCSharp,
                        sharpOps,
                        compiledCSharp,
                        error,
                        source = Path.GetFileName(jsonlFile)
                    });
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error parsing line: {ex.Message}");
                    failed++;
                }
            }
        }

        var output = new
        {
            totalSamples = results.Count,
            succeeded,
            failed,
            samples = results
        };

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(outputJson, System.Text.Json.JsonSerializer.Serialize(output, options));

        Console.WriteLine($"Roundtrip complete: {succeeded} succeeded, {failed} failed");
        Console.WriteLine($"Output: {outputJson}");

        return 0;
    }
}
