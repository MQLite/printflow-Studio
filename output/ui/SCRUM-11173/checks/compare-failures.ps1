param(
    [string]$Reference = 'output/ui/SCRUM-11172/checks/e5-ui-architecture-final.trx',
    [Parameter(Mandatory)][string]$Current,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
function Read-Run([string]$Path) {
    [xml]$xml = Get-Content -LiteralPath $Path -Raw
    $failures = @($xml.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Failed' | ForEach-Object {
        [ordered]@{ Name = $_.testName; Message = [string]$_.Output.ErrorInfo.Message }
    } | Sort-Object { $_.Name })
    return [pscustomobject]@{ Path=$Path; Sha256=(Get-FileHash -LiteralPath $Path).Hash; Counters=$xml.TestRun.ResultSummary.Counters; Failures=$failures }
}
$before = Read-Run $Reference
$after = Read-Run $Current
$new = @($after.Failures | Where-Object { $_.Name -notin $before.Failures.Name })
$missing = @($before.Failures | Where-Object { $_.Name -notin $after.Failures.Name })
$changed = @($after.Failures | Where-Object {
    $name=$_.Name; $old=@($before.Failures | Where-Object Name -CEQ $name)
    $old.Count -eq 1 -and $old[0].Message -cne $_.Message
})
[ordered]@{ Reference=$before; Current=$after; New=$new; Missing=$missing; ChangedMessages=$changed;
    ExactMatch=($new.Count -eq 0 -and $missing.Count -eq 0 -and $changed.Count -eq 0)
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output "Reference failures=$($before.Failures.Count); current=$($after.Failures.Count); new=$($new.Count); missing=$($missing.Count); changed=$($changed.Count)"
if ($new.Count -or $missing.Count -or $changed.Count) { exit 1 }
