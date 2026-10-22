param([string]$CandidateId = 'e6-20261022-review')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (& git rev-parse --show-toplevel).Trim().Replace('/', '\')
$expected = 'C:\Users\admin\.codex\worktrees\scrum-11173-guided-acceptance\printflow-Studio'
if ($repo -cne $expected) { throw 'This evidence script is bound to the E6 worktree.' }
if ($CandidateId -notmatch '^e6-[A-Za-z0-9-]+$') { throw 'Use a fresh E6 candidate leaf.' }
$artifactRoot = Join-Path $repo 'artifacts\pf-opux-scrum11154-workstation-entry'
$build = Join-Path $artifactRoot ('build\' + $CandidateId)
$manifest = Join-Path $artifactRoot ('manifests\' + $CandidateId)
$buildLog = Join-Path $artifactRoot ($CandidateId + '-build.log')
$run = Join-Path $artifactRoot ('runs\' + $CandidateId)
$evidence = Join-Path $repo 'output\ui\SCRUM-11173\candidate'
foreach ($path in @($build, $manifest, $buildLog, $run)) {
    if (Test-Path -LiteralPath $path) { throw "Fresh candidate path required; preserve existing evidence: $path" }
}
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
& dotnet build tests/PrintFlow.WorkstationEntry/PrintFlow.WorkstationEntry.csproj --disable-build-servers --configuration Debug --output $build > $buildLog 2>&1
$buildExit = $LASTEXITCODE
Copy-Item -LiteralPath $buildLog -Destination (Join-Path $evidence 'build.log')
if ($buildExit -ne 0) { throw "Candidate build failed ($buildExit); inspect retained build.log." }
& (Join-Path $repo 'tools\New-WorkstationEntryCandidate.ps1') -RepositoryRoot $repo -BuildDirectory $build -ManifestDirectory $manifest -RunId $CandidateId -BuildEvidence $buildLog -ExcludedRoot @('D:\Repositories\printflow-Studio') > (Join-Path $evidence 'generate.log') 2>&1
foreach ($name in @('candidate-manifest.json','scenario-manifest.json','candidate-provenance.json')) {
    Copy-Item -LiteralPath (Join-Path $manifest $name) -Destination (Join-Path $evidence $name)
}
$candidateArgs = @{
    Mode='Validate'; Root=$run; ScenarioManifest=(Join-Path $manifest 'scenario-manifest.json')
    CandidateManifest=(Join-Path $manifest 'candidate-manifest.json'); HostAssembly=(Join-Path $build 'PrintFlow.WorkstationEntry.dll')
}
& (Join-Path $repo 'tools\Start-WorkstationEntry.ps1') @candidateArgs > (Join-Path $evidence 'validate.log') 2>&1
$validateExit = $LASTEXITCODE
$rootExists = Test-Path -LiteralPath $run
[ordered]@{
    CandidateId=$CandidateId; SourceHead=(& git rev-parse HEAD).Trim(); BuildExit=$buildExit; ValidateExit=$validateExit
    RuntimeRootExists=$rootExists; RuntimeGraph='NOT RUN'; PrepareAndSmoke='NOT RUN'; Interactive='NOT RUN'
    Novice='NOT RUN'; Workstation='NOT RUN'; Arguments=$candidateArgs
    CandidateManifestSha256=(Get-FileHash -LiteralPath $candidateArgs.CandidateManifest).Hash
    ScenarioManifestSha256=(Get-FileHash -LiteralPath $candidateArgs.ScenarioManifest).Hash
    HostSha256=(Get-FileHash -LiteralPath $candidateArgs.HostAssembly).Hash
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'validation-summary.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $evidence 'validate.log')
if ($validateExit -ne 0 -or $rootExists) { throw 'Static validation did not satisfy the expected no-runtime boundary.' }

