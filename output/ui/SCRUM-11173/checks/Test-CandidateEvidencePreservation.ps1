param([string]$RunName = 'green')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (& git rev-parse --show-toplevel).Trim().Replace('/', '\')
$subject = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build-validate-candidate.ps1') -Raw
$boundRepo = 'C:\Users\admin\.codex\worktrees\scrum-11173-guided-acceptance\printflow-Studio'
$caseRoot = Join-Path $PSScriptRoot ('m1-fix\fixtures-' + $RunName + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $caseRoot | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
function Assert-True([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Snapshot([string]$Directory) {
    return @((Get-ChildItem -LiteralPath $Directory -File -Recurse | Sort-Object FullName) | ForEach-Object {
        [IO.Path]::GetRelativePath($Directory,$_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }) -join "`n"
}
function Case([string]$Name,[scriptblock]$Body) {
    $fixture = Join-Path $caseRoot $Name
    $legacy = Join-Path $fixture 'output\ui\SCRUM-11173\candidate'
    New-Item -ItemType Directory -Path $legacy -Force | Out-Null
    '{"BuildExit":0,"ValidateExit":0,"CandidateId":"reviewed"}' | Set-Content -LiteralPath (Join-Path $legacy 'validation-summary.json')
    'reviewed successful build' | Set-Content -LiteralPath (Join-Path $legacy 'build.log')
    $before = Snapshot $legacy
    New-Item -ItemType Directory -Path (Join-Path $fixture 'tools') | Out-Null
    # Only the workstation binding is relocated. All subject control flow and file writes are real.
    $copy = $subject.Replace($boundRepo,$fixture)
    Assert-True ($copy -cne $subject) 'The expected repository binding was not found.'
    $scriptPath = Join-Path $fixture 'subject.ps1'
    [IO.File]::WriteAllText($scriptPath,$copy)
    @'
param($RepositoryRoot,$BuildDirectory,$ManifestDirectory,$RunId,$BuildEvidence,$ExcludedRoot)
New-Item -ItemType Directory -Path $ManifestDirectory -Force | Out-Null
foreach ($name in 'candidate-manifest.json','scenario-manifest.json','candidate-provenance.json') {
    '{"Fixture":true}' | Set-Content -LiteralPath (Join-Path $ManifestDirectory $name)
}
'@ | Set-Content -LiteralPath (Join-Path $fixture 'tools\New-WorkstationEntryCandidate.ps1')
    @'
param($Mode,$Root,$ScenarioManifest,$CandidateManifest,$HostAssembly)
if ($Mode -cne 'Validate') { throw 'Unexpected runtime mode.' }
[ordered]@{Status= $(if ($global:M1ValidateExit -eq 0) {'STATIC_VALIDATED'} else {'INVALID'});GraphConstructed=$false} | ConvertTo-Json
$global:LASTEXITCODE=$global:M1ValidateExit
'@ | Set-Content -LiteralPath (Join-Path $fixture 'tools\Start-WorkstationEntry.ps1')
    $global:M1BuildCalls=0; $global:M1BuildExit=0; $global:M1ValidateExit=0
    function git {
        if ($args -contains '--show-toplevel') { return $fixture.Replace('\','/') }
        if ($args -contains 'HEAD') { return 'fixture-source' }
        throw 'Unexpected git command.'
    }
    function dotnet {
        $global:M1BuildCalls++
        $out = $args[[array]::IndexOf($args,'--output')+1]
        New-Item -ItemType Directory -Path $out | Out-Null
        'fake host bytes; no executable assembly' | Set-Content -LiteralPath (Join-Path $out 'PrintFlow.WorkstationEntry.dll')
        'Controlled build fixture, not a product build.'
        $global:LASTEXITCODE=$global:M1BuildExit
    }
    try {
        & $Body $fixture $scriptPath $legacy $before
        Assert-True ((Snapshot $legacy) -ceq $before) 'Reviewed evidence was overwritten.'
        $results.Add([pscustomobject]@{name=$Name;outcome='PASS'})
    } catch {
        $results.Add([pscustomobject]@{name=$Name;outcome='FAIL';message=$_.Exception.Message})
    }
}
Case 'fresh-success' {
    param($fixture,$scriptPath,$legacy,$before)
    & $scriptPath -CandidateId 'e6-success' | Out-Null
    $new = Join-Path $fixture 'output\ui\SCRUM-11173\candidates\e6-success'
    Assert-True (Test-Path -LiteralPath (Join-Path $new 'validation-summary.json')) 'Fresh candidate must have its own evidence directory.'
    $summary = Get-Content -LiteralPath (Join-Path $new 'validation-summary.json') -Raw | ConvertFrom-Json
    Assert-True ($summary.BuildExit -eq 0 -and $summary.ValidateExit -eq 0) 'Fresh success summary missing.'
}
Case 'build-failure' {
    param($fixture,$scriptPath,$legacy,$before)
    $global:M1BuildExit=7
    $failed=$false
    try { & $scriptPath -CandidateId 'e6-build-failure' | Out-Null } catch { $failed=$true }
    Assert-True $failed 'Failed build must reject candidate.'
    Assert-True ((Snapshot $legacy) -ceq $before) 'Failed build overwrote reviewed evidence.'
    $new = Join-Path $fixture 'output\ui\SCRUM-11173\candidates\e6-build-failure'
    Assert-True (Test-Path -LiteralPath (Join-Path $new 'build.log')) 'Failed build log must remain in its own candidate.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $new 'validation-summary.json'))) 'Failed build must not inherit a successful summary.'
}
Case 'existing-evidence-refused' {
    param($fixture,$scriptPath,$legacy,$before)
    $new = Join-Path $fixture 'output\ui\SCRUM-11173\candidates\e6-existing'
    New-Item -ItemType Directory -Path $new -Force | Out-Null
    'preserve this attempt' | Set-Content -LiteralPath (Join-Path $new 'build.log')
    $prior = Snapshot $new
    $failed=$false
    try { & $scriptPath -CandidateId 'e6-existing' | Out-Null } catch { $failed=$true }
    Assert-True $failed 'Existing candidate evidence must be refused.'
    Assert-True ($global:M1BuildCalls -eq 0) 'Existing evidence must be rejected before starting a build.'
    Assert-True ((Snapshot $new) -ceq $prior) 'Existing candidate attempt changed.'
}
Case 'two-fresh-candidates' {
    param($fixture,$scriptPath,$legacy,$before)
    & $scriptPath -CandidateId 'e6-first' | Out-Null
    $first = Join-Path $fixture 'output\ui\SCRUM-11173\candidates\e6-first'
    Assert-True (Test-Path -LiteralPath $first) 'First evidence directory missing.'
    $firstBefore = Snapshot $first
    & $scriptPath -CandidateId 'e6-second' | Out-Null
    Assert-True ((Snapshot $first) -ceq $firstBefore) 'Second candidate overwrote first candidate.'
    Assert-True (Test-Path -LiteralPath (Join-Path $fixture 'output\ui\SCRUM-11173\candidates\e6-second\validation-summary.json')) 'Second evidence directory missing.'
    $calls=$global:M1BuildCalls
    $failed=$false
    try { & $scriptPath -CandidateId 'e6-first' | Out-Null } catch { $failed=$true }
    Assert-True ($failed -and $global:M1BuildCalls -eq $calls) 'Repeated candidate ID must refuse before build.'
}
Case 'validation-failure' {
    param($fixture,$scriptPath,$legacy,$before)
    $global:M1ValidateExit=9
    $failed=$false
    try { & $scriptPath -CandidateId 'e6-invalid' | Out-Null } catch { $failed=$true }
    Assert-True $failed 'Validation failure must reject candidate.'
    $new = Join-Path $fixture 'output\ui\SCRUM-11173\candidates\e6-invalid'
    Assert-True (Test-Path -LiteralPath (Join-Path $new 'validation-summary.json')) 'Failed validation must retain its own summary.'
    $summary = Get-Content -LiteralPath (Join-Path $new 'validation-summary.json') -Raw | ConvertFrom-Json
    Assert-True ($summary.ValidateExit -eq 9) 'Failed validation cannot inherit an old successful summary.'
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot ('m1-fix\'+$RunName+'-results.json')) -Encoding utf8
$results | Format-Table -AutoSize
$failedCases=@($results | Where-Object outcome -eq 'FAIL').Count
Write-Output "$($results.Count) cases; $failedCases failures. Controlled filesystem fixtures only; no real build, App or desktop."
if ($failedCases -ne 0) { exit 1 }
exit 0
