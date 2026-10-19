param([string]$RunName = 'e2-ui-architecture-final')
$ErrorActionPreference = 'Stop'
$excluded = Get-Content (Join-Path $PSScriptRoot 'safe-ui-exclusions.json') -Raw | ConvertFrom-Json
$filter = '(FullyQualifiedName~Integration.Ui|FullyQualifiedName~Architecture)' +
    (($excluded | ForEach-Object { '&FullyQualifiedName!~' + $_ }) -join '')
$filter | Set-Content (Join-Path $PSScriptRoot 'safe-ui-filter.txt') -Encoding utf8
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-restore --filter $filter --results-directory $PSScriptRoot --logger ("trx;LogFileName=$RunName.trx") > (Join-Path $PSScriptRoot "$RunName.log") 2>&1
$testExitCode = $LASTEXITCODE
Get-Content (Join-Path $PSScriptRoot "$RunName.log") -Tail 10
exit $testExitCode

