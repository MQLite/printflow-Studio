param([string]$Current = 'review-ui-architecture-final2.trx')
$ErrorActionPreference = 'Stop'
function Counters($run) { $c=$run.TestRun.ResultSummary.Counters; [ordered]@{ Total=[int]$c.total; Passed=[int]$c.passed; Failed=[int]$c.failed; NotExecuted=[int]$c.notExecuted; Executed=[int]$c.executed } }
$baselinePath = Join-Path $PSScriptRoot '../../SCRUM-11169/checks/review-ui-architecture-final.trx'
[xml]$baseline = Get-Content $baselinePath -Raw
[xml]$currentRun = Get-Content (Join-Path $PSScriptRoot $Current) -Raw
$baselineResults = @{}
foreach ($r in $baseline.TestRun.Results.UnitTestResult) { $baselineResults[$r.testName] = $r }
$rows = @(foreach ($r in $currentRun.TestRun.Results.UnitTestResult | Where-Object outcome -ne Passed) {
    $before = $baselineResults[$r.testName]
    [pscustomobject][ordered]@{ Name=$r.testName; Outcome=$r.outcome; BaselineOutcome=$before.outcome; InheritedFailureName=($before.outcome -eq 'Failed'); ExactMessageMatches=([string]$r.Output.ErrorInfo.Message -ceq [string]$before.Output.ErrorInfo.Message); CurrentMessage=[string]$r.Output.ErrorInfo.Message; BaselineMessage=[string]$before.Output.ErrorInfo.Message; CurrentStack=[string]$r.Output.ErrorInfo.StackTrace; BaselineStack=[string]$before.Output.ErrorInfo.StackTrace }
})
$currentFailedNames = @($rows | ForEach-Object { $_.Name })
[ordered]@{ Baseline='output/ui/SCRUM-11169/checks/review-ui-architecture-final.trx'; Current=$Current; BaselineCounters=(Counters $baseline); CurrentCounters=(Counters $currentRun); Nonpass=$rows; NewFailureNames=@($rows|Where-Object {-not $_.InheritedFailureName}|ForEach-Object {$_.Name}); BaselineFailuresNotReproduced=@($baseline.TestRun.Results.UnitTestResult|Where-Object {$_.outcome -eq 'Failed' -and $_.testName -notin $currentFailedNames}|ForEach-Object {$_.testName}) } | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $PSScriptRoot 'baseline-comparison.json') -Encoding utf8
$index = @(foreach ($f in Get-ChildItem $PSScriptRoot -Filter *.trx) { [xml]$x=Get-Content $f.FullName -Raw; [ordered]@{ File=$f.Name; Counters=(Counters $x); Start=$x.TestRun.Times.start; Finish=$x.TestRun.Times.finish } })
$index | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $PSScriptRoot 'run-index.json') -Encoding utf8
$rows | Select-Object Name,InheritedFailureName,ExactMessageMatches | Format-Table -AutoSize


