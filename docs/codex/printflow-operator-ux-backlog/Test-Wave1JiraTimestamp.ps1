[CmdletBinding()]
param(
    [string] $SnapshotPath = (Join-Path $PSScriptRoot 'WAVE1_JIRA_READBACK.json'),
    [string] $ExporterPath = (Join-Path $PSScriptRoot 'Export-Wave1Jira.ps1'),
    [Parameter(Mandatory)][string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Independent, string-only JSON oracle: never compare two date-coerced values.
$json = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($SnapshotPath))
try {
    $source = $json.RootElement
    $expectedReadAt = $source.GetProperty('readAt').GetString()
    $expectedByKey = @{}
    foreach ($issue in $source.GetProperty('issues').EnumerateArray()) {
        $expectedByKey[$issue.GetProperty('key').GetString()] = $issue.GetProperty('fields').GetProperty('updated').GetString()
    }
    & $ExporterPath -SnapshotPath $SnapshotPath -OutputPath $OutputPath -LedgerPath (Join-Path $PSScriptRoot 'JIRA_WRITE_LEDGER.json')
    $rows = @(Import-Csv -LiteralPath $OutputPath -Encoding utf8)
    $failures = [Collections.Generic.List[string]]::new()
    foreach ($row in $rows) {
        if ($row.Jira_Updated_At -cne $expectedByKey[$row.Issue_Key]) {
            $failures.Add("$($row.Issue_Key) Jira_Updated_At expected '$($expectedByKey[$row.Issue_Key])', actual '$($row.Jira_Updated_At)'")
        }
        if ($row.Readback_At -cne $expectedReadAt) {
            $failures.Add("$($row.Issue_Key) Readback_At expected '$expectedReadAt', actual '$($row.Readback_At)'")
        }
    }
    if ($rows.Count -ne $expectedByKey.Count) { throw 'CSV/source issue count differs.' }
    if ($failures.Count) { throw "Timestamp fidelity FAIL ($($failures.Count) values):`n$($failures -join "`n")" }
    $example = $rows | Where-Object Issue_Key -CEQ 'SCRUM-11143'
    Write-Output "PASS: $($rows.Count * 2) timestamp strings exactly equal raw JSON; offsets and fractional seconds preserved."
    Write-Output "SCRUM-11143: $($example.Jira_Updated_At); readAt: $($example.Readback_At)"
}
finally { $json.Dispose() }
