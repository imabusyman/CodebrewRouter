#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Materializes prompts/squad/ to .github/plugins/squad/ (Copilot CLI) and .claude/ (Claude Code).

.DESCRIPTION
    prompts/squad/ is the single source of truth for the Blaze.LlmGateway development squad.
    This script parses role prompt frontmatter, translates tool-name vocabularies between
    Claude Code and Copilot CLI, and emits per-target variants. It also copies shared
    instructions, protocol schemas, and command shims to both targets.

    Idempotent. Runs on demand. Not CI-enforced — drift caught at PR review.

    See ADR-0009 (Docs/design/adr/0009-squad-orchestration.md) and
    prompts/squad/README.md for the governing contract.

.EXAMPLE
    pwsh ./scripts/sync-squad.ps1
#>

[CmdletBinding()]
param(
    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot    = Resolve-Path (Join-Path $PSScriptRoot '..')
$srcRoot     = Join-Path $repoRoot 'prompts/squad'
$copilotRoot = Join-Path $repoRoot '.github/plugins/squad'
$claudeRoot  = Join-Path $repoRoot '.claude'
$geminiRoot  = Join-Path $repoRoot '.gemini'

if (-not (Test-Path $srcRoot)) {
    throw "Source squad directory not found: $srcRoot"
}

$roles = @(
    'conductor', 'planner', 'architect', 'coder',
    'tester', 'reviewer', 'infra', 'security-review', 'orchestrator'
)

# Claude Code -> Copilot CLI tool-name mapping.
$toolMapCopilot = @{
    'Read'     = 'read'
    'Edit'     = 'edit'
    'Grep'     = 'search'
    'Glob'     = 'search'
    'Bash'     = 'shell'
    'WebFetch' = 'web'
    'Agent'    = 'agent'
    'Write'    = 'edit'
}

# Claude Code -> Gemini CLI tool-name mapping.
$toolMapGemini = @{
    'Read'     = 'read_file'
    'Edit'     = 'replace'
    'Grep'     = 'grep_search'
    'Glob'     = 'glob'
    'Bash'     = 'run_shell_command'
    'WebFetch' = 'web_fetch'
    'Agent'    = 'generalist'
    'Write'    = 'write_file'
}

function Ensure-Dir {
    param([string]$Path)
    if ($WhatIf) { Write-Host "  (what-if) mkdir $Path" -ForegroundColor Yellow; return }
    if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

function Write-File {
    param([string]$Path, [string]$Content)
    if ($WhatIf) { Write-Host "  (what-if) write $Path ($($Content.Length) chars)" -ForegroundColor Yellow; return }
    Ensure-Dir -Path (Split-Path -Parent $Path)
    # Normalize line endings to LF; Copilot CLI + Claude Code both tolerate this on Windows.
    $normalized = $Content -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText($Path, $normalized, [System.Text.UTF8Encoding]::new($false))
}

function Parse-Frontmatter {
    param([string]$Content)
    # Frontmatter is a YAML block at the very start bounded by --- lines.
    $match = [regex]::Match($Content, '^---\r?\n(?<fm>.*?)\r?\n---\r?\n(?<body>.*)$', 'Singleline')
    if (-not $match.Success) {
        throw "No YAML frontmatter found in prompt file."
    }
    return @{ Frontmatter = $match.Groups['fm'].Value; Body = $match.Groups['body'].Value }
}

function Extract-FrontmatterValue {
    param([string]$Frontmatter, [string]$Key)
    # Naive single-line extraction (good enough for our flat frontmatter).
    $line = $Frontmatter -split "`n" | Where-Object { $_ -match "^\s*$([regex]::Escape($Key))\s*:\s*" } | Select-Object -First 1
    if (-not $line) { return $null }
    return ($line -replace "^\s*$([regex]::Escape($Key))\s*:\s*", '').Trim()
}

function Translate-Tools {
    param([string]$ToolsLine, [hashtable]$Map, [switch]$Gemini)
    # ToolsLine like "[Read, Edit, Grep, Glob, Bash, WebFetch]" or "Read, Edit"
    if (-not $ToolsLine) { return if ($Gemini) { "" } else { "[]" } }
    $inner = $ToolsLine.Trim('[', ']', ' ')
    if (-not $inner) { return if ($Gemini) { "" } else { "[]" } }
    $translated = $inner.Split(',') `
        | ForEach-Object { $_.Trim() } `
        | ForEach-Object { if ($Map.ContainsKey($_)) { $Map[$_] } else { $_.ToLowerInvariant() } } `
        | Select-Object -Unique
    
    if ($Gemini) {
        # Return as YAML list for Gemini
        $yaml = "`n"
        foreach ($t in $translated) { $yaml += "  - $t`n" }
        return $yaml.TrimEnd()
    } else {
        # Return as bracketed list for Copilot/Claude
        return "[" + ($translated -join ", ") + "]"
    }
}

function Emit-ClaudeAgent {
    param([string]$Role, [hashtable]$Parsed)
    $target = Join-Path $claudeRoot "agents/squad-$Role.md"
    $frontmatter = $Parsed.Frontmatter.TrimEnd()
    $content = "---`n$frontmatter`n---`n$($Parsed.Body)"
    Write-File -Path $target -Content $content
    Write-Host "  [claude]  agents/squad-$Role.md" -ForegroundColor Cyan
}

function Emit-GeminiAgent {
    param([string]$Role, [hashtable]$Parsed)
    $target = Join-Path $geminiRoot "agents/squad-$Role.md"
    $toolsSrc = Extract-FrontmatterValue -Frontmatter $Parsed.Frontmatter -Key 'tools'
    $toolsTranslated = Translate-Tools -ToolsLine $toolsSrc -Map $toolMapGemini -Gemini

    # Gemini agents use YAML frontmatter but with specific keys.
    $newFrontmatter = ($Parsed.Frontmatter -split "`n") | ForEach-Object {
        if ($_ -match '^\s*tools\s*:') { 
            if ($toolsTranslated) { "tools:$toolsTranslated" } else { "tools: []" }
        }
        elseif ($_ -match '^\s*model\s*:') { "model: inherit" }
        else { $_ }
    }
    $newFrontmatter = ($newFrontmatter -join "`n").TrimEnd()
    $content = "---`n$newFrontmatter`n---`n$($Parsed.Body)"
    Write-File -Path $target -Content $content
    Write-Host "  [gemini]  agents/squad-$Role.md" -ForegroundColor Green
}

function Emit-CopilotAgent {
    param([string]$Role, [hashtable]$Parsed)
    $target = Join-Path $copilotRoot "agents/squad.$Role.agent.md"
    $toolsSrc = Extract-FrontmatterValue -Frontmatter $Parsed.Frontmatter -Key 'tools'
    $toolsTranslated = Translate-Tools -ToolsLine $toolsSrc -Map $toolMapCopilot

    # Rewrite only the tools: line; leave everything else identical.
    $newFrontmatter = ($Parsed.Frontmatter -split "`n") | ForEach-Object {
        if ($_ -match '^\s*tools\s*:') { "tools: $toolsTranslated" } else { $_ }
    }
    $newFrontmatter = ($newFrontmatter -join "`n").TrimEnd()
    $content = "---`n$newFrontmatter`n---`n$($Parsed.Body)"
    Write-File -Path $target -Content $content
    Write-Host "  [copilot] agents/squad.$Role.agent.md" -ForegroundColor Magenta
}

function Copy-Verbatim {
    param([string]$SourceDir, [string]$DestDir, [string]$Label)
    if (-not (Test-Path $SourceDir)) { return }
    Ensure-Dir -Path $DestDir
    Get-ChildItem -Path $SourceDir -File | ForEach-Object {
        $dest = Join-Path $DestDir $_.Name
        $content = Get-Content -Raw -Path $_.FullName
        Write-File -Path $dest -Content $content
        Write-Host "  [$Label] $($_.Name)" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "Squad sync: $srcRoot" -ForegroundColor Green
Write-Host "  -> $copilotRoot" -ForegroundColor Green
Write-Host "  -> $claudeRoot" -ForegroundColor Green
Write-Host "  -> $geminiRoot" -ForegroundColor Green
Write-Host ""

# 1. Agents — per-target variants with translated tool names.
Ensure-Dir -Path (Join-Path $copilotRoot 'agents')
Ensure-Dir -Path (Join-Path $claudeRoot 'agents')
Ensure-Dir -Path (Join-Path $geminiRoot 'agents')

foreach ($role in $roles) {
    $src = Join-Path $srcRoot "$role.prompt.md"
    if (-not (Test-Path $src)) {
        Write-Warning "Missing source prompt: $src — skipped."
        continue
    }
    $raw    = Get-Content -Raw -Path $src
    $parsed = Parse-Frontmatter -Content $raw
    Emit-ClaudeAgent  -Role $role -Parsed $parsed
    Emit-CopilotAgent -Role $role -Parsed $parsed
    Emit-GeminiAgent  -Role $role -Parsed $parsed
}

# 2. Shared instructions — copied verbatim to both targets.
Copy-Verbatim `
    -SourceDir (Join-Path $srcRoot '_shared') `
    -DestDir   (Join-Path $copilotRoot 'instructions') `
    -Label     'copilot'

Copy-Verbatim `
    -SourceDir (Join-Path $srcRoot '_shared') `
    -DestDir   (Join-Path $geminiRoot 'instructions') `
    -Label     'gemini'

# 3. Protocol schemas — copied to both targets for reference.
Copy-Verbatim `
    -SourceDir (Join-Path $srcRoot 'protocol') `
    -DestDir   (Join-Path $copilotRoot 'protocol') `
    -Label     'copilot'

Copy-Verbatim `
    -SourceDir (Join-Path $srcRoot 'protocol') `
    -DestDir   (Join-Path $claudeRoot 'squad-protocol') `
    -Label     'claude'

Copy-Verbatim `
    -SourceDir (Join-Path $srcRoot 'protocol') `
    -DestDir   (Join-Path $geminiRoot 'squad-protocol') `
    -Label     'gemini'

# 4. Commands — Claude Code slash commands live under .claude/commands/.
Copy-Verbatim `
    -SourceDir (Join-Path $srcRoot 'commands') `
    -DestDir   (Join-Path $claudeRoot 'commands') `
    -Label     'claude'

# Copilot CLI exposes the squad as /agent squad; commands are informational.
Copy-Verbatim `
    -SourceDir (Join-Path $srcRoot 'commands') `
    -DestDir   (Join-Path $copilotRoot 'commands') `
    -Label     'copilot'

# Gemini CLI uses .toml for commands usually, but can read .md agents as commands if configured.
# We'll copy the .md commands to .gemini/commands/ just in case.
Copy-Verbatim `
    -SourceDir (Join-Path $srcRoot 'commands') `
    -DestDir   (Join-Path $geminiRoot 'commands') `
    -Label     'gemini'

# 5. Skills — per-skill directories each holding a SKILL.md.
function Copy-Skills {
    param([string]$SourceDir, [string]$DestDir, [string]$Label)
    if (-not (Test-Path $SourceDir)) { return }
    Ensure-Dir -Path $DestDir
    Get-ChildItem -Path $SourceDir -Directory | ForEach-Object {
        $skillSrc  = Join-Path $_.FullName 'SKILL.md'
        $skillDest = Join-Path (Join-Path $DestDir $_.Name) 'SKILL.md'
        if (Test-Path $skillSrc) {
            $content = Get-Content -Raw -Path $skillSrc
            Write-File -Path $skillDest -Content $content
            Write-Host "  [$Label] skills/$($_.Name)/SKILL.md" -ForegroundColor DarkGray
        }
    }
}

Copy-Skills `
    -SourceDir (Join-Path $srcRoot 'skills') `
    -DestDir   (Join-Path $claudeRoot 'skills') `
    -Label     'claude'

Copy-Skills `
    -SourceDir (Join-Path $srcRoot 'skills') `
    -DestDir   (Join-Path $copilotRoot 'skills') `
    -Label     'copilot'

Copy-Skills `
    -SourceDir (Join-Path $srcRoot 'skills') `
    -DestDir   (Join-Path $geminiRoot 'skills') `
    -Label     'gemini'

Write-Host ""
Write-Host "Squad sync complete." -ForegroundColor Green
Write-Host "Review the diff before committing: git diff .github/plugins/squad .claude .gemini"
Write-Host ""
