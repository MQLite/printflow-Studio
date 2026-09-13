$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$producer = (Resolve-Path (Join-Path $here '..\New-PrintFlowBuildPair.ps1')).Path
$sharedContract = (Resolve-Path (Join-Path $here '..\PrintFlowBuildPair.ps1')).Path

function New-SyntheticProducerLayout {
    param([ValidateSet('CandidateFailure', 'InputDrift')][string] $Mode)

    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('printflow-build-pair-main-' + [guid]::NewGuid().ToString('N'))
    foreach ($directory in @(
        'tools\regression', 'tools\installer', 'build\installer',
        'src\PrintFlow.App', 'src\PrintFlow.Infrastructure\Verification',
        'tests\PrintFlow.Tests', 'artifacts\build-pairs'
    )) { New-Item -ItemType Directory -Path (Join-Path $root $directory) -Force | Out-Null }

    Copy-Item -LiteralPath $producer -Destination (Join-Path $root 'tools\regression\New-PrintFlowBuildPair.ps1')
    Copy-Item -LiteralPath $sharedContract -Destination (Join-Path $root 'tools\regression\PrintFlowBuildPair.ps1')
    'root = true' | Set-Content -LiteralPath (Join-Path $root '.editorconfig') -Encoding utf8
    '<Project><Import Project="$(MSBuildThisFileDirectory)Version.props" /></Project>' |
        Set-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Encoding utf8
    '<Project />' | Set-Content -LiteralPath (Join-Path $root 'Directory.Packages.props') -Encoding utf8
    '<Project />' | Set-Content -LiteralPath (Join-Path $root 'Version.props') -Encoding utf8
    '{}' | Set-Content -LiteralPath (Join-Path $root 'global.json') -Encoding utf8
    '<configuration />' | Set-Content -LiteralPath (Join-Path $root 'nuget.config') -Encoding utf8
    '{}' | Set-Content -LiteralPath (Join-Path $root 'appsettings.json') -Encoding utf8
    '<Project />' | Set-Content -LiteralPath (Join-Path $root 'src\PrintFlow.App\PrintFlow.App.csproj') -Encoding utf8
    '<Project><ItemGroup><EmbeddedResource Update="Strings.resx" /></ItemGroup></Project>' |
        Set-Content -LiteralPath (Join-Path $root 'tests\PrintFlow.Tests\PrintFlow.Tests.csproj') -Encoding utf8
    New-Item -ItemType Directory -Path (Join-Path $root 'tests\PrintFlow.Tests\TestResults') | Out-Null
    'synthetic historical log, not a compile input' |
        Set-Content -LiteralPath (Join-Path $root 'tests\PrintFlow.Tests\TestResults\old.trx') -Encoding utf8
    'internal sealed class OriginInput { }' |
        Set-Content -LiteralPath (Join-Path $root 'src\PrintFlow.App\OriginInput.cs') -Encoding utf8
    @'
internal static class ProductBuildIdentity {
    private static readonly string[] Names = {
        "PrintFlow.App.dll", "PrintFlow.Domain.dll",
        "PrintFlow.Infrastructure.dll", "PrintFlow.Workflow.dll"
    };
}
'@ | Set-Content -LiteralPath (Join-Path $root 'src\PrintFlow.Infrastructure\Verification\ProductBuildIdentity.cs') -Encoding utf8
    foreach ($file in @(
        'tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1',
        'tools\installer\Set-PrintFlowProductionRevalidation.ps1',
        'build\installer\Build-Installer.ps1'
    )) { '# synthetic tracked tool' | Set-Content -LiteralPath (Join-Path $root $file) -Encoding utf8 }

    $tracked = @(
        '.editorconfig', 'Directory.Build.props', 'Directory.Packages.props', 'Version.props',
        'global.json', 'nuget.config', 'appsettings.json',
        'tools/regression/New-PrintFlowBuildPair.ps1',
        'tools/regression/PrintFlowBuildPair.ps1',
        'tools/regression/Invoke-PrintFlowStandardRegressionSet.ps1',
        'tools/installer/Set-PrintFlowProductionRevalidation.ps1',
        'build/installer/Build-Installer.ps1',
        'src/PrintFlow.App/PrintFlow.App.csproj', 'src/PrintFlow.App/OriginInput.cs',
        'src/PrintFlow.Infrastructure/Verification/ProductBuildIdentity.cs',
        'tests/PrintFlow.Tests/PrintFlow.Tests.csproj'
    )
    $dotnet = Join-Path $root 'synthetic-dotnet.ps1'
    @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Cli)
if ($Cli[0] -eq '--version') { '10.0.400'; exit 0 }
if ($Cli[0] -eq 'build') {
    'synthetic harness build'
    if ($env:PRINTFLOW_BUILD_PAIR_TEST_MODE -eq 'InputDrift') {
        Add-Content -LiteralPath $env:PRINTFLOW_BUILD_PAIR_TEST_DRIFT_PATH -Value '// changed during build'
    }
    exit 0
}
if ($Cli[0] -eq 'publish') { 'synthetic candidate failure'; exit 37 }
throw "Unexpected synthetic dotnet command: $($Cli -join ' ')"
'@ | Set-Content -LiteralPath $dotnet -Encoding utf8

    return [pscustomobject] @{ Root = $root; Tracked = $tracked; DotNet = $dotnet; Mode = $Mode }
}

function Set-SyntheticGit {
    param($Layout)
    $global:PrintFlowBuildPairSyntheticRoot = $Layout.Root
    $global:PrintFlowBuildPairSyntheticTracked = $Layout.Tracked
    function global:git {
        param(
            [string] $C,
            [Parameter(ValueFromRemainingArguments = $true)] [string[]] $Remaining
        )
        $global:LASTEXITCODE = 0
        if ($Remaining[0] -eq 'rev-parse' -and $Remaining[1] -eq '--show-toplevel') {
            return $global:PrintFlowBuildPairSyntheticRoot
        }
        if ($Remaining[0] -eq 'rev-parse' -and $Remaining[1] -eq 'HEAD') {
            return '1111111111111111111111111111111111111111'
        }
        if ($Remaining[0] -eq 'ls-files') { return $global:PrintFlowBuildPairSyntheticTracked }
        if ($Remaining[0] -eq 'diff' -or $Remaining[0] -eq 'check-ignore') { return }
        throw "Unexpected synthetic git command: $($Remaining -join ' ')"
    }
}

function Remove-SyntheticGit {
    Remove-Item Function:\git -ErrorAction SilentlyContinue
    Remove-Variable PrintFlowBuildPairSyntheticRoot -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable PrintFlowBuildPairSyntheticTracked -Scope Global -ErrorAction SilentlyContinue
}

function Remove-SyntheticLayout {
    param([Parameter(Mandatory)][string] $Root)
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { return }
    $resolved = (Resolve-Path -LiteralPath $Root).Path
    $temporaryPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\printflow-build-pair-'
    if (-not $resolved.StartsWith($temporaryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing synthetic cleanup outside '$temporaryPrefix': '$resolved'."
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

Describe 'New-PrintFlowBuildPair' {
    It 'supports receipt verification without invoking a build' {
        $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('printflow-build-pair-test-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $temporaryRoot 'tools\regression') -Force | Out-Null
        try {
            $copiedProducer = Join-Path $temporaryRoot 'tools\regression\New-PrintFlowBuildPair.ps1'
            Copy-Item -LiteralPath $producer -Destination $copiedProducer
            @'
function Read-PrintFlowBuildPair {
    param([string] $ReceiptPath)
    if ($ReceiptPath -ne 'synthetic-receipt.json') { throw "Unexpected receipt: $ReceiptPath" }
    return [pscustomobject] @{ PairId = 'c54e64f6-480b-4b44-b2da-04ee4fbb0a74' }
}
'@ | Set-Content -LiteralPath (Join-Path $temporaryRoot 'tools\regression\PrintFlowBuildPair.ps1') -Encoding utf8

            $result = & $copiedProducer -VerifyOnly -ReceiptPath 'synthetic-receipt.json'

            $result.PairId | Should Be 'c54e64f6-480b-4b44-b2da-04ee4fbb0a74'
        } finally {
            Remove-SyntheticLayout -Root $temporaryRoot
        }
    }

    It 'preserves failure evidence and emits no completed receipt when the candidate build fails' {
        $layout = New-SyntheticProducerLayout -Mode CandidateFailure
        Set-SyntheticGit $layout
        $env:PRINTFLOW_BUILD_PAIR_TEST_MODE = $layout.Mode
        $env:PRINTFLOW_BUILD_PAIR_TEST_DRIFT_PATH = Join-Path $layout.Root 'src\PrintFlow.App\OriginInput.cs'
        try {
            $thrown = $null
            try {
                $null = & (Join-Path $layout.Root 'tools\regression\New-PrintFlowBuildPair.ps1') `
                    -OutputRoot (Join-Path $layout.Root 'artifacts\build-pairs') -DotNetPath $layout.DotNet
            } catch { $thrown = $_ }

            $thrown | Should Not BeNullOrEmpty
            $thrown.Exception.Message | Should Match 'Candidate publish failed with exit code 37'
            $pair = @(Get-ChildItem -LiteralPath (Join-Path $layout.Root 'artifacts\build-pairs') -Directory)
            $pair.Count | Should Be 1
            Test-Path -LiteralPath (Join-Path $pair[0].FullName 'build-pair.json.incomplete') | Should Be $true
            Test-Path -LiteralPath (Join-Path $pair[0].FullName 'build-pair.json') | Should Be $false
            Test-Path -LiteralPath (Join-Path $pair[0].FullName 'harness-build.log') | Should Be $true
            Test-Path -LiteralPath (Join-Path $pair[0].FullName 'candidate-build.log') | Should Be $true
        } finally {
            $env:PRINTFLOW_BUILD_PAIR_TEST_MODE = $null
            $env:PRINTFLOW_BUILD_PAIR_TEST_DRIFT_PATH = $null
            Remove-SyntheticGit
            Remove-SyntheticLayout -Root $layout.Root
        }
    }

    It 'stops between builds when a relevant input drifts and emits no completed receipt' {
        $layout = New-SyntheticProducerLayout -Mode InputDrift
        Set-SyntheticGit $layout
        $env:PRINTFLOW_BUILD_PAIR_TEST_MODE = $layout.Mode
        $env:PRINTFLOW_BUILD_PAIR_TEST_DRIFT_PATH = Join-Path $layout.Root 'src\PrintFlow.App\OriginInput.cs'
        try {
            $thrown = $null
            try {
                $null = & (Join-Path $layout.Root 'tools\regression\New-PrintFlowBuildPair.ps1') `
                    -OutputRoot (Join-Path $layout.Root 'artifacts\build-pairs') -DotNetPath $layout.DotNet
            } catch { $thrown = $_ }

            $thrown | Should Not BeNullOrEmpty
            $thrown.Exception.Message | Should Match 'changed between the harness and candidate builds'
            $pair = @(Get-ChildItem -LiteralPath (Join-Path $layout.Root 'artifacts\build-pairs') -Directory)
            $pair.Count | Should Be 1
            Test-Path -LiteralPath (Join-Path $pair[0].FullName 'build-pair.json.incomplete') | Should Be $true
            Test-Path -LiteralPath (Join-Path $pair[0].FullName 'build-pair.json') | Should Be $false
            Test-Path -LiteralPath (Join-Path $pair[0].FullName 'candidate-build.log') | Should Be $false
        } finally {
            $env:PRINTFLOW_BUILD_PAIR_TEST_MODE = $null
            $env:PRINTFLOW_BUILD_PAIR_TEST_DRIFT_PATH = $null
            Remove-SyntheticGit
            Remove-SyntheticLayout -Root $layout.Root
        }
    }

    It 'rejects an untracked compile input in a nested bin-named source folder before building' {
        $layout = New-SyntheticProducerLayout -Mode CandidateFailure
        Set-SyntheticGit $layout
        $nestedInput = Join-Path $layout.Root 'src\PrintFlow.App\Feature\bin\Origin.cs'
        New-Item -ItemType Directory -Path (Split-Path -Parent $nestedInput) -Force | Out-Null
        'internal sealed class HiddenOriginInput { }' | Set-Content -LiteralPath $nestedInput -Encoding utf8
        try {
            $thrown = $null
            try {
                $null = & (Join-Path $layout.Root 'tools\regression\New-PrintFlowBuildPair.ps1') `
                    -OutputRoot (Join-Path $layout.Root 'artifacts\build-pairs') -DotNetPath $layout.DotNet
            } catch { $thrown = $_ }

            $thrown | Should Not BeNullOrEmpty
            $thrown.Exception.Message | Should Match 'Feature/bin/Origin.cs'
            $pair = @(Get-ChildItem -LiteralPath (Join-Path $layout.Root 'artifacts\build-pairs') -Directory)
            $pair.Count | Should Be 1
            Test-Path -LiteralPath (Join-Path $pair[0].FullName 'build-pair.json.incomplete') | Should Be $true
            Test-Path -LiteralPath (Join-Path $pair[0].FullName 'build-pair.json') | Should Be $false
            Test-Path -LiteralPath (Join-Path $pair[0].FullName 'harness-build.log') | Should Be $false
        } finally {
            Remove-SyntheticGit
            Remove-SyntheticLayout -Root $layout.Root
        }
    }
}
