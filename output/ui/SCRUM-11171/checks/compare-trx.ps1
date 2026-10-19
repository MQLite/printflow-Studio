param([Parameter(Mandatory=$true)][string]$RunName)
$ErrorActionPreference = 'Stop'
$baselinePath = Join-Path $PSScriptRoot '../../SCRUM-11170/checks/review-ui-architecture-final2.trx'
$currentPath = Join-Path $PSScriptRoot ($RunName + '.trx')
[xml]$baseline = Get-Content -LiteralPath $baselinePath
[xml]$current = Get-Content -LiteralPath $currentPath
function Failures($xml) {
    $rows = @{}
    foreach ($result in $xml.SelectNodes("//*[local-name()='UnitTestResult' and @outcome='Failed']")) {
        $message = $result.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
        $rows[$result.GetAttribute('testName')] = $message.InnerText
    }
    return $rows
}
function Counts($xml) {
    $counts = $xml.SelectSingleNode("//*[local-name()='Counters']")
    return @{ Total=[int]$counts.GetAttribute('total'); Passed=[int]$counts.GetAttribute('passed'); Failed=[int]$counts.GetAttribute('failed'); NotExecuted=[int]$counts.GetAttribute('notExecuted') }
}
$old = Failures $baseline
$new = Failures $current
$result = [ordered]@{
    Baseline='SCRUM-11170/checks/review-ui-architecture-final2.trx'
    BaselineSha256=(Get-FileHash -LiteralPath $baselinePath -Algorithm SHA256).Hash
    Current=$RunName+'.trx'
    CurrentSha256=(Get-FileHash -LiteralPath $currentPath -Algorithm SHA256).Hash
    BaselineCounters=(Counts $baseline)
    CurrentCounters=(Counts $current)
    InheritedExact=@($new.Keys | Where-Object { $old.ContainsKey($_) -and $new[$_] -ceq $old[$_] } | Sort-Object)
    ChangedMessages=@($new.Keys | Where-Object { $old.ContainsKey($_) -and $new[$_] -cne $old[$_] } | Sort-Object | ForEach-Object { @{ Name=$_; Baseline=$old[$_]; Current=$new[$_] } })
    NewFailureNames=@($new.Keys | Where-Object { -not $old.ContainsKey($_) } | Sort-Object)
    BaselineFailuresNotReproduced=@($old.Keys | Where-Object { -not $new.ContainsKey($_) } | Sort-Object)
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot ($RunName+'-comparison.json')) -Encoding utf8
@{Counters=$result.CurrentCounters; InheritedExact=$result.InheritedExact.Count; NewFailures=$result.NewFailureNames.Count; Changed=$result.ChangedMessages.Count; Missing=$result.BaselineFailuresNotReproduced.Count} | ConvertTo-Json -Compress
