using System.Text.Json;

namespace RoslynMcpServer;

/// <summary>
/// MCP tool for setting up Claude hooks in a project.
/// </summary>
public static partial class RoslynTools
{
    private static void RegisterSetupHooksTool(McpServer server)
    {
        server.RegisterTool(
            "roslyn_setup_hooks",
            new ToolDefinition
            {
                Description = "Sets up Claude hooks in a project to enforce calling roslyn_get_instructions before git and GitHub operations. Creates .claude/hooks/ directory and settings.json. Also creates or updates CLAUDE.md with the template content.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectPath = new
                        {
                            type = "string",
                            description = "Path to the project root directory where hooks should be installed"
                        },
                        template = new
                        {
                            type = "string",
                            description = "Template to use for CLAUDE.md. Default: 'claude'",
                            @enum = Instructions.Templates.Available
                        }
                    },
                    required = RequiredProjectPath
                }
            },
            async args =>
            {
                var projectPath = args?["projectPath"]?.GetValue<string>();
                var template = args?["template"]?.GetValue<string>() ?? "claude";

                if (string.IsNullOrEmpty(projectPath))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = "Error: projectPath is required" }
                        },
                        isError = true
                    };
                }

                if (!Directory.Exists(projectPath))
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = $"Error: Directory does not exist: {projectPath}" }
                        },
                        isError = true
                    };
                }

                try
                {
                    var result = SetupHooksInProject(projectPath, template);
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) }
                        }
                    };
                }
                catch (Exception ex)
                {
                    return new
                    {
                        content = new[]
                        {
                            new { type = "text", text = $"Error setting up hooks: {ex.Message}" }
                        },
                        isError = true
                    };
                }
            });
    }

    private static object SetupHooksInProject(string projectPath, string templateName)
    {
        var claudeDir = Path.Combine(projectPath, ".claude");
        var hooksDir = Path.Combine(claudeDir, "hooks");
        var plansDir = Path.Combine(claudeDir, "plans");
        var settingsFile = Path.Combine(claudeDir, "settings.json");
        var gitHookFile = Path.Combine(hooksDir, "enforce-git-instructions.py");
        var planHookFile = Path.Combine(hooksDir, "enforce-plan-instructions.py");
        var roslynSuggestHookFile = Path.Combine(hooksDir, "suggest-roslyn-for-csharp.py");
        var roslynReadHookFile = Path.Combine(hooksDir, "suggest-roslyn-for-read.py");
        var claudeMdFile = Path.Combine(projectPath, "CLAUDE.md");
        var gitignoreFile = Path.Combine(projectPath, ".gitignore");

        // Create directories
        Directory.CreateDirectory(hooksDir);
        Directory.CreateDirectory(plansDir);

        // Write the hook files (read from Instructions/Hooks/)
        File.WriteAllText(gitHookFile, GetHookContent("enforce-git-instructions.py"));
        File.WriteAllText(planHookFile, GetHookContent("enforce-plan-instructions.py"));
        File.WriteAllText(roslynSuggestHookFile, GetHookContent("suggest-roslyn-for-csharp.py"));
        File.WriteAllText(roslynReadHookFile, GetHookContent("suggest-roslyn-for-read.py"));

        // Handle .gitignore - add .claude/ and .roslyn-mcp/ entries
        var gitignoreAction = UpdateGitignore(gitignoreFile);

        // Handle CLAUDE.md
        var templateContent = Instructions.Templates.Get(templateName);
        var claudeMdAction = "none";

        if (templateContent != null)
        {
            if (File.Exists(claudeMdFile))
            {
                // Prepend template to existing CLAUDE.md if not already present
                var existingContent = File.ReadAllText(claudeMdFile);
                if (!existingContent.Contains("roslyn_get_instructions"))
                {
                    // Add separator and prepend
                    var newContent = templateContent + "\n\n---\n\n# Original CLAUDE.md Content\n\n" + existingContent;
                    File.WriteAllText(claudeMdFile, newContent);
                    claudeMdAction = "updated";
                }
                else
                {
                    claudeMdAction = "skipped (already contains roslyn instructions)";
                }
            }
            else
            {
                // Create new CLAUDE.md
                File.WriteAllText(claudeMdFile, templateContent);
                claudeMdAction = "created";
            }
        }

        // Create or update settings.json
        var settings = new Dictionary<string, object>();
        if (File.Exists(settingsFile))
        {
            try
            {
                var existingJson = File.ReadAllText(settingsFile);
                settings = JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson) ?? new();
            }
            catch
            {
                // If can't parse, start fresh
            }
        }

        // Add hooks configuration
        settings["hooks"] = new Dictionary<string, object>
        {
            ["PreToolUse"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["matcher"] = "Bash",
                    ["hooks"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["type"] = "command",
                            ["command"] = "python .claude/hooks/enforce-git-instructions.py"
                        },
                        new Dictionary<string, object>
                        {
                            ["type"] = "command",
                            ["command"] = "python .claude/hooks/enforce-plan-instructions.py"
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    ["matcher"] = "Edit",
                    ["hooks"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["type"] = "command",
                            ["command"] = "python .claude/hooks/suggest-roslyn-for-csharp.py"
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    ["matcher"] = "Write",
                    ["hooks"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["type"] = "command",
                            ["command"] = "python .claude/hooks/suggest-roslyn-for-csharp.py"
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    ["matcher"] = "Read",
                    ["hooks"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["type"] = "command",
                            ["command"] = "python .claude/hooks/suggest-roslyn-for-read.py"
                        }
                    }
                }
            }
        };

        File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings, JsonOptions));

        var filesCreated = new List<string> { gitHookFile, planHookFile, roslynSuggestHookFile, roslynReadHookFile, settingsFile };
        if (claudeMdAction == "created" || claudeMdAction == "updated")
        {
            filesCreated.Add(claudeMdFile);
        }
        if (gitignoreAction == "created" || gitignoreAction == "updated")
        {
            filesCreated.Add(gitignoreFile);
        }

        return new
        {
            success = true,
            message = "Hooks installed successfully",
            filesCreated = filesCreated.ToArray(),
            directoriesCreated = new[] { hooksDir, plansDir },
            claudeMd = new
            {
                action = claudeMdAction,
                path = claudeMdFile
            },
            gitignore = new
            {
                action = gitignoreAction,
                path = gitignoreFile
            },
            instructions = new[]
            {
                "The git hook requires calling roslyn_get_instructions(topic: \"git\") before any git commit or push.",
                "The plan hook requires calling roslyn_get_instructions(topic: \"plan\") before GitHub issue/PR operations.",
                "The C# edit hook requires calling roslyn_get_instructions(topic: \"tools\") before Edit/Write on .cs files.",
                "The C# read hook requires calling roslyn_get_instructions(topic: \"tools\") before Read on .cs files."
            }
        };
    }

    private static readonly string HooksPath = Path.Combine(
        AppContext.BaseDirectory,
        "Instructions",
        "Hooks");
    private static readonly string[] RequiredProjectPath = new[] { "projectPath" };

    private static string GetHookContent(string hookName)
    {
        var filePath = Path.Combine(HooksPath, hookName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Hook file not found: {filePath}");
        }
        return File.ReadAllText(filePath);
    }
}
