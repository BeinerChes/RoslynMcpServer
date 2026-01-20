# Roslyn MCP Server - Setup Script for Windows
# Run this from your C# solution directory:
#   powershell -ExecutionPolicy Bypass -File path\to\setup.ps1

param(
    [string]$RoslynMcpPath = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

Write-Host "Roslyn MCP Server - Setup" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host ""

# Verify we're in a solution directory
$slnFiles = Get-ChildItem -Path "." -Filter "*.sln" -File
if ($slnFiles.Count -eq 0) {
    Write-Host "ERROR: No .sln file found in current directory." -ForegroundColor Red
    Write-Host "Please run this script from your C# solution directory." -ForegroundColor Yellow
    exit 1
}

Write-Host "Found solution: $($slnFiles[0].Name)" -ForegroundColor Green

# Create directories
Write-Host ""
Write-Host "Creating directories..." -ForegroundColor White
$claudeDir = ".claude"
$hooksDir = Join-Path $claudeDir "hooks"
$plansDir = Join-Path $claudeDir "plans"

New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
New-Item -ItemType Directory -Path $plansDir -Force | Out-Null
Write-Host "  Created: $hooksDir" -ForegroundColor Gray
Write-Host "  Created: $plansDir" -ForegroundColor Gray

# Copy hook files
Write-Host ""
Write-Host "Installing hooks..." -ForegroundColor White
$sourceHooksDir = Join-Path $RoslynMcpPath "Instructions\Hooks"

if (Test-Path $sourceHooksDir) {
    Copy-Item -Path (Join-Path $sourceHooksDir "enforce-git-instructions.py") -Destination $hooksDir -Force
    Copy-Item -Path (Join-Path $sourceHooksDir "enforce-plan-instructions.py") -Destination $hooksDir -Force
    Write-Host "  Installed: enforce-git-instructions.py" -ForegroundColor Gray
    Write-Host "  Installed: enforce-plan-instructions.py" -ForegroundColor Gray
} else {
    Write-Host "  ERROR: Hook source files not found at $sourceHooksDir" -ForegroundColor Red
    exit 1
}

# Create settings.json
Write-Host ""
Write-Host "Configuring Claude Code hooks..." -ForegroundColor White
$settingsFile = Join-Path $claudeDir "settings.json"
$settings = @{
    hooks = @{
        PreToolUse = @(
            @{
                matcher = "Bash"
                hooks = @(
                    @{
                        type = "command"
                        command = "python .claude/hooks/enforce-git-instructions.py"
                    },
                    @{
                        type = "command"
                        command = "python .claude/hooks/enforce-plan-instructions.py"
                    }
                )
            }
        )
    }
}
$settings | ConvertTo-Json -Depth 10 | Set-Content $settingsFile
Write-Host "  Created: $settingsFile" -ForegroundColor Gray

# Create CLAUDE.md if not exists
Write-Host ""
$claudeMdFile = "CLAUDE.md"
if (Test-Path $claudeMdFile) {
    Write-Host "CLAUDE.md already exists - skipping" -ForegroundColor Yellow
} else {
    Write-Host "Creating CLAUDE.md..." -ForegroundColor White
    $templateFile = Join-Path $RoslynMcpPath "Instructions\Templates\standard.md"
    if (Test-Path $templateFile) {
        Copy-Item -Path $templateFile -Destination $claudeMdFile
        Write-Host "  Created: $claudeMdFile" -ForegroundColor Gray
    } else {
        Write-Host "  WARNING: Template not found, creating minimal CLAUDE.md" -ForegroundColor Yellow
        @"
# CLAUDE.md - C# Development with Roslyn MCP

## MANDATORY: Start Every Session with Plan Instructions

**Before doing anything else**, call ``roslyn_get_instructions("plan")`` and follow those instructions.

## MANDATORY: Get Instructions Before Operations

| Before doing this... | Call with topic |
|---------------------|-----------------|
| Starting or resuming a task | ``"plan"`` |
| Using any Roslyn tool | ``"tools"`` |
| Making any code change | ``"git"`` |
| Modifying C# code | ``"code"`` |
| Writing or running tests | ``"tdd"`` |
| Creating a pull request | ``"pre-pr"`` |

## Build Commands

``````bash
dotnet build        # Build
dotnet test         # Run tests
``````
"@ | Set-Content $claudeMdFile
        Write-Host "  Created: $claudeMdFile (minimal)" -ForegroundColor Gray
    }
}

# Create .mcp.json if not exists
Write-Host ""
$mcpJsonFile = ".mcp.json"
if (Test-Path $mcpJsonFile) {
    Write-Host ".mcp.json already exists - skipping" -ForegroundColor Yellow
} else {
    Write-Host "Creating .mcp.json..." -ForegroundColor White
    $mcpConfig = @{
        mcpServers = @{
            roslyn = @{
                type = "stdio"
                command = "dotnet"
                args = @("run", "--project", $RoslynMcpPath)
            }
        }
    }
    $mcpConfig | ConvertTo-Json -Depth 10 | Set-Content $mcpJsonFile
    Write-Host "  Created: $mcpJsonFile" -ForegroundColor Gray
}

# Summary
Write-Host ""
Write-Host "Setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Files created:" -ForegroundColor White
Write-Host "  .claude/hooks/enforce-git-instructions.py" -ForegroundColor Gray
Write-Host "  .claude/hooks/enforce-plan-instructions.py" -ForegroundColor Gray
Write-Host "  .claude/settings.json" -ForegroundColor Gray
Write-Host "  .claude/plans/ (directory)" -ForegroundColor Gray
if (-not (Test-Path $claudeMdFile -PathType Leaf)) {
    Write-Host "  CLAUDE.md" -ForegroundColor Gray
}
if (-not (Test-Path $mcpJsonFile -PathType Leaf)) {
    Write-Host "  .mcp.json" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Start Claude Code in this directory: claude" -ForegroundColor White
Write-Host "  2. Verify MCP connection: /mcp" -ForegroundColor White
Write-Host "  3. Start working!" -ForegroundColor White
Write-Host ""
