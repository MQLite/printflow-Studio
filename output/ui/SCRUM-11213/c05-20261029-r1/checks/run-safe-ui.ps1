param([Parameter(Mandatory)][string]$RunName, [string]$Include = 'FullyQualifiedName~WpfUiSharedThemeTests', [switch]$NoBuild, [switch]$ListTests)
$ErrorActionPreference = 'Stop'
$excluded = Get-Content (Join-Path $PSScriptRoot 'safe-ui-exclusions.json') -Raw | ConvertFrom-Json
$filter = '(' + $Include + ')&FullyQualifiedName!~PrintFlow.Tests.Smoke.' + (($excluded | ForEach-Object { '&FullyQualifiedName!~' + $_ }) -join '')
$log = Join-Path $PSScriptRoot "$RunName.log"
if (Test-Path -LiteralPath $log) { throw "Evidence already exists: $log" }
$filter | Set-Content (Join-Path $PSScriptRoot "$RunName-filter.txt") -Encoding utf8
$testBuildOptions = @()
if ($NoBuild) { $testBuildOptions += '--no-build' }
if ($ListTests) { $testBuildOptions += '--list-tests' }
$u09LargeRender = -not $ListTests -and $Include -match 'PrintWorkspaceRolloutTests|ProcessingWorkspaceRolloutTests|RecoveryUtilityRolloutTests|SaveWorkspaceRolloutTests|PlainChineseSaveRecoveryCopyTests|PlainChinesePrintCopyTests|FailureGuidanceLayoutTests'
$u09RenderMutex = $null
if ($u09LargeRender) {
    $u09RenderMutex = [Threading.Mutex]::new($false, 'Local\PrintFlow-U09-LargeRender')
    [void]$u09RenderMutex.WaitOne()
    # Bound rendering concurrency without altering test selection or product behavior.
    while ((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory -lt 5GB / 1KB) {
        Start-Sleep -Seconds 2
    }
}
try {
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-restore --disable-build-servers @testBuildOptions --blame-hang-timeout 120s --blame-hang-dump-type none --filter $filter --results-directory $PSScriptRoot --logger ("trx;LogFileName=$RunName.trx") > $log 2>&1
$testExitCode = $LASTEXITCODE
} finally {
    if ($u09RenderMutex) { $u09RenderMutex.ReleaseMutex(); $u09RenderMutex.Dispose() }
}
Get-Content -LiteralPath $log -Tail 12
exit $testExitCode
