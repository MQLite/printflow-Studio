$ErrorActionPreference = 'Stop'
$rows = Get-ChildItem $PSScriptRoot -Filter '*.trx' | ForEach-Object {
    [xml]$run = Get-Content $_.FullName -Raw
    $results = @($run.TestRun.Results.UnitTestResult)
    [ordered]@{
        File = $_.Name
        Passed = @($results | Where-Object outcome -eq 'Passed').Count
        Failed = @($results | Where-Object outcome -eq 'Failed').Count
        Skipped = @($results | Where-Object outcome -eq 'NotExecuted').Count
        Total = $results.Count
        Outcome = [string]$run.TestRun.ResultSummary.outcome
    }
}
$rows | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PSScriptRoot 'run-index.json') -Encoding utf8
$rows | Format-Table -AutoSize

