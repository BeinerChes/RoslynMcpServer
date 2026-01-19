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
                Description = "Sets up Claude hooks in a project to enforce calling roslyn_get_instructions before git operations. Creates .claude/hooks/ directory and settings.json. Also creates or updates CLAUDE.md with the template content.",
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
                            description = "Template to use for CLAUDE.md. Default: 'standard'",
                            @enum = new[] { "standard" }
                        }
                    },
                    required = new[] { "projectPath" }
                }
            },
            async args =>
            {
                var projectPath = args?["projectPath"]?.GetValue<string>();
                var template = args?["template"]?.GetValue<string>() ?? "standard";

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
        var settingsFile = Path.Combine(claudeDir, "settings.json");
        var hookFile = Path.Combine(hooksDir, "enforce-git-instructions.py");
        var claudeMdFile = Path.Combine(projectPath, "CLAUDE.md");

        // Create directories
        Directory.CreateDirectory(hooksDir);

        // Write the hook file
        File.WriteAllText(hookFile, GetEnforceGitInstructionsHook());

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
                        }
                    }
                }
            }
        };

        File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        var filesCreated = new List<string> { hookFile, settingsFile };
        if (claudeMdAction == "created" || claudeMdAction == "updated")
        {
            filesCreated.Add(claudeMdFile);
        }

        return new
        {
            success = true,
            message = "Hooks installed successfully",
            filesCreated = filesCreated.ToArray(),
            claudeMd = new
            {
                action = claudeMdAction,
                path = claudeMdFile
            },
            instructions = "The hook will now require calling roslyn_get_instructions(topic: \"git\") before any git commit or push."
        };
    }

    private static string GetEnforceGitInstructionsHook()
    {
        return @"#!/usr/bin/env python3
""""""
Hook: Enforce calling roslyn_get_instructions(""git"") before git commit/push.

This hook validates that a git token was generated by the MCP server
before allowing git commit/push operations.

Flow:
1. roslyn_get_instructions(""git"") generates a token and writes to ~/.claude/roslyn-git-token
2. This hook reads the token and validates it via HTTP endpoint
3. If valid (< 5 min old), commit/push is allowed
""""""
import sys
import json
import re
import os
import urllib.request
import urllib.error
import urllib.parse

def get_hook_port():
    """"""Read the port number from the port file.""""""
    port_file = os.path.join(os.path.expanduser('~'), '.claude', 'roslyn-hook-port')
    try:
        with open(port_file, 'r') as f:
            return int(f.read().strip())
    except:
        return None

def get_git_token():
    """"""Read the git token from the token file.""""""
    token_file = os.path.join(os.path.expanduser('~'), '.claude', 'roslyn-git-token')
    try:
        with open(token_file, 'r') as f:
            return f.read().strip()
    except:
        return None

def validate_token_http(token, port):
    """"""Validate token via HTTP endpoint.""""""
    try:
        encoded_token = urllib.parse.quote(token, safe='')
        url = f'http://localhost:{port}/validate?token={encoded_token}&topic=git'
        with urllib.request.urlopen(url, timeout=5) as response:
            data = json.loads(response.read().decode('utf-8'))
            return data.get('valid', False), data.get('message', 'Unknown error')
    except urllib.error.URLError as e:
        return False, f'Cannot connect to validation server: {e.reason}'
    except Exception as e:
        return False, f'Validation error: {e}'

def validate_token_local(token):
    """"""Fallback: validate token locally by checking timestamp.""""""
    try:
        parts = token.split('.')
        if len(parts) != 4:
            return False, 'Invalid token format'

        timestamp = int(parts[1])
        import time
        age_seconds = time.time() - timestamp

        if age_seconds > 300:  # 5 minutes
            return False, f'Token expired ({age_seconds/60:.1f} minutes old)'

        if age_seconds < 0:
            return False, 'Token timestamp is in the future'

        return True, f'Valid (local validation, {age_seconds:.0f}s old)'
    except Exception as e:
        return False, f'Local validation error: {e}'

def main():
    try:
        data = json.load(sys.stdin)
    except:
        sys.exit(0)  # Can't parse input, allow

    command = data.get('tool_input', {}).get('command', '')

    # Check if this is a git commit or push command
    if not re.search(r'git\s+(commit|push)', command):
        sys.exit(0)  # Not a commit/push, allow

    # Get the token
    token = get_git_token()
    if not token:
        print('BLOCKED: No git token found.', file=sys.stderr)
        print('Run: roslyn_get_instructions(topic: ""git"") before committing.', file=sys.stderr)
        sys.exit(2)

    # Try HTTP validation first
    port = get_hook_port()
    if port:
        valid, message = validate_token_http(token, port)
    else:
        # Fallback to local validation
        valid, message = validate_token_local(token)

    if valid:
        sys.exit(0)  # Allow
    else:
        print(f'BLOCKED: {message}', file=sys.stderr)
        print('Run: roslyn_get_instructions(topic: ""git"") before committing.', file=sys.stderr)
        sys.exit(2)

if __name__ == '__main__':
    main()
";
    }
}
