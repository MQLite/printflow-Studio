param([string]$RunName = 'e5-ui-architecture-final')
$ErrorActionPreference = 'Stop'
$baseline = Get-Content (Join-Path $PSScriptRoot 'controller-inherited-failures.json') -Raw | ConvertFrom-Json
[xml]$run = Get-Content (Join-Path $PSScriptRoot "$RunName.trx") -Raw
$failures = @($run.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Failed' | ForEach-Object {
    [ordered]@{ name = $_.testName; message = [string]$_.Output.ErrorInfo.Message }
})
$exact = @()
$changed = @()
$new = @()
foreach ($failure in $failures) {
    $previous = @($baseline.failures | Where-Object { $_.name -ceq $failure.name })
    if ($previous.Count -eq 0) { $new += $failure; continue }
    if ($previous.Count -ne 1) { throw "Ambiguous baseline: $($failure.name)" }
    if ($previous[0].message -ceq $failure.message) { $exact += $failure.name }
    else { $changed += [ordered]@{ name = $failure.name; baselineMessage = $previous[0].message; currentMessage = $failure.message } }
}
$notReproduced = @($baseline.failures | Where-Object { $name = $_.name; -not @($failures | Where-Object { $_.name -ceq $name }).Count } | ForEach-Object { $_.name })
$counters = $run.TestRun.ResultSummary.Counters
[ordered]@{
    source = $baseline.source
    run = "$RunName.trx"
    counters = [ordered]@{ total = [int]$counters.total; passed = [int]$counters.passed; failed = [int]$counters.failed; notExecuted = [int]$counters.notExecuted }
    exactInherited = $exact
    changedMessages = $changed
    newFailures = $new
    baselineNotReproduced = $notReproduced
    interpretation = 'Only identical names AND messages are classified exact inherited. Non-reproduction is not a causal fix.'
} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $PSScriptRoot "$RunName-comparison.json") -Encoding utf8
[pscustomobject]@{ Total = $counters.total; Passed = $counters.passed; Failed = $counters.failed; ExactInherited = $exact.Count; Changed = $changed.Count; New = $new.Count; NotReproduced = $notReproduced.Count }
