<#
.SYNOPSIS
    Extracts the release notes for one version from CHANGELOG.md (Keep a Changelog format).

.EXAMPLE
    ./build/release-notes.ps1 -Version 1.2.0 -OutFile artifacts/release-notes.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $OutFile
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$lines = Get-Content (Join-Path $Root 'CHANGELOG.md')

$collecting = $false
$notes = New-Object System.Collections.Generic.List[string]
foreach ($line in $lines) {
    if ($line -match '^##\s+\[?v?([^\]\s]+)\]?') {
        if ($collecting) { break }
        if ($Matches[1] -eq $Version) { $collecting = $true; continue }
    }
    elseif ($collecting) {
        $notes.Add($line)
    }
}

$body = ($notes -join "`n").Trim()
if (-not $body) {
    Write-Warning "CHANGELOG.md has no section for $Version; using a generic note."
    $body = "Vigil $Version."
}

$body += @"


---
**Verify your download:** compare the file's SHA-256 with ``SHA256SUMS.txt`` (PowerShell: ``Get-FileHash .\<file> -Algorithm SHA256``). The in-app updater does this automatically.

**Installer** (recommended): per-user install, no administrator rights needed, updates in place from inside Vigil.
**Portable zip**: unzip anywhere and run ``Vigil.exe``; settings stay in the ``Data`` folder next to it.
"@

$dir = Split-Path -Parent $OutFile
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
[System.IO.File]::WriteAllText($OutFile, $body + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Release notes for $Version written to $OutFile"
