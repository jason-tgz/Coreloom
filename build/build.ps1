<#
.SYNOPSIS
    Builds, tests, publishes and packages Coreloom.

.DESCRIPTION
    Typical uses:

      ./build/build.ps1                      # restore + build (Debug) + unit tests
      ./build/build.ps1 -Configuration Release -Publish
                                             # self-contained build in artifacts/publish/win-x64
      ./build/build.ps1 -Configuration Release -Version 1.2.0 -Package
                                             # publish + installer + portable zip + SHA256SUMS.txt
                                             # in artifacts/release (what the release workflow runs)

    Works in Windows PowerShell 5.1 and PowerShell 7+. Requires the .NET SDK from global.json.
    Packaging the installer requires Inno Setup 6 (https://jrsoftware.org/isinfo.php); without it the
    portable zip and checksums are still produced unless -RequireInstaller is given.

.PARAMETER Configuration
    Debug or Release (default Debug; packaging forces Release).

.PARAMETER Version
    Semantic version (1.2.3 or 1.2.3-beta.1). Defaults to VersionPrefix from Directory.Build.props + "-dev".

.PARAMETER Repository
    GitHub "owner/name" embedded for update checks. Defaults to $env:GITHUB_REPOSITORY.

.PARAMETER SkipTests
    Skip unit tests.

.PARAMETER Publish
    Publish the self-contained app and CoreloomDiag into artifacts/publish/<runtime>.

.PARAMETER Package
    Publish, then build the installer, the portable zip and SHA256SUMS.txt into artifacts/release.

.PARAMETER NoLhm
    Build without the LibreHardwareMonitor adapter (NVML/Windows sensors only).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [string] $Version,
    [string] $Runtime = 'win-x64',
    [string] $Repository = $env:GITHUB_REPOSITORY,
    [switch] $SkipTests,
    [switch] $Publish,
    [switch] $Package,
    [switch] $RequireInstaller,
    [switch] $NoLhm
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $Root 'Coreloom.sln'
$Artifacts = Join-Path $Root 'artifacts'
$PublishDir = Join-Path $Artifacts "publish/$Runtime"
$ReleaseDir = Join-Path $Artifacts 'release'
$TestResults = Join-Path $Artifacts 'test-results'

function Write-Step([string] $Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Native {
    param([Parameter(Mandatory)] [string] $FilePath, [string[]] $Arguments)
    Write-Host "    $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath exited with code $LASTEXITCODE"
    }
}

# On GitHub Actions, sums up each test run (trx) and turns every failed test into an annotation, so a failure can be read
# on the run's summary page without the log.
function Write-TestReport([string] $Directory) {
    if (-not $env:GITHUB_ACTIONS -or -not (Test-Path $Directory)) { return }
    function Escape([string] $Text, [switch] $Property) {
        $Text = $Text -replace '%', '%25' -replace "`r", '%0D' -replace "`n", '%0A'
        if ($Property) { $Text = $Text -replace ':', '%3A' -replace ',', '%2C' }
        return $Text
    }
    foreach ($file in Get-ChildItem $Directory -Filter '*.trx' -Recurse) {
        try {
            [xml] $trx = Get-Content $file.FullName -Raw
            $ns = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
            $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
            $method = $trx.SelectSingleNode('//t:UnitTest/t:TestMethod', $ns)
            $project = if ($method) { [System.IO.Path]::GetFileNameWithoutExtension($method.GetAttribute('codeBase')) } else { $file.Name }
            $counters = $trx.SelectSingleNode('//t:ResultSummary/t:Counters', $ns)
            $outcome = $trx.SelectSingleNode('//t:ResultSummary', $ns).GetAttribute('outcome')
            $skipped = @($trx.SelectNodes('//t:UnitTestResult[@outcome="NotExecuted"]', $ns) | ForEach-Object { $_.GetAttribute('testName') -replace '^.*\.([^.]+\.[^.]+)$', '$1' })
            $summary = "$project ($outcome): $($counters.GetAttribute('passed')) passed, $($counters.GetAttribute('failed')) failed, $($skipped.Count) skipped"
            if ($skipped.Count -gt 0) { $summary += ' - skipped: ' + (($skipped | Select-Object -First 12) -join '; ') }
            Write-Host "::notice title=Tests::$(Escape $summary)"
            foreach ($result in $trx.SelectNodes('//t:UnitTestResult[@outcome="Failed"]', $ns)) {
                $message = $result.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
                $stack = $result.SelectSingleNode('t:Output/t:ErrorInfo/t:StackTrace', $ns)
                $text = if ($message) { $message.InnerText.Trim() } else { '(no message)' }
                if ($stack) { $text += "`n" + ((($stack.InnerText -split "`n") | Select-Object -First 4) -join "`n") }
                Write-Host "::error title=$(Escape "Test failed: $($result.GetAttribute('testName'))" -Property)::$(Escape $text)"
            }
        }
        catch {
            Write-Host "::warning title=Tests::$(Escape "Couldn't read $($file.Name): $_")"
        }
    }
}

function Get-DefaultVersion {
    [xml] $props = Get-Content (Join-Path $Root 'Directory.Build.props')
    $node = $props.SelectSingleNode('//VersionPrefix')
    $prefix = if ($node -and $node.InnerText) { $node.InnerText.Trim() } else { '0.0.0' }
    return "$prefix-dev"
}

function Find-InnoSetupCompiler {
    $candidates = @()
    if ($env:INNO_SETUP_COMPILER) { $candidates += $env:INNO_SETUP_COMPILER }
    $candidates += @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $null
}

if ($Package) { $Publish = $true; $Configuration = 'Release' }
if (-not $Version) { $Version = Get-DefaultVersion }
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a semantic version like 1.2.3 or 1.2.3-beta.1"
}

$msbuildProps = @("-p:Version=$Version")
if ($Repository) { $msbuildProps += "-p:CoreloomUpdateRepository=$Repository" }
if ($NoLhm) { $msbuildProps += '-p:CoreloomNoLhm=true' }

Write-Host "Coreloom build  version=$Version  configuration=$Configuration  runtime=$Runtime  repository=$(if ($Repository) { $Repository } else { '(none: update checks disabled)' })"

Write-Step 'dotnet SDK'
Invoke-Native 'dotnet' @('--version')

Write-Step 'Restore'
Invoke-Native 'dotnet' (@('restore', $Solution) + $msbuildProps)

Write-Step "Build ($Configuration)"
Invoke-Native 'dotnet' (@('build', $Solution, '-c', $Configuration, '--no-restore', '-nologo') + $msbuildProps)

if (-not $SkipTests) {
    Write-Step 'Unit tests'
    try {
        Invoke-Native 'dotnet' (@('test', $Solution, '-c', $Configuration, '--no-build', '-nologo',
            '--logger', 'trx', '--results-directory', $TestResults) + $msbuildProps)
    }
    finally {
        Write-TestReport $TestResults
    }
}

if ($Publish) {
    Write-Step "Publish self-contained $Runtime -> $PublishDir"
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

    $publishCommon = @('-c', $Configuration, '-r', $Runtime, '--self-contained', 'true', '-nologo', '-o', $PublishDir,
        '-p:DebugType=none', '-p:GenerateDocumentationFile=false') + $msbuildProps

    # CoreloomDiag first, then the app (the app's files win where both ship the same library).
    Invoke-Native 'dotnet' (@('publish', (Join-Path $Root 'src/Coreloom.Diagnostics/Coreloom.Diagnostics.csproj')) + $publishCommon)
    Invoke-Native 'dotnet' (@('publish', (Join-Path $Root 'src/Coreloom.App/Coreloom.App.csproj'), '-p:PublishReadyToRun=true') + $publishCommon)

    foreach ($required in @('Coreloom.exe', 'CoreloomDiag.exe', 'Coreloom.dll', 'Coreloom.Core.dll', 'Coreloom.Hardware.dll')) {
        if (-not (Test-Path (Join-Path $PublishDir $required))) { throw "Publish output is missing $required" }
    }
}

if ($Package) {
    if (Test-Path $ReleaseDir) { Remove-Item $ReleaseDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
    $baseName = "Coreloom-$Version-$Runtime"

    Write-Step 'Installer (Inno Setup)'
    $iscc = Find-InnoSetupCompiler
    if ($iscc) {
        $repoUrl = if ($Repository) { "https://github.com/$Repository" } else { 'https://github.com' }
        Invoke-Native $iscc @("/DAppVersion=$Version", "/DSourceDir=$PublishDir", "/DOutputDir=$ReleaseDir",
            "/DRepositoryUrl=$repoUrl", '/Q', (Join-Path $Root 'installer/Coreloom.iss'))
        if (-not (Test-Path (Join-Path $ReleaseDir "$baseName-Setup.exe"))) { throw 'Installer was not produced.' }
    }
    elseif ($RequireInstaller) {
        throw 'Inno Setup 6 (ISCC.exe) was not found. Install it or set INNO_SETUP_COMPILER.'
    }
    else {
        Write-Warning 'Inno Setup 6 was not found; skipping the installer. Install it from https://jrsoftware.org/isinfo.php'
    }

    Write-Step 'Portable zip'
    $staging = Join-Path $Artifacts "staging/$baseName"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Copy-Item (Join-Path $PublishDir '*') $staging -Recurse
    Set-Content -Path (Join-Path $staging 'portable.txt') -Encoding ASCII -Value @(
        'This file makes Coreloom run in portable mode: settings and logs are stored next to Coreloom.exe',
        'in the "Data" folder instead of your user profile. Delete it to use the normal locations.')
    $zip = Join-Path $ReleaseDir "$baseName-Portable.zip"
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -CompressionLevel Optimal
    Remove-Item (Join-Path $Artifacts 'staging') -Recurse -Force

    Write-Step 'SHA256SUMS.txt'
    $lines = Get-ChildItem $ReleaseDir -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name | ForEach-Object {
        $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($_.Name)"
    }
    # LF line endings, no BOM: the same format as sha256sum, verifiable on any OS.
    [System.IO.File]::WriteAllText((Join-Path $ReleaseDir 'SHA256SUMS.txt'), (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
    Get-Content (Join-Path $ReleaseDir 'SHA256SUMS.txt') | ForEach-Object { Write-Host "    $_" }

    Write-Step 'Release artifacts'
    Get-ChildItem $ReleaseDir -File | ForEach-Object { '{0,-52} {1,10:N1} MB' -f $_.Name, ($_.Length / 1MB) } | Write-Host
}

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
