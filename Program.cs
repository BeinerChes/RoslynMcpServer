using System.Reflection;
using System.Text.Json;
using RoslynMcpServer;

// Handle CLI arguments
if (args.Length > 0)
{
    switch (args[0].ToLowerInvariant())
    {
        case "--version":
        case "-v":
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine($"roslyn-mcp {version?.ToString(3) ?? "1.0.0"}");
            return;

        case "--init":
            await InitializeProject();
            return;

        case "--enable-hooks":
            EnableHooks();
            return;

        case "--enable-skills":
            EnableSkills();
            return;

        case "--help":
        case "-h":
            ShowHelp();
            return;

        default:
            Console.Error.WriteLine($"Unknown option: {args[0]}");
            ShowHelp();
            Environment.Exit(1);
            return;
    }
}

// Default: Run as MCP server
// MCP servers must use stdio for communication
// All logging goes to stderr to avoid interfering with the protocol
Console.Error.WriteLine("RoslynMcpServer starting...");

// Start HTTP server for hook token validation
using var hookServer = new HookValidationServer();
hookServer.Start();

var server = new McpServer();

// Register all tools
RoslynTools.RegisterAll(server);

await server.RunAsync();

// === CLI Command Implementations ===

static void ShowHelp()
{
    Console.WriteLine("""
        Roslyn MCP Server - Semantic C# analysis for Claude Code

        Usage: roslyn-mcp [command]

        Commands:
          (none)           Run as MCP server (default)
          --init           Initialize project (.mcp.json + CLAUDE.md)
          --enable-hooks   Enable Roslyn tool suggestions
          --enable-skills  Enable /architect skill
          --version, -v    Show version
          --help, -h       Show this help

        Quick Start:
          1. Extract to YourSolution/.roslyn-mcp/
          2. Run: roslyn-mcp --init
          3. Start Claude Code and run /mcp to verify
        """);
}

static async Task InitializeProject()
{
    var currentDir = Directory.GetCurrentDirectory();
    var exeDir = AppContext.BaseDirectory;

    // Check if we're in a solution directory
    var slnFiles = Directory.GetFiles(currentDir, "*.sln")
        .Concat(Directory.GetFiles(currentDir, "*.slnx"))
        .ToArray();

    if (slnFiles.Length == 0)
    {
        Console.Error.WriteLine("Warning: No .sln or .slnx file found in current directory.");
        Console.Error.WriteLine("Run this command from your C# solution directory.");
    }

    // Create .mcp.json
    var mcpJsonPath = Path.Combine(currentDir, ".mcp.json");
    if (File.Exists(mcpJsonPath))
    {
        Console.WriteLine(".mcp.json already exists - skipping");
    }
    else
    {
        // Calculate relative path from solution to exe
        var exeName = Path.GetFileName(Environment.ProcessPath) ?? "roslyn-mcp.exe";
        var relativePath = Path.GetRelativePath(currentDir, Path.Combine(exeDir, exeName));
        var mcpConfig = new
        {
            mcpServers = new
            {
                roslyn = new
                {
                    type = "stdio",
                    command = relativePath.Replace("\\", "/"),
                    args = Array.Empty<string>()
                }
            }
        };
        var json = JsonSerializer.Serialize(mcpConfig, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(mcpJsonPath, json);
        Console.WriteLine($"Created: .mcp.json");
    }

    // Create minimal CLAUDE.md
    var claudeMdPath = Path.Combine(currentDir, "CLAUDE.md");
    if (File.Exists(claudeMdPath))
    {
        Console.WriteLine("CLAUDE.md already exists - skipping");
    }
    else
    {
        var claudeMd = """
            # CLAUDE.md - C# Development with Roslyn MCP

            ## Use Roslyn Tools for C# Code

            When working with C# code, prefer Roslyn MCP tools over text search:

            | Instead of... | Use... |
            |---------------|--------|
            | `grep "MethodName"` | `roslyn_find_symbol` |
            | Reading entire files | `roslyn_get_method_body` |
            | Find/replace rename | `roslyn_rename_symbol` |

            ## Available Tools

            - `roslyn_find_symbol` - Find types, methods, properties
            - `roslyn_get_callers` - Find all call sites
            - `roslyn_get_type_members` - List class members
            - `roslyn_get_method_body` - Get method source code
            - `roslyn_rename_symbol` - Rename across solution
            - `roslyn_get_diagnostics` - Get compiler errors/warnings

            ## Build Commands

            ```bash
            dotnet build    # Build solution
            dotnet test     # Run tests
            ```
            """;
        await File.WriteAllTextAsync(claudeMdPath, claudeMd);
        Console.WriteLine($"Created: CLAUDE.md");
    }

    Console.WriteLine();
    Console.WriteLine("Project initialized! Next steps:");
    Console.WriteLine("  1. Start Claude Code: claude");
    Console.WriteLine("  2. Verify connection: /mcp");
    Console.WriteLine();
    Console.WriteLine("Optional:");
    Console.WriteLine("  roslyn-mcp --enable-hooks   # Suggest Roslyn tools");
    Console.WriteLine("  roslyn-mcp --enable-skills  # Add /architect skill");
}

static void EnableHooks()
{
    var currentDir = Directory.GetCurrentDirectory();
    var exeDir = AppContext.BaseDirectory;

    // Create .claude directory
    var claudeDir = Path.Combine(currentDir, ".claude");
    var hooksDir = Path.Combine(claudeDir, "hooks");
    Directory.CreateDirectory(hooksDir);

    // Copy hook files from exe directory
    var sourceHooksDir = Path.Combine(exeDir, "hooks");
    if (!Directory.Exists(sourceHooksDir))
    {
        Console.Error.WriteLine($"Error: hooks/ folder not found at {sourceHooksDir}");
        Console.Error.WriteLine("Make sure hooks/ is in the same directory as roslyn-mcp.exe");
        Environment.Exit(1);
        return;
    }

    foreach (var hookFile in Directory.GetFiles(sourceHooksDir, "*.py"))
    {
        var destPath = Path.Combine(hooksDir, Path.GetFileName(hookFile));
        File.Copy(hookFile, destPath, overwrite: true);
        Console.WriteLine($"Copied: {Path.GetFileName(hookFile)}");
    }

    // Create settings.json with hook configuration
    var settingsPath = Path.Combine(claudeDir, "settings.json");
    var settings = new
    {
        hooks = new
        {
            PreToolUse = new object[]
            {
                new
                {
                    matcher = "Read",
                    hooks = new[] { new { type = "command", command = "python .claude/hooks/suggest-roslyn-for-read.py" } }
                },
                new
                {
                    matcher = "Edit",
                    hooks = new[] { new { type = "command", command = "python .claude/hooks/suggest-roslyn-for-csharp.py" } }
                },
                new
                {
                    matcher = "Write",
                    hooks = new[] { new { type = "command", command = "python .claude/hooks/suggest-roslyn-for-csharp.py" } }
                }
            }
        }
    };
    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(settingsPath, json);
    Console.WriteLine($"Created: .claude/settings.json");

    Console.WriteLine();
    Console.WriteLine("Hooks enabled! Claude will now suggest Roslyn tools for C# files.");
}

static void EnableSkills()
{
    var currentDir = Directory.GetCurrentDirectory();
    var exeDir = AppContext.BaseDirectory;

    // Create .claude/skills directory
    var skillsDir = Path.Combine(currentDir, ".claude", "skills");
    Directory.CreateDirectory(skillsDir);

    // Copy skills from exe directory
    var sourceSkillsDir = Path.Combine(exeDir, "skills");
    if (!Directory.Exists(sourceSkillsDir))
    {
        Console.Error.WriteLine($"Error: skills/ folder not found at {sourceSkillsDir}");
        Console.Error.WriteLine("Make sure skills/ is in the same directory as roslyn-mcp.exe");
        Environment.Exit(1);
        return;
    }

    foreach (var skillFolder in Directory.GetDirectories(sourceSkillsDir))
    {
        var skillName = Path.GetFileName(skillFolder);
        var destFolder = Path.Combine(skillsDir, skillName);
        Directory.CreateDirectory(destFolder);

        foreach (var file in Directory.GetFiles(skillFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(skillFolder, file);
            var destPath = Path.Combine(destFolder, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(file, destPath, overwrite: true);
        }
        Console.WriteLine($"Installed skill: {skillName}");
    }

    Console.WriteLine();
    Console.WriteLine("Skills enabled! Available commands:");
    Console.WriteLine("  /architect  - Deep code analysis");
    Console.WriteLine("");
}
