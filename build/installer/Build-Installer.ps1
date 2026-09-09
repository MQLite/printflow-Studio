<#
.SYNOPSIS
    Builds the PrintFlow Studio versioned offline installer (SCRUM-11123).

.DESCRIPTION
    The one repeatable entry point for producing a release artefact. Four stages, each of which
    fails the build rather than degrading:

      1. Version      — read PrintFlowVersion from Version.props through MSBuild, never by
                        regex and never from a second file. This is the only version in the
                        artefact name, the MSI ProductVersion and every assembly.
      2. Publish      — a controlled Release, self-contained, win-x64 publish of PrintFlow.App
                        into a clean directory. Self-contained because the validated workstation
                        must never be asked to fetch a .NET runtime at installation time.
      3. Stage        — copy the publish output into the payload directory through
                        installer\payload-policy.json. Default deny: a file reaches the payload
                        only if it matches an allow pattern; a file matching denyAlways stops
                        the build; a missing required file stops the build.
      4. Package      — build installer\PrintFlowStudio.Installer against the staged payload and
                        emit PrintFlowStudio-<version>-win-x64.msi with a build manifest.

    What this script never does: sign anything, contact an update service, publish, deploy, or
    touch D:\PrintFlowStudio. Its only writes are under the -OutputRoot directory.

    NuGet restore during stages 2 and 4 needs the build machine's feed. That is a build-time
    concern and says nothing about the installed product: the MSI it produces embeds its whole
    payload and installs with no network at all (SCRUM-11123 Part L §26).

.PARAMETER OutputRoot
    Where the release folder is created. Defaults to artifacts\installer under the repository,
    which is git-ignored.

.PARAMETER VersionOverride
    Packages under a different version without editing Version.props. Intended for the upgrade
    smoke, which needs an N and an N+1 built from the same source. Not for release builds.

.PARAMETER SkipPublish
    Reuses an existing publish directory. For iterating on packaging only.
#>
[CmdletBinding()]
param(
    [string] $OutputRoot,
    [string] $VersionOverride,
    [switch] $SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$DotNet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $DotNet)) { $DotNet = 'dotnet' }

function Write-Stage([string] $Text) {
    Write-Host ''
    Write-Host "=== $Text ===" -ForegroundColor Cyan
}

# --------------------------------------------------------------------------------------------
# Glob matching for the payload policy.
#
# '**' crosses directory separators, '*' does not, and '?' is one non-separator character. The
# comparison is ordinal and case-insensitive because Windows paths are, and it runs against the
# payload-relative path with forward slashes so a policy entry reads the same as the path it
# describes.
# --------------------------------------------------------------------------------------------
function ConvertTo-GlobRegex([string] $Pattern) {
    $sb = New-Object System.Text.StringBuilder
    [void] $sb.Append('^')
    $i = 0
    while ($i -lt $Pattern.Length) {
        $c = $Pattern[$i]
        if ($c -eq '*') {
            if (($i + 1) -lt $Pattern.Length -and $Pattern[$i + 1] -eq '*') {
                # '**/' also matches zero directories, so '**/x' matches a root-level 'x'.
                if (($i + 2) -lt $Pattern.Length -and $Pattern[$i + 2] -eq '/') {
                    [void] $sb.Append('(?:.*/)?')
                    $i += 3
                    continue
                }
                [void] $sb.Append('.*')
                $i += 2
                continue
            }
            [void] $sb.Append('[^/]*')
            $i++
            continue
        }
        if ($c -eq '?') { [void] $sb.Append('[^/]'); $i++; continue }
        [void] $sb.Append([regex]::Escape([string] $c))
        $i++
    }
    [void] $sb.Append('$')
    return $sb.ToString()
}

function Test-Glob([string] $RelativePath, [string[]] $Patterns) {
    foreach ($pattern in $Patterns) {
        if ($RelativePath -imatch (ConvertTo-GlobRegex $pattern)) { return $true }
    }
    return $false
}

# --------------------------------------------------------------------------------------------
# 1 — Version
# --------------------------------------------------------------------------------------------
Write-Stage '1/4  Version'

if ([string]::IsNullOrWhiteSpace($VersionOverride)) {
    $versionProbe = Join-Path ([System.IO.Path]::GetTempPath()) ("printflow-version-" + [guid]::NewGuid().ToString('N') + ".proj")
    @"
<Project>
  <Import Project="$RepositoryRoot\Version.props" />
  <Target Name="PrintVersion"><Message Importance="high" Text="PRINTFLOW_VERSION=`$(PrintFlowVersion)" /></Target>
</Project>
"@ | Set-Content -LiteralPath $versionProbe -Encoding utf8

    $probeOutput = & $DotNet msbuild $versionProbe -t:PrintVersion -nologo -v:minimal
    Remove-Item -LiteralPath $versionProbe -Force
    $match = [regex]::Match(($probeOutput -join "`n"), 'PRINTFLOW_VERSION=([0-9][0-9A-Za-z.\-+]*)')
    if (-not $match.Success) {
        throw "Could not read PrintFlowVersion from Version.props. MSBuild said: $($probeOutput -join "`n")"
    }
    $Version = $match.Groups[1].Value
} else {
    $Version = $VersionOverride
    Write-Host "Version overridden to $Version (not a release build)." -ForegroundColor Yellow
}

Write-Host "PrintFlow Studio version: $Version"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $RepositoryRoot 'artifacts\installer'
}
$ReleaseDir = Join-Path $OutputRoot $Version
$PublishDir = Join-Path $ReleaseDir 'publish'
$PayloadDir = Join-Path $ReleaseDir 'payload'
$SymbolsDir = Join-Path $ReleaseDir 'symbols'

# --------------------------------------------------------------------------------------------
# 2 — Publish
# --------------------------------------------------------------------------------------------
Write-Stage '2/4  Release publish (self-contained, win-x64)'

if ($SkipPublish -and (Test-Path (Join-Path $PublishDir 'PrintFlow.App.exe'))) {
    Write-Host "Reusing existing publish at $PublishDir"
} else {
    if (Test-Path $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force }
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

    & $DotNet publish (Join-Path $RepositoryRoot 'src\PrintFlow.App\PrintFlow.App.csproj') `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:Version=$Version `
        -o $PublishDir `
        -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
}

# --------------------------------------------------------------------------------------------
# 3 — Stage against the payload policy
# --------------------------------------------------------------------------------------------
Write-Stage '3/4  Payload staging (default deny)'

$policyPath = Join-Path $RepositoryRoot 'installer\payload-policy.json'
$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json

if (Test-Path $PayloadDir) { Remove-Item -LiteralPath $PayloadDir -Recurse -Force }
New-Item -ItemType Directory -Path $PayloadDir -Force | Out-Null
if (Test-Path $SymbolsDir) { Remove-Item -LiteralPath $SymbolsDir -Recurse -Force }
New-Item -ItemType Directory -Path $SymbolsDir -Force | Out-Null

$publishRootLength = (Resolve-Path $PublishDir).Path.TrimEnd('\').Length + 1
$staged = New-Object System.Collections.Generic.List[string]
$dropped = New-Object System.Collections.Generic.List[string]
$violations = New-Object System.Collections.Generic.List[string]

foreach ($file in (Get-ChildItem -LiteralPath $PublishDir -Recurse -File)) {
    $relative = $file.FullName.Substring($publishRootLength).Replace('\', '/')

    if (Test-Glob $relative $policy.denyAlways) {
        # Symbols are the one denied kind that is expected and kept, as a release record beside
        # the .msi rather than as an installed file. Everything else on the deny list appearing
        # in a Release publish is a defect and stops the build.
        if ($relative -imatch '\.pdb$') {
            $symbolTarget = Join-Path $SymbolsDir $relative.Replace('/', '\')
            New-Item -ItemType Directory -Path (Split-Path -Parent $symbolTarget) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $symbolTarget -Force
            $dropped.Add($relative)
            continue
        }
        $violations.Add($relative)
        continue
    }

    if (-not (Test-Glob $relative $policy.allow)) {
        $dropped.Add($relative)
        continue
    }

    $target = Join-Path $PayloadDir $relative.Replace('/', '\')
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    $staged.Add($relative)
}

if ($violations.Count -gt 0) {
    throw ("The Release publish contains files the payload policy denies outright. This is a " +
           "defect in the publish, not something staging may drop:`n  " + ($violations -join "`n  "))
}

foreach ($required in $policy.requiredFiles) {
    if (-not (Test-Path (Join-Path $PayloadDir $required.Replace('/', '\')))) {
        throw "Required payload file '$required' is missing from the staged payload."
    }
}

Write-Host ("Staged {0} files; dropped {1} ({2} symbols archived)." -f `
    $staged.Count, $dropped.Count, (Get-ChildItem -LiteralPath $SymbolsDir -Recurse -File).Count)

# --------------------------------------------------------------------------------------------
# 4 — Package
# --------------------------------------------------------------------------------------------
Write-Stage '4/4  MSI'

$wixProject = Join-Path $RepositoryRoot 'installer\PrintFlowStudio.Installer\PrintFlowStudio.Installer.wixproj'
$msiOutDir = Join-Path $ReleaseDir 'msi'
if (Test-Path $msiOutDir) { Remove-Item -LiteralPath $msiOutDir -Recurse -Force }

# Intermediate output goes under this release's own folder rather than into the project
# directory. Two versions built from one working tree — which is exactly what the upgrade smoke
# needs — would otherwise share one obj\ and the second build would be judged up to date against
# the first's inputs while carrying the first's output name.
$msiObjDir = Join-Path $ReleaseDir 'obj'

& $DotNet build $wixProject `
    -c Release `
    -p:PayloadDir=$PayloadDir `
    -p:PrintFlowVersion=$Version `
    -p:OutputPath=$msiOutDir `
    -p:BaseIntermediateOutputPath="$msiObjDir\" `
    -p:IntermediateOutputPath="$msiObjDir\Release\" `
    -v minimal
if ($LASTEXITCODE -ne 0) { throw "WiX package build failed with exit code $LASTEXITCODE." }

$msi = Get-ChildItem -LiteralPath $msiOutDir -Filter '*.msi' -Recurse | Select-Object -First 1
if ($null -eq $msi) { throw "The WiX build reported success but produced no .msi under $msiOutDir." }

$finalMsi = Join-Path $ReleaseDir $msi.Name
Copy-Item -LiteralPath $msi.FullName -Destination $finalMsi -Force

$msiHash = (Get-FileHash -LiteralPath $finalMsi -Algorithm SHA256).Hash

$manifest = [ordered] @{
    schemaVersion  = 1
    product        = 'PrintFlow Studio'
    version        = $Version
    architecture   = 'win-x64'
    publishModel   = 'self-contained'
    installer      = [ordered] @{
        fileName = $msi.Name
        bytes    = (Get-Item -LiteralPath $finalMsi).Length
        sha256   = $msiHash
    }
    payload        = [ordered] @{
        policyVersion = $policy.policyVersion
        fileCount     = $staged.Count
        droppedCount  = $dropped.Count
        files         = @($staged | Sort-Object)
    }
    builtAtUtc     = (Get-Date).ToUniversalTime().ToString('o')
    builtOn        = $env:COMPUTERNAME
    autoUpdate     = 'none'
    signing        = 'none'
}
$manifestPath = Join-Path $ReleaseDir 'build-manifest.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host ''
Write-Host "Installer : $finalMsi" -ForegroundColor Green
Write-Host "SHA-256   : $msiHash"
Write-Host "Manifest  : $manifestPath"
Write-Host "Symbols   : $SymbolsDir  (release record; not installed)"
