<#
.SYNOPSIS
    Runs the standard local regression set on the fixed workstation (SCRUM-11065).

.DESCRIPTION
    The one entry point. SCRUM-11123's runbook deliberately left "how the set is run" undefined
    because there was no set; this defines it, and there is intentionally no second runner.

    Two layers, and the difference between them is the whole point:

      Layer 1  Preflight. Static, offline, opens no application. Seven categories present and
               each claimed exactly once; every manifest readable and free of PENDING; every
               referenced file on disk; every SHA-256 recomputed from the bytes and matching;
               format and structural preconditions right for PSD, PDF and TIFF; the expected
               processing path recorded; one consistent set identity. Seconds, not minutes, and
               safe to run any time.

      Layer 2  The workstation run. Drives the real PrintFlow production foundations and the
               real external applications for the categories whose recorded path names them.
               This is the only layer that produces regression evidence.

    A Layer 1 pass is NOT a regression pass, and this script will not let one be mistaken for the
    other: -PreflightOnly writes no result.json, and the revalidation tool reads result.json.

.PARAMETER SetRoot
    The regression set. Defaults to D:\PrintFlowStudio\TestData\v1.

.PARAMETER PreflightOnly
    Run Layer 1 and stop. Produces no run result, because nothing ran.

.PARAMETER Categories
    Run only these categories in Layer 2. For staged or resumed runs; a partial run can never
    report Passed, because the result's status is derived from all seven.

.PARAMETER RunId
    Name the run folder. Defaults to a local timestamp.

.PARAMETER RecordVisualReview
    A JSON file of decisions for the qualitative checks a completed run left open. With
    -RunId, re-derives that run's verdict from evidence already on disk; nothing is re-run, and a
    decision can only turn a Pending case into a Passed or Failed one. Shape:

        {
          "decidedBy": "DESKTOP-0BG8884\\admin",
          "decidedAtLocal": "2026-09-09T19:20:00+12:00",
          "decisions": [
            { "id": "PORTRAIT-VISUAL-001", "outcome": "Passed", "notes": "..." }
          ]
        }

.PARAMETER Configuration
    Build configuration for the test host. Release by default.

.EXAMPLE
    .\Invoke-PrintFlowStandardRegressionSet.ps1 -PreflightOnly

.EXAMPLE
    .\Invoke-PrintFlowStandardRegressionSet.ps1 -RunId 20260909-183000

.EXAMPLE
    .\Invoke-PrintFlowStandardRegressionSet.ps1 -RunId 20260909-183000 -RecordVisualReview .\decisions.json
#>
[CmdletBinding()]
param(
    [string] $SetRoot = 'D:\PrintFlowStudio\TestData\v1',
    [switch] $PreflightOnly,
    [string[]] $Categories,
    [string] $RunId,
    [string] $RecordVisualReview,
    [string] $Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RequiredCategories = @(
    'NORMAL_JPG_PORTRAIT',
    'COMPLEX_BACKGROUND_FINE_HAIR',
    'TRANSPARENT_PNG',
    'COMPLETE_CUSTOMER_DESIGN',
    'PSD_WITH_COMPOSITE_PREVIEW',
    'SINGLE_PAGE_PDF',
    'REFERENCE_PRODUCTION_TIFF'
)

$KnownComparisonModes = @('Structural', 'ExactHash', 'ReferenceProperties', 'ManualVisual')

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$testProject = Join-Path $repositoryRoot 'tests\PrintFlow.Tests\PrintFlow.Tests.csproj'

# The .NET 10 SDK is installed per-user on this workstation and the machine-wide 8.x on PATH
# shadows it, so `dotnet` unqualified fails with "a compatible SDK was not found".
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }

# ==========================================================================================
# Layer 1 — preflight
# ==========================================================================================
function Invoke-Preflight {
    param([string] $Root)

    $problems = New-Object System.Collections.Generic.List[string]
    $manifestFolder = Join-Path $Root 'manifests'

    if (-not (Test-Path -LiteralPath $manifestFolder)) {
        $problems.Add("No manifests folder under '$Root'.")
        return ,$problems
    }

    $seenCategories = @{}
    $setIds = New-Object System.Collections.Generic.HashSet[string]

    foreach ($file in Get-ChildItem -LiteralPath $manifestFolder -Filter '*.json' -File | Sort-Object Name) {
        $id = $file.BaseName
        $raw = Get-Content -LiteralPath $file.FullName -Raw

        try { $manifest = $raw | ConvertFrom-Json }
        catch { $problems.Add("${id}: unreadable manifest — $($_.Exception.Message)"); continue }

        # PENDING is the absence of an outcome. An accepted fixed set states outcomes, and the
        # set inherited exactly one such field for this slice to resolve.
        if ($raw -match '"PENDING"') {
            $problems.Add("${id}: still records a PENDING expectation.")
        }

        foreach ($required in @('schemaVersion', 'setId', 'fixtureId', 'category', 'file',
                                'expectedWorkflow', 'expectedProcessingPath', 'comparisonPolicy')) {
            if ($null -eq $manifest.PSObject.Properties[$required]) {
                $problems.Add("${id}: no '$required'.")
            }
        }
        if ($problems.Count -gt 0 -and $null -eq $manifest.PSObject.Properties['file']) { continue }

        if ($manifest.schemaVersion -ne 2) { $problems.Add("${id}: schema $($manifest.schemaVersion); expected 2.") }
        [void] $setIds.Add([string] $manifest.setId)

        $category = ([string] $manifest.category).ToUpperInvariant()
        if ($seenCategories.ContainsKey($category)) {
            $problems.Add("Category $category is claimed by both $($seenCategories[$category]) and $id.")
        } else {
            $seenCategories[$category] = $id
        }

        # @(...) because ConvertFrom-Json unrolls a one-element array to a bare string, which has
        # no Count and throws under StrictMode.
        if (@($manifest.expectedProcessingPath).Count -lt 1) {
            $problems.Add("${id}: records no expected processing path, which the requirement asks for by name.")
        }
        if ($KnownComparisonModes -notcontains $manifest.comparisonPolicy.mode) {
            $problems.Add("${id}: unknown comparison mode '$($manifest.comparisonPolicy.mode)'.")
        }

        $path = [string] $manifest.file.path
        if (-not (Test-Path -LiteralPath $path)) {
            $problems.Add("${id}: '$path' does not exist.")
            continue
        }

        $item = Get-Item -LiteralPath $path
        if ($item.Length -ne $manifest.file.length) {
            $problems.Add("${id}: $($item.Length) bytes on disk; the manifest records $($manifest.file.length).")
        }

        # Recomputed, never trusted. A set whose integrity rested on file names would accept a
        # re-exported image as the accepted one, and every later "the set still passes" would be
        # a claim about a different set.
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($actual -ne $manifest.file.sha256) {
            $problems.Add("${id}: SHA-256 drift. Manifest $($manifest.file.sha256); bytes hash to $actual.")
        }

        switch ($category) {
            'PSD_WITH_COMPOSITE_PREVIEW' {
                if ($null -eq $manifest.file.PSObject.Properties['psd']) {
                    $problems.Add("${id}: no psd structural facts.")
                } else {
                    if ($manifest.file.psd.hasRealMergedData -ne $true) {
                        $problems.Add("${id}: hasRealMergedData is not true; PrintFlow refuses a PSD without image resource 1057.")
                    }
                    if ($manifest.file.psd.colourMode -ne 'RGB') {
                        $problems.Add("${id}: PSD colour mode is $($manifest.file.psd.colourMode); PrintFlow accepts RGB.")
                    }
                }
            }
            'SINGLE_PAGE_PDF' {
                if ($null -eq $manifest.file.PSObject.Properties['pdf']) {
                    $problems.Add("${id}: no pdf structural facts.")
                } else {
                    if ($manifest.file.pdf.pageCount -ne 1) {
                        $problems.Add("${id}: pageCount is $($manifest.file.pdf.pageCount); the positive category is a single page.")
                    }
                    if ($manifest.file.pdf.isPasswordProtected -ne $false) {
                        $problems.Add("${id}: the PDF is encrypted; PrintFlow refuses encrypted PDFs.")
                    }
                }
            }
            'REFERENCE_PRODUCTION_TIFF' {
                if ($null -eq $manifest.file.PSObject.Properties['tiff']) {
                    $problems.Add("${id}: no tiff structural facts.")
                } else {
                    if ($manifest.file.tiff.samplesPerPixel -ne 5) {
                        $problems.Add("${id}: samplesPerPixel is $($manifest.file.tiff.samplesPerPixel); the accepted structure is CMYK + W1.")
                    }
                    if ($manifest.file.tiff.photometricInterpretation -ne 5) {
                        $problems.Add("${id}: photometricInterpretation is $($manifest.file.tiff.photometricInterpretation); the accepted value is 5 (separated).")
                    }
                    if ($manifest.file.tiff.compression -ne 1) {
                        $problems.Add("${id}: compression is $($manifest.file.tiff.compression); the accepted TIFF is uncompressed.")
                    }
                }
            }
        }
    }

    foreach ($required in $RequiredCategories) {
        if (-not $seenCategories.ContainsKey($required)) {
            $problems.Add("Required category $required is absent.")
        }
    }

    if ($setIds.Count -gt 1) {
        $problems.Add("The manifests disagree about the set identity: $($setIds -join ', ').")
    }

    # Comma-wrapped: returning a List directly makes PowerShell enumerate it, so an empty list
    # becomes $null and a one-problem list becomes a bare string.
    return ,$problems
}

Write-Host ''
Write-Host '== Layer 1: preflight ==' -ForegroundColor Cyan
Write-Host "Set: $SetRoot"

$preflightProblems = Invoke-Preflight -Root $SetRoot
if ($preflightProblems.Count -gt 0) {
    foreach ($problem in $preflightProblems) { Write-Host "  FAIL $problem" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'PREFLIGHT FAILED. The set is not fit to run and nothing was started.' -ForegroundColor Red
    exit 2
}

Write-Host ("  OK  {0} categories, every manifest readable, every hash recomputed and matching." -f $RequiredCategories.Count) -ForegroundColor Green

if ($PreflightOnly) {
    Write-Host ''
    Write-Host 'Preflight only. No run result was written: a set that exists is not a set that passed.' -ForegroundColor Yellow
    exit 0
}

# ==========================================================================================
# Layer 2 — the workstation run
# ==========================================================================================
if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = (Get-Date).ToString('yyyyMMdd-HHmmss') }
$runFolder = Join-Path $SetRoot "runs\$RunId"

$env:PRINTFLOW_REGRESSION_SET_ROOT = $SetRoot
$env:PRINTFLOW_REGRESSION_RUN_ID = $RunId

if ($RecordVisualReview) {
    # Re-derivation, not a run. The artefacts already exist and the reviewer has looked at them.
    if (-not (Test-Path -LiteralPath (Join-Path $runFolder 'result.json'))) {
        throw "No completed run at '$runFolder'. Run the set before recording a visual review of it."
    }
    Write-Host ''
    Write-Host '== Recording the visual review ==' -ForegroundColor Cyan
    $env:PRINTFLOW_REGRESSION_REAGGREGATE = $runFolder
    $env:PRINTFLOW_REGRESSION_VISUAL_DECISIONS = (Resolve-Path -LiteralPath $RecordVisualReview).Path
    $env:PRINTFLOW_STANDARD_REGRESSION_SET = $null
    $filter = 'FullyQualifiedName~StandardRegressionSetWorkstationSmoke.Re_derive_a_completed_run'
} else {
    Write-Host ''
    Write-Host '== Layer 2: fixed-workstation run ==' -ForegroundColor Cyan
    Write-Host "Run: $runFolder"
    Write-Host 'Real Meitu and real Photoshop are driven for the categories whose recorded path names them.'
    $env:PRINTFLOW_STANDARD_REGRESSION_SET = '1'
    $env:PRINTFLOW_REGRESSION_REAGGREGATE = $null
    $env:PRINTFLOW_REGRESSION_VISUAL_DECISIONS = $null
    if ($Categories) {
        $env:PRINTFLOW_REGRESSION_CATEGORIES = ($Categories -join ',')
        Write-Host ("Categories: {0} (a partial run can never report Passed)" -f $env:PRINTFLOW_REGRESSION_CATEGORIES) -ForegroundColor Yellow
    } else {
        $env:PRINTFLOW_REGRESSION_CATEGORIES = $null
    }
    $filter = 'FullyQualifiedName~StandardRegressionSetWorkstationSmoke.Run_the_standard_local_regression_set'
}

Write-Host ''
& $dotnet test $testProject -c $Configuration --nologo --filter $filter --logger 'console;verbosity=detailed'
$testExit = $LASTEXITCODE

# ==========================================================================================
# Report
# ==========================================================================================
$resultPath = Join-Path $runFolder 'result.json'
Write-Host ''
if (-not (Test-Path -LiteralPath $resultPath)) {
    Write-Host "No result was written at '$resultPath'." -ForegroundColor Red
    exit 3
}

$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
Write-Host '== Result ==' -ForegroundColor Cyan
Write-Host ("  setId  : {0}" -f $result.SetId)
Write-Host ("  status : {0}" -f $result.Status) -ForegroundColor $(if ($result.Status -eq 'Passed') { 'Green' } else { 'Red' })
Write-Host ("  verdict: {0}" -f $result.Verdict)
Write-Host ''
foreach ($case in $result.Cases) {
    $colour = switch ($case.Outcome) { 'Passed' { 'Green' } 'Pending' { 'Yellow' } default { 'Red' } }
    Write-Host ("  {0,-9} {1,-28} {2}" -f $case.Outcome, $case.Category, $case.Detail) -ForegroundColor $colour
}

Write-Host ''
Write-Host "Evidence: $runFolder"

if ($result.Status -eq 'Passed') {
    Write-Host ''
    Write-Host 'The set passed. Record the revalidation with:' -ForegroundColor Green
    Write-Host ("  tools\installer\Set-PrintFlowProductionRevalidation.ps1 -EnvironmentReadinessPassed " +
                "-StandardRegressionSetPath '$SetRoot\manifests' -StandardRegressionSetResult '$resultPath'")
    exit 0
}

Write-Host ''
if ($result.Cases | Where-Object { $_.Outcome -eq 'Pending' }) {
    Write-Host 'Some cases are waiting on a recorded visual review. Nothing marks those passed automatically.' -ForegroundColor Yellow
    Write-Host ("  Re-run with: -RunId $RunId -RecordVisualReview <decisions.json>")
}
exit $(if ($testExit -ne 0) { $testExit } else { 1 })
