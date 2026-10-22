param([ValidateSet('Q01','Q02','Final')][string]$Phase='Final')
$ErrorActionPreference='Stop'
$checks=$PSScriptRoot
function Assert-Counters([string]$Name,[int]$Total,[int]$Passed,[int]$Failed) {
    [xml]$trx=Get-Content -LiteralPath (Join-Path $checks ($Name+'.trx')) -Raw
    $counts=$trx.TestRun.ResultSummary.Counters
    if ([int]$counts.total -ne $Total -or [int]$counts.passed -ne $Passed -or [int]$counts.failed -ne $Failed -or [int]$counts.notExecuted -ne 0) {
        throw "Unexpected counters: $Name"
    }
    Write-Output "$Name total=$Total passed=$Passed failed=$Failed"
}
Assert-Counters 'e6-baseline' 1485 1471 14
Assert-Counters 'e6-q01-final' 45 45 0
$comparison=Get-Content -LiteralPath (Join-Path $checks 'e6-baseline-vs-e5.json') -Raw | ConvertFrom-Json
if (-not $comparison.ExactMatch) { throw 'Baseline failure comparison differs.' }
$old=Join-Path $checks '..\..\SCRUM-11172\checks\safe-ui-exclusions.json'
$current=Join-Path $checks 'safe-ui-exclusions.json'
if ((Get-FileHash -LiteralPath $old).Hash -ne (Get-FileHash -LiteralPath $current).Hash) { throw 'Safety exclusions changed.' }
if (@(Get-Content -LiteralPath $current -Raw | ConvertFrom-Json).Count -ne 11) { throw 'Expected 11 explicit exclusions.' }
$visuals=Join-Path $checks '..\visuals'
$pngs=@(Get-ChildItem -LiteralPath $visuals -Filter '*.png')
if ($pngs.Count -ne 17) { throw "Expected 17 final images, found $($pngs.Count)." }
foreach ($png in $pngs) {
    $geometry=Get-Content -LiteralPath ([IO.Path]::ChangeExtension($png.FullName,'.json')) -Raw | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath $png.FullName).Hash -ne $geometry.ScreenshotSha256 -or $geometry.Dpi -ne 96) { throw "Image/geometry mismatch: $($png.Name)" }
}
Write-Output 'Safety exclusions unchanged; 17 PNG/geometry hashes match at offscreen 96 DPI.'
foreach ($reference in (Get-Content -LiteralPath (Join-Path $checks 'readonly-reference-hashes.json') -Raw | ConvertFrom-Json)) {
    if ((Get-FileHash -LiteralPath $reference.Path).Hash -ne $reference.Hash) { throw "Readonly reference changed: $($reference.Path)" }
}
if ($Phase -ne 'Q01') {
    Assert-Counters 'e6-ui-architecture-final' 1494 1480 14
    Assert-Counters 'e6-workflow-persistence-final' 277 277 0
    foreach ($name in 'e6-final-vs-e5.json','e6-final-vs-baseline.json') {
        if (-not (Get-Content -LiteralPath (Join-Path $checks $name) -Raw | ConvertFrom-Json).ExactMatch) { throw "$name differs" }
    }
    $oldFilter=(Get-Content -LiteralPath (Join-Path $checks '..\..\SCRUM-11172\checks\e5-workflow-persistence-final-filter.txt') -Raw).Trim()
    $currentFilter=(Get-Content -LiteralPath (Join-Path $checks 'e6-workflow-persistence-final-filter.txt') -Raw).Trim()
    if ($oldFilter -cne $currentFilter) { throw 'Persistence selection differs from E5.' }
    $coverage=Get-Content -LiteralPath (Join-Path $checks 'coverage-map.json') -Raw | ConvertFrom-Json
    foreach ($source in $coverage.Sources) {
        if ((Get-FileHash -LiteralPath (Join-Path $checks $source.Path)).Hash -ne $source.Sha256) { throw "Coverage source changed: $($source.Path)" }
    }
    if (@($coverage.Groups).Count -ne 15) { throw 'Coverage group missing.' }
}
if ($Phase -eq 'Final') {
    $candidateRoot=Join-Path $checks '..\candidate'
    $summary=Get-Content -LiteralPath (Join-Path $candidateRoot 'validation-summary.json') -Raw | ConvertFrom-Json
    if ($summary.BuildExit -ne 0 -or $summary.ValidateExit -ne 0 -or $summary.RuntimeRootExists) { throw 'Candidate static boundary failed.' }
    if (Test-Path -LiteralPath $summary.Arguments.Root) { throw 'Candidate runtime root now exists.' }
    foreach ($entry in @(@('candidate-manifest.json',$summary.CandidateManifestSha256),@('scenario-manifest.json',$summary.ScenarioManifestSha256))) {
        if ((Get-FileHash -LiteralPath (Join-Path $candidateRoot $entry[0])).Hash -ne $entry[1]) { throw "Copied candidate evidence changed: $($entry[0])" }
    }
    $validation=Get-Content -LiteralPath (Join-Path $candidateRoot 'validate.log') -Raw | ConvertFrom-Json
    if ($validation.Status -cne 'STATIC_VALIDATED' -or $validation.GraphConstructed -ne $false) { throw 'Candidate graph boundary not confirmed.' }
    if ($validation.CandidateHash -cne $summary.CandidateManifestSha256 -or $validation.ScenarioHash -cne $summary.ScenarioManifestSha256) { throw 'Static validation hashes differ.' }
    $manifest=Get-Content -LiteralPath (Join-Path $candidateRoot 'candidate-manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.SourceHead -cne $summary.SourceHead) { throw 'Candidate source identity mismatch.' }
    foreach ($file in $manifest.Files) {
        if ((Get-FileHash -LiteralPath $file.Path).Hash -ne $file.Sha256) { throw "Candidate changed: $($file.Path)" }
    }
    $imageManifest=Get-Content -LiteralPath (Join-Path $checks '..\visual-manifest.json') -Raw | ConvertFrom-Json
    foreach ($file in $imageManifest.Files) {
        if ((Get-FileHash -LiteralPath (Join-Path $checks ('..\'+$file.Path))).Hash -ne $file.Sha256) { throw "Visual manifest mismatch: $($file.Path)" }
    }
    $forbidden=@(& git diff --name-only 65c5853e0e3f4f828b7851479bb418d043b0e44e -- src/PrintFlow.Domain src/PrintFlow.Workflow src/PrintFlow.Infrastructure)
    if ($forbidden.Count) { throw 'Business implementation changed outside E6 scope.' }
    Write-Output "Candidate source/binary hashes match ($($manifest.Files.Count) files); no runtime root or business implementation changes."
}
Write-Output "$Phase evidence verified; actual workstation/novice/production remain NOT RUN."
