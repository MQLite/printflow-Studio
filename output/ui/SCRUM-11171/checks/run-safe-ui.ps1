param([string]$RunName = 'e4-final', [string]$Include = '(FullyQualifiedName~Integration.Ui|FullyQualifiedName~Architecture)')
$ErrorActionPreference = 'Stop'
$excluded = Get-Content (Join-Path $PSScriptRoot 'safe-ui-exclusions.json') -Raw | ConvertFrom-Json
$filter = '(' + $Include + ')&FullyQualifiedName!~PrintFlow.Tests.Smoke.' + (($excluded | ForEach-Object { '&FullyQualifiedName!~' + $_ }) -join '')
$filter | Set-Content (Join-Path $PSScriptRoot "$RunName-filter.txt") -Encoding utf8
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --disable-build-servers --filter $filter --results-directory $PSScriptRoot --logger ("trx;LogFileName=$RunName.trx") > (Join-Path $PSScriptRoot "$RunName.log") 2>&1
$testExitCode = $LASTEXITCODE
Get-Content (Join-Path $PSScriptRoot "$RunName.log") -Tail 10
exit $testExitCode

