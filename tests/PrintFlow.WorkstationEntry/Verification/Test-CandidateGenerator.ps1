# Metadata-only regression checks. Never constructs or runs the application graph.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$BuildDirectory,
    [Parameter(Mandatory)][string]$BuildEvidence,
    [Parameter(Mandatory)][string]$ManifestDirectory
)
$ErrorActionPreference='Stop'
$repo=$RepositoryRoot
$build=$BuildDirectory
$runId='generator-contract-test'
$root=Join-Path $repo "artifacts\pf-opux-scrum11154-workstation-entry\runs\$runId"
if (Test-Path -LiteralPath $root) { throw 'Test needs absent run root; will not adopt it.' }
& (Join-Path $repo 'tools\New-WorkstationEntryCandidate.ps1') -RepositoryRoot $repo -BuildDirectory $build -ManifestDirectory $ManifestDirectory -RunId $runId -BuildEvidence $BuildEvidence
if (-not (Test-Path -LiteralPath (Join-Path $ManifestDirectory 'candidate-manifest.json'))) { throw 'BEHAVIOR ASSERTION: valid built candidate must produce its candidate manifest.' }
$candidate=Get-Content (Join-Path $ManifestDirectory 'candidate-manifest.json') -Raw | ConvertFrom-Json
$scenario=Get-Content (Join-Path $ManifestDirectory 'scenario-manifest.json') -Raw | ConvertFrom-Json
$provenance=Get-Content (Join-Path $ManifestDirectory 'candidate-provenance.json') -Raw | ConvertFrom-Json
if ($scenario.RunId -ne $runId -or $scenario.AdapterMode -ne 'Fake' -or ($scenario.Families -join ',') -ne 'F1,F2,F3,F4,F5,F6') { throw 'Scenario contract mismatch' }
if ($candidate.SourceHead -ne (git -C $repo rev-parse HEAD) -or $provenance.SourceHead -ne $candidate.SourceHead) { throw 'Source identity mismatch' }
$assemblies=@($candidate.Files | Where-Object { $_.Mvid })
if (@($assemblies | Where-Object { [IO.Path]::GetFileName($_.Path) -eq 'PrintFlow.WorkstationEntry.dll' }).Count -ne 1) { throw 'Missing host MVID' }
if (@($assemblies | Where-Object { [IO.Path]::GetFileName($_.Path) -eq 'PrintFlow.App.dll' }).Count -ne 1) { throw 'Missing product MVID' }
foreach ($file in $candidate.Files) { if ((Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash -ne $file.Sha256) { throw "Hash mismatch $($file.Path)" } }
if (Test-Path -LiteralPath $root) { throw 'Generator created runtime root' }
$refused=$false
try { & (Join-Path $repo 'tools\New-WorkstationEntryCandidate.ps1') -RepositoryRoot $repo -BuildDirectory $build -ManifestDirectory $ManifestDirectory -RunId $runId -BuildEvidence $BuildEvidence } catch { $refused=$true }
if (-not $refused) { throw 'Existing manifest destination must refuse overwrite' }
Write-Output "PASS: actual source/artifact hashes, host/product MVIDs, F1-F6 Fake manifest, no runtime root, overwrite refusal. Files=$($candidate.Files.Count)"
