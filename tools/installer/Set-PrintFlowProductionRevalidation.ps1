<#
.SYNOPSIS
    Records that this PrintFlow Studio installation has been revalidated (SCRUM-11123 Part H).

.DESCRIPTION
    Writes the production revalidation record that PrintFlow's environment verification reads.
    Until a record exists that matches the installed product version, the configured preset and
    its digest, the current Windows build and the accepted Meitu and Photoshop binaries,
    Production is closed. Installing a new PrintFlow version, upgrading Windows, or accepting a
    new preset that names a different Meitu or Photoshop binary each invalidate the record and
    close Production again — which is the whole point.

    This script is the only thing that writes that record. PrintFlow itself cannot: there is no
    writer for it anywhere in the application, and an architecture test asserts so. An
    application that could write its own production approval would be approving itself.

    THE STANDARD REGRESSION SET IS CHECKED, NOT ASSERTED.

    SCRUM-11123 requires the standard test set to be rerun and to pass before production use
    resumes. SCRUM-11065 defines that set: at least a normal JPG portrait, a complex background
    with fine hair, a transparent PNG, a complete customer design, a PSD with a compatible
    composite preview, a single-page PDF, and a reference production TIFF, each with its expected
    processing path and properties recorded.

    This script verifies that the set named by -StandardRegressionSetPath actually contains every
    one of those categories before it will record a pass. If the set is absent or incomplete, it
    writes the record with the standard regression set marked NotAvailable, which PrintFlow reads
    as blocking. There is no switch that turns "the set does not exist" into "the set passed",
    and adding one would defeat the requirement this script exists to enforce.

.PARAMETER InstallFolder
    Where PrintFlow Studio is installed. Defaults to %ProgramFiles%\PrintFlow Studio.

.PARAMETER WorkspaceRoot
    The production workspace. Defaults to the Workspace:Root in the installed configuration.

.PARAMETER EnvironmentReadinessPassed
    Set only after Environment Readiness has been run from Settings and reported PASS.

.PARAMETER StandardRegressionSetPath
    The root of the standard local regression set, i.e. the folder holding its manifests.

.PARAMETER StandardRegressionSetResult
    A JSON file produced by the regression run, with { "setId": ..., "status": "Passed" |
    "Failed", "completedAtLocal": ..., "evidencePath": ... }.
#>
[CmdletBinding()]
param(
    [string] $InstallFolder = (Join-Path $env:ProgramFiles 'PrintFlow Studio'),
    [string] $WorkspaceRoot,
    [switch] $EnvironmentReadinessPassed,
    [string] $StandardRegressionSetPath,
    [string] $StandardRegressionSetResult
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The seven categories SCRUM-11065 requires of the standard local regression set. Quoted from the
# original requirement rather than paraphrased, because this list is what decides whether a
# production gate opens.
$RequiredCategories = @(
    'NORMAL_JPG_PORTRAIT',
    'COMPLEX_BACKGROUND_FINE_HAIR',
    'TRANSPARENT_PNG',
    'COMPLETE_CUSTOMER_DESIGN',
    'PSD_WITH_COMPOSITE_PREVIEW',
    'SINGLE_PAGE_PDF',
    'REFERENCE_PRODUCTION_TIFF'
)

# --------------------------------------------------------------------------------------------
# Read the installation
# --------------------------------------------------------------------------------------------
$appSettingsPath = Join-Path $InstallFolder 'appsettings.json'
$localSettingsPath = Join-Path $InstallFolder 'appsettings.local.json'
$exePath = Join-Path $InstallFolder 'PrintFlow.App.exe'

if (-not (Test-Path $appSettingsPath)) { throw "No appsettings.json under '$InstallFolder'." }
if (-not (Test-Path $exePath)) { throw "No PrintFlow.App.exe under '$InstallFolder'." }

$settings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json
if (Test-Path $localSettingsPath) {
    $localSettings = Get-Content -LiteralPath $localSettingsPath -Raw | ConvertFrom-Json
    foreach ($section in $localSettings.PSObject.Properties.Name) {
        $settings | Add-Member -NotePropertyName $section -NotePropertyValue $localSettings.$section -Force
    }
}
if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) { $WorkspaceRoot = $settings.Workspace.Root }

# Three components, matching the product version and not the assembly's four-part spelling.
$fileVersion = (Get-Item -LiteralPath $exePath).VersionInfo
$rawVersion = $fileVersion.ProductVersion
if ([string]::IsNullOrWhiteSpace($rawVersion)) { $rawVersion = $fileVersion.FileVersion }
$parts = ($rawVersion -split '[^0-9]') | Where-Object { $_ -ne '' }
if ($parts.Count -lt 3) { throw "Could not read a three-part product version from '$exePath' (saw '$rawVersion')." }
$productVersion = "$($parts[0]).$($parts[1]).$($parts[2])"

$presetPath = Join-Path $WorkspaceRoot $settings.Preset.Path
if (-not (Test-Path $presetPath)) { throw "The configured preset manifest '$presetPath' does not exist." }
$presetSha = (Get-FileHash -LiteralPath $presetPath -Algorithm SHA256).Hash
if ($presetSha -ne $settings.Preset.ExpectedSha256) {
    throw ("The preset manifest does not hash to the configured ExpectedSha256. Resolve the preset " +
           "integrity failure before recording a revalidation.")
}

$preset = Get-Content -LiteralPath $presetPath -Raw | ConvertFrom-Json
$meituSha = $preset.meituContract.executableSha256
$photoshopSha = $preset.photoshopContract.executableSha256
$osBuild = [string] (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber

Write-Host "Product version : $productVersion"
Write-Host "Preset          : $($settings.Preset.Id) $($settings.Preset.Version)"
Write-Host "Windows build   : $osBuild"

# --------------------------------------------------------------------------------------------
# The standard regression set — verified, never asserted
# --------------------------------------------------------------------------------------------
$regressionStatus = 'NotAvailable'
$regressionSetId = $null
$regressionCompleted = $null
$regressionEvidence = $null
$missingCategories = @($RequiredCategories)

if (-not [string]::IsNullOrWhiteSpace($StandardRegressionSetPath) -and (Test-Path $StandardRegressionSetPath)) {
    $present = New-Object System.Collections.Generic.HashSet[string]
    foreach ($manifest in (Get-ChildItem -LiteralPath $StandardRegressionSetPath -Recurse -Filter '*.json' -File)) {
        try {
            $entry = Get-Content -LiteralPath $manifest.FullName -Raw | ConvertFrom-Json
        } catch { continue }
        if ($null -ne $entry.PSObject.Properties['category'] -and -not [string]::IsNullOrWhiteSpace($entry.category)) {
            [void] $present.Add(([string] $entry.category).ToUpperInvariant())
        }
    }
    $missingCategories = @($RequiredCategories | Where-Object { -not $present.Contains($_) })

    if ($missingCategories.Count -eq 0) {
        if ([string]::IsNullOrWhiteSpace($StandardRegressionSetResult) -or -not (Test-Path $StandardRegressionSetResult)) {
            Write-Host ''
            Write-Host ('The regression set is complete but no run result was supplied. ' +
                        'Recording the set as NotAvailable: a set that exists is not a set that ran.') -ForegroundColor Yellow
        } else {
            $result = Get-Content -LiteralPath $StandardRegressionSetResult -Raw | ConvertFrom-Json
            $regressionSetId = $result.setId
            $regressionCompleted = $result.completedAtLocal
            $regressionEvidence = $result.evidencePath
            if ($result.status -eq 'Passed') { $regressionStatus = 'Passed' } else { $regressionStatus = 'Failed' }
        }
    }
}

if ($regressionStatus -ne 'Passed') {
    Write-Host ''
    Write-Host 'STANDARD REGRESSION SET: NOT PASSED' -ForegroundColor Red
    if ($missingCategories.Count -gt 0) {
        Write-Host ('The standard local regression set required by SCRUM-11065 is absent or ' +
                    'incomplete. Missing categories:') -ForegroundColor Red
        foreach ($category in $missingCategories) { Write-Host "  - $category" -ForegroundColor Red }
    }
    Write-Host ('A record will still be written, stating this honestly. PrintFlow reads it as ' +
                'blocking, so Production stays closed until the set exists and passes.') -ForegroundColor Red
}

# --------------------------------------------------------------------------------------------
# Write the record
# --------------------------------------------------------------------------------------------
$record = [ordered] @{
    schemaVersion              = 1
    productVersion             = $productVersion
    presetId                   = $settings.Preset.Id
    presetVersion              = $settings.Preset.Version
    presetSha256               = $presetSha
    operatingSystemBuild       = $osBuild
    meituSha256                = $meituSha
    photoshopSha256            = $photoshopSha
    environmentReadinessPassed = [bool] $EnvironmentReadinessPassed
    standardRegressionSet      = [ordered] @{
        setId           = $regressionSetId
        status          = $regressionStatus
        completedAtLocal = $regressionCompleted
        evidencePath    = $regressionEvidence
    }
    attestedBy                 = "$env:USERDOMAIN\$env:USERNAME"
    attestedAtLocal            = (Get-Date).ToString('o')
}

$recordPath = Join-Path $WorkspaceRoot 'Revalidation\production-revalidation.json'
New-Item -ItemType Directory -Path (Split-Path -Parent $recordPath) -Force | Out-Null
if (Test-Path $recordPath) {
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    Copy-Item -LiteralPath $recordPath -Destination "$recordPath.superseded-$stamp" -Force
}
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $recordPath -Encoding utf8

Write-Host ''
Write-Host "Record written : $recordPath"
Write-Host "Readiness      : $([bool] $EnvironmentReadinessPassed)"
Write-Host "Regression set : $regressionStatus"

if ($EnvironmentReadinessPassed -and $regressionStatus -eq 'Passed') {
    Write-Host ''
    Write-Host 'This installation is revalidated. Production may resume.' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host 'Production remains CLOSED. Both Environment Readiness and the standard regression set must pass.' -ForegroundColor Red
    exit 2
}
