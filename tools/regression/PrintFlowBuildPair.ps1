# Trusted local build-pair contract, shared by preparation, execution and publication.
# Read-only: never builds, enriches a run, writes approvals or contacts external applications.
function Get-PrintFlowProductAssemblyNames {
    @('PrintFlow.App.dll', 'PrintFlow.Domain.dll', 'PrintFlow.Infrastructure.dll', 'PrintFlow.Workflow.dll')
}

function Assert-PrintFlowPairInventory {
    param($Recorded, $Observed, [string[]] $Names, [string] $Side)
    if (@($Recorded).Count -ne $Names.Count -or @($Observed).Count -ne $Names.Count) {
        throw "Build origin: incomplete or duplicate $Side inventory."
    }
    foreach ($name in $Names) {
        $left = @($Recorded | Where-Object { $_.Name -eq $name })
        $right = @($Observed | Where-Object { $_.Name -eq $name })
        if ($left.Count -ne 1 -or $right.Count -ne 1 -or
            $left[0].Sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
            $left[0].Sha256 -ne $right[0].Sha256) {
            throw "Build origin: $Side bytes do not match for $name (matching version labels are insufficient)."
        }
    }
}

function Read-PrintFlowPairFiles {
    param([string] $Folder, [string[]] $Names)
    foreach ($name in $Names) {
        if ([IO.Path]::IsPathRooted($name) -or $name -match '(^|[/\\])\.\.([/\\]|$)') {
            throw 'Build origin: artifact name escapes its output folder.'
        }
        $path = Join-Path $Folder $name
        @{ Name = $name; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash }
    }
}

function Read-PrintFlowBuildPair {
    param([Parameter(Mandatory)] [string] $ReceiptPath, [switch] $AllowPending)
    if ($ReceiptPath -match '\.incomplete$' -or (-not $AllowPending -and $ReceiptPath -match '\.pending$')) {
        throw 'Build origin: incomplete or pending output is not a completed receipt.'
    }
    $receipt = Get-Content -LiteralPath $ReceiptPath -Raw -ErrorAction Stop | ConvertFrom-Json
    if ($receipt.Version -ne 1 -or [string]::IsNullOrWhiteSpace($receipt.CompletedAtUtc) -or
        $receipt.InputDigest -notmatch '^[0-9a-fA-F]{64}$' -or
        [string]::IsNullOrWhiteSpace($receipt.SourceRevision) -or
        [string]::IsNullOrWhiteSpace($receipt.SdkVersion) -or
        [string]::IsNullOrWhiteSpace($receipt.DotnetPath) -or
        @($receipt.Inputs).Count -eq 0 -or @($receipt.HarnessCommand).Count -eq 0 -or
        @($receipt.CandidateCommand).Count -eq 0) { throw 'Build origin: receipt is not a completed supported build pair.' }
    $parsedId = [guid]::Empty
    if (-not [guid]::TryParse($receipt.PairId, [ref] $parsedId)) { throw 'Build origin: invalid pair identity.' }
    foreach ($folder in @($receipt.HarnessFolder, $receipt.CandidateFolder)) {
        if (-not [IO.Path]::IsPathRooted($folder) -or -not (Test-Path -LiteralPath $folder -PathType Container)) {
            throw 'Build origin: a concrete output folder is unavailable.'
        }
    }
    $names = @(Get-PrintFlowProductAssemblyNames)
    Assert-PrintFlowPairInventory $receipt.HarnessProductAssemblies `
        @(Read-PrintFlowPairFiles $receipt.HarnessFolder $names) $names 'harness Product'
    Assert-PrintFlowPairInventory $receipt.CandidateProductAssemblies `
        @(Read-PrintFlowPairFiles $receipt.CandidateFolder $names) $names 'candidate Product'
    $runtimeNames = @($receipt.HarnessArtifacts | ForEach-Object { [string] $_.Name })
    foreach ($required in @('PrintFlow.Tests.dll', 'PrintFlow.Tests.deps.json', 'PrintFlow.Tests.runtimeconfig.json', 'testhost.dll') + $names) {
        if ($runtimeNames -notcontains $required) { throw "Build origin: harness inventory lacks $required." }
    }
    $root = [IO.Path]::GetFullPath($receipt.HarnessFolder).TrimEnd('\', '/')
    $actualNames = @(Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
        $_.FullName.Substring($root.Length + 1).Replace('\', '/')
    })
    if (@(Compare-Object $runtimeNames $actualNames).Count -ne 0) { throw 'Build origin: harness runtime file set changed.' }
    Assert-PrintFlowPairInventory $receipt.HarnessArtifacts `
        @(Read-PrintFlowPairFiles $root $runtimeNames) $runtimeNames 'harness runtime'
    return $receipt
}

function Assert-PrintFlowRunBuildOrigin {
    param($Binding)
    $origin = $Binding.BuildOrigin
    if ($null -eq $origin -or $origin.ReceiptSha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw 'Build origin: run is unbound; a new receipt cannot enrich historical evidence.'
    }
    if ((Get-FileHash -LiteralPath $origin.ReceiptPath -Algorithm SHA256).Hash -ne $origin.ReceiptSha256) {
        throw 'Build origin: the receipt recorded by the run is missing or substituted.'
    }
    $receipt = Read-PrintFlowBuildPair -ReceiptPath $origin.ReceiptPath
    if ($receipt.PairId -ne $origin.PairId) { throw 'Build origin: the run names another pair.' }
    $names = @(Get-PrintFlowProductAssemblyNames)
    Assert-PrintFlowPairInventory $receipt.HarnessProductAssemblies $Binding.HarnessProductAssemblies $names 'recorded harness'
    Assert-PrintFlowPairInventory $receipt.CandidateProductAssemblies $Binding.CandidateProductAssemblies $names 'recorded candidate'
}
