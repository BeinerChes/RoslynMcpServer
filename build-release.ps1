# Build Release Package for Roslyn MCP Server
# Creates: roslyn-mcp-win-x64.zip

param(
    [string]$OutputDir = ".\release",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot

Write-Host "Building Roslyn MCP Server Release Package" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

# Clean output directory
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null

$PackageDir = Join-Path $OutputDir "roslyn-mcp"
New-Item -ItemType Directory -Path $PackageDir | Out-Null

# Step 1: Publish
if (-not $SkipBuild) {
    Write-Host "Step 1: Publishing..." -ForegroundColor White

    # Kill any running instance
    $proc = Get-Process -Name "RoslynMcpServer" -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "  Stopping running instance..." -ForegroundColor Gray
        Stop-Process -Name "RoslynMcpServer" -Force
        Start-Sleep -Seconds 1
    }

    $publishDir = Join-Path $OutputDir "publish-temp"
    dotnet publish "$ProjectRoot\RoslynMcpServer.csproj" -c Release -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed"
        exit 1
    }
} else {
    Write-Host "Step 1: Skipping build (using existing publish)" -ForegroundColor Yellow
    $publishDir = Join-Path $OutputDir "publish-temp"
}

# Step 2: Copy exe and required files
Write-Host ""
Write-Host "Step 2: Copying files..." -ForegroundColor White

# Main exe
Copy-Item -Path (Join-Path $publishDir "RoslynMcpServer.exe") -Destination (Join-Path $PackageDir "roslyn-mcp.exe")
Write-Host "  Copied: roslyn-mcp.exe" -ForegroundColor Gray

# Required folders
$requiredFolders = @("Instructions", "LocalEmbeddingsModel", "BuildHost-net472", "BuildHost-netcore", "analyzers")
foreach ($folder in $requiredFolders) {
    $src = Join-Path $publishDir $folder
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $PackageDir -Recurse
        Write-Host "  Copied: $folder/" -ForegroundColor Gray
    }
}

# Step 3: Copy hooks (suggest only, not enforce)
Write-Host ""
Write-Host "Step 3: Copying hooks (suggest only)..." -ForegroundColor White
$hooksDir = Join-Path $PackageDir "hooks"
New-Item -ItemType Directory -Path $hooksDir | Out-Null

$suggestHooks = @(
    "suggest-roslyn-for-csharp.py",
    "suggest-roslyn-for-read.py"
)
foreach ($hook in $suggestHooks) {
    $src = Join-Path $ProjectRoot "Instructions\Hooks\$hook"
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $hooksDir
        Write-Host "  Copied: $hook" -ForegroundColor Gray
    }
}

# Step 4: Copy skills (exclude repo-specific skills like blog)
Write-Host ""
Write-Host "Step 4: Copying skills..." -ForegroundColor White
$skillsDir = Join-Path $PackageDir "skills"
New-Item -ItemType Directory -Path $skillsDir | Out-Null

$excludeSkills = @("blog")  # Skills specific to RoslynMcpServer repo
$skillFolders = Get-ChildItem -Path (Join-Path $ProjectRoot "Instructions\Skills") -Directory
foreach ($skill in $skillFolders) {
    if ($excludeSkills -contains $skill.Name) {
        Write-Host "  Skipped: $($skill.Name)/ (repo-specific)" -ForegroundColor Yellow
        continue
    }
    Copy-Item -Path $skill.FullName -Destination $skillsDir -Recurse
    Write-Host "  Copied: $($skill.Name)/" -ForegroundColor Gray
}

# Step 5: Create README.txt
Write-Host ""
Write-Host "Step 5: Creating README.txt..." -ForegroundColor White

$readme = @"
Roslyn MCP Server - Quick Start
================================

Semantic C# analysis for Claude Code.

INSTALLATION
------------

1. Copy this folder to your C# solution directory:
   YourSolution/.roslyn-mcp/

2. Open a terminal in your solution directory and run:
   .roslyn-mcp/roslyn-mcp.exe --init

3. Start Claude Code and verify:
   claude
   /mcp   (should show "roslyn" connected)

That's it! Claude will now use Roslyn tools for C# code.


OPTIONAL: ENABLE HOOKS
----------------------

Hooks suggest using Roslyn tools instead of grep/cat for C# files:

   .roslyn-mcp/roslyn-mcp.exe --enable-hooks


OPTIONAL: ENABLE SKILLS
-----------------------

Skills add custom commands like /architect:

   .roslyn-mcp/roslyn-mcp.exe --enable-skills

Available skills:
   /architect  - Deep code analysis with improvement plan
   /blog       - Generate session retrospective


MORE INFORMATION
----------------

   roslyn-mcp.exe --help      Show all commands
   roslyn-mcp.exe --version   Show version

GitHub: https://github.com/BeinerChes/RoslynMcpServer
"@

$readme | Set-Content (Join-Path $PackageDir "README.txt")
Write-Host "  Created: README.txt" -ForegroundColor Gray

# Step 6: Create zip
Write-Host ""
Write-Host "Step 6: Creating zip..." -ForegroundColor White

$zipPath = Join-Path $OutputDir "roslyn-mcp-win-x64.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path $PackageDir -DestinationPath $zipPath
Write-Host "  Created: roslyn-mcp-win-x64.zip" -ForegroundColor Gray

# Step 7: Summary
Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host ""

$zipSize = (Get-Item $zipPath).Length / 1MB
Write-Host "Package: $zipPath" -ForegroundColor White
Write-Host "Size: $([math]::Round($zipSize, 1)) MB" -ForegroundColor White
Write-Host ""
Write-Host "Contents:" -ForegroundColor White
Get-ChildItem -Path $PackageDir -Recurse | Where-Object { -not $_.PSIsContainer } | ForEach-Object {
    $relativePath = $_.FullName.Substring($PackageDir.Length + 1)
    $size = if ($_.Length -gt 1MB) { "$([math]::Round($_.Length / 1MB, 1)) MB" } else { "$([math]::Round($_.Length / 1KB, 1)) KB" }
    Write-Host "  $relativePath ($size)" -ForegroundColor Gray
}

# Cleanup temp publish folder
if (Test-Path (Join-Path $OutputDir "publish-temp")) {
    Remove-Item -Path (Join-Path $OutputDir "publish-temp") -Recurse -Force
}

Write-Host ""
Write-Host "To test, extract and run:" -ForegroundColor Cyan
Write-Host "  Expand-Archive $zipPath -DestinationPath .\test-release" -ForegroundColor White
Write-Host "  cd test-release\roslyn-mcp" -ForegroundColor White
Write-Host "  .\roslyn-mcp.exe --help" -ForegroundColor White
