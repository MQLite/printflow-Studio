[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [switch]$SafeDesktopConfirmed
)
$ErrorActionPreference = 'Stop'
if (-not $ValidateOnly -and -not $SafeDesktopConfirmed) {
    throw 'No window opened. At an idle, operator-controlled non-production desktop, pass -SafeDesktopConfirmed. For nonvisible validation pass -ValidateOnly.'
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$saved = @{}
foreach ($name in @('DOTNET_ROOT', 'PATH', 'PF_OPUX_SAFE_DESKTOP', 'PF_OPUX_HOST_LOG', 'PF_OPUX_CAPTURE_DIR')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
Push-Location -LiteralPath $repo
try {
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    $env:PF_OPUX_SAFE_DESKTOP = $null
    $env:PF_OPUX_HOST_LOG = $null
    $env:PF_OPUX_CAPTURE_DIR = $null
    $sdk = (& $dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.0\.[4-9][0-9]{2}$') {
        throw "Installed SDK does not satisfy the repo's 10.0.400/latestFeature policy: $sdk. No toolchain installation performed."
    }
    $run = Join-Path $repo ('artifacts/pf-opux-wave1a/ui-run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
    New-Item -ItemType Directory -Path $run | Out-Null
    $filter = 'FullyQualifiedName=PrintFlow.Tests.Integration.Ui.OperatorWave1AHostTests.Nonvisible_seed_and_real_command_persistence_smoke'
    if (-not $ValidateOnly) {
        $env:PF_OPUX_SAFE_DESKTOP = 'operator-confirmed'
        $env:PF_OPUX_HOST_LOG = Join-Path $run 'host.jsonl'
        $filter = 'FullyQualifiedName=PrintFlow.Tests.Integration.Ui.OperatorWave1AHostTests.Operator_launched_visible_synthetic_host'
    }
    [ordered]@{
        at = [DateTimeOffset]::UtcNow.ToString('o'); mode = $(if ($ValidateOnly) { 'NONVISIBLE' } else { 'OPERATOR_INTERACTIVE' })
        safeDesktopDeclaredByOperator = [bool]$SafeDesktopConfirmed; dotnet = $dotnet; sdk = $sdk
        filter = $filter; productionStartup = $false; globalInputInjection = $false
        composition = 'HomeScreenHarness -> SessionServiceHarness; GUID temp workspace/SQLite/settings/lease; fake Meitu/Photoshop; recorded navigation; no session file picker; no startup recovery/instance lease'
        inputAcceptance = 'NOT RUN unless recorded separately by operator; passing host test only means it closed without an unhandled exception'
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding utf8
    Write-Host "Evidence directory: $run"
    & $dotnet test 'tests/PrintFlow.Tests/PrintFlow.Tests.csproj' --no-restore --filter $filter --logger 'trx;LogFileName=host.trx' --results-directory $run 2>&1 | Tee-Object -FilePath (Join-Path $run 'test.log')
    if ($LASTEXITCODE -ne 0) { throw "Host validation/execution failed (exit $LASTEXITCODE). Retain $run" }
    Write-Host 'Host path completed. This is not physical-input, visual, novice, or Wave 1 acceptance.'
}
finally {
    Pop-Location
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
