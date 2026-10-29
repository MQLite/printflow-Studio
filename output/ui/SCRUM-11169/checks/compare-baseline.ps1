param([string]$CurrentFile = 'e2-ui-architecture-final.trx', [string]$OutputFile = 'baseline-comparison.json')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$e1Root = Join-Path (Split-Path $taskRoot -Parent) 'SCRUM-11177/checks'
[xml]$baseline = Get-Content (Join-Path $e1Root 'scrum-11177-ui-fix.trx') -Raw
[xml]$current = Get-Content (Join-Path $PSScriptRoot $CurrentFile) -Raw
$old = @{}
foreach ($item in $baseline.TestRun.Results.UnitTestResult) { $old[$item.testName] = $item }
$rows = @(
    foreach ($item in $current.TestRun.Results.UnitTestResult) {
        if ($item.outcome -eq 'Passed') { continue }
        $prior = $old[$item.testName]
        [ordered]@{
            Name = $item.testName
            Outcome = $item.outcome
            BaselineOutcome = if ($prior) { $prior.outcome } else { 'Absent' }
            InheritedFailureName = $item.outcome -eq 'Failed' -and $prior.outcome -eq 'Failed'
            ExactMessageMatches = [string]$item.Output.ErrorInfo.Message -ceq [string]$prior.Output.ErrorInfo.Message
            CurrentMessage = [string]$item.Output.ErrorInfo.Message
            BaselineMessage = [string]$prior.Output.ErrorInfo.Message
            CurrentStack = [string]$item.Output.ErrorInfo.StackTrace
            BaselineStack = [string]$prior.Output.ErrorInfo.StackTrace
        }
    }
)
$unreproduced = @(
    foreach ($item in $baseline.TestRun.Results.UnitTestResult) {
        if ($item.outcome -eq 'Failed' -and -not ($rows | Where-Object Name -eq $item.testName)) { $item.testName }
    }
)
[ordered]@{
    Baseline = 'SCRUM-11177/checks/scrum-11177-ui-fix.trx'
    Current = 'SCRUM-11169/checks/' + $CurrentFile
    BaselineCounters = $baseline.TestRun.ResultSummary.Counters
    CurrentCounters = $current.TestRun.ResultSummary.Counters
    Nonpass = $rows
    BaselineFailuresNotReproduced = $unreproduced
    NewFailureNames = @($rows | Where-Object { $_.Outcome -eq 'Failed' -and -not $_.InheritedFailureName } | ForEach-Object Name)
} | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $PSScriptRoot $OutputFile) -Encoding utf8
$rows | Select-Object Name, Outcome, BaselineOutcome, ExactMessageMatches | Format-Table -Wrap
$current.TestRun.ResultSummary.Counters

