#Requires -Version 7.5
# Focused regression tests for the raw response -> snapshot -> CSV label/field fidelity defect found in the
# SCRUM-11145 export. Writes only inside its own GUID-named temporary folder.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RawPath,
    [Parameter(Mandatory)][string] $FaultySnapshotPath,
    [Parameter(Mandatory)][string] $RawReadAt,
    [string] $OriginalTransformerPath,
    [string] $EvidenceDir
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("pf-jira-integrity-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $work | Out-Null
$builder = Join-Path $PSScriptRoot 'Build-JiraReadback.ps1'
$exporter = Join-Path $PSScriptRoot 'Export-Wave1Jira.ps1'
$oracle = Join-Path $PSScriptRoot 'Test-JiraSnapshotFidelity.ps1'
$ledger = Join-Path $PSScriptRoot 'JIRA_WRITE_LEDGER.json'
$pwsh = (Get-Process -Id $PID).Path
$results = [System.Collections.Generic.List[object]]::new()

function Record([string] $name, [bool] $passed, [string] $detail) {
    $results.Add([pscustomobject]@{ Case = $name; Result = if ($passed) { 'PASS' } else { 'FAIL' }; Detail = $detail })
}
function Invoke-Oracle([string] $raw, [string] $snapshot, [string] $csv) {
    $arguments = @('-NoProfile', '-File', $oracle, '-RawPath', $raw, '-SnapshotPath', $snapshot)
    if ($csv) { $arguments += @('-CsvPath', $csv) }
    $text = & $pwsh @arguments 2>&1 | Out-String
    [pscustomobject]@{ Exit = $LASTEXITCODE; Text = $text.Trim() }
}
function Invoke-Exporter([string] $snapshot, [string] $csv) {
    $text = & $pwsh -NoProfile -Command "& '$exporter' -SnapshotPath '$snapshot' -OutputPath '$csv' -LedgerPath '$ledger' | Out-String -Width 200" 2>&1 | Out-String
    [pscustomobject]@{ Exit = $LASTEXITCODE; Text = $text.Trim(); Created = (Test-Path -LiteralPath $csv) }
}
function Build([string] $raw, [string] $out, [string] $readAt, [string] $task, [string] $markerKey, [string] $marker,
    [string] $phase = 'TEST', [string] $source = 'Integrity test on a synthetic fixture') {
    & $pwsh -NoProfile -File $builder -RawPath $raw -OutputPath $out -ReadAt $readAt -Task $task -Phase $phase `
        -Source $source -RawEvidence $raw -MarkerIssueKey $markerKey -Marker $marker 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Builder failed for $raw" }
}

try {
    # 1. Clearly synthetic fixture: exact label strings and other JSON shapes survive the projection.
    $synthetic = @'
{"issues":[
 {"expand":"x","id":"90001","self":"https://example.invalid/1","key":"SYN-1","fields":{
   "summary":"Synthetic label fixture","labels":["operator-ux","pf-opux-v1","printflow"],
   "single":["only"],"numbers":[1,2.5,-3],"withNull":["a",null],"empty":[],"flag":false,"none":null,
   "updated":"2026-09-24T15:24:54.578+1200",
   "issuetype":{"self":"https://example.invalid/t","iconUrl":"https://example.invalid/i","avatarId":10307,"name":"Task","hierarchyLevel":0},
   "description":"Line one\r\nLine two — 中文",
   "comment":{"startAt":0,"total":1,"comments":[{"id":"1","body":"SYN-MARKER\nbody","created":"2026-09-24T15:24:54.578+1200","author":{"accountId":"a","avatarUrls":{"16x16":"u"}}}]}}},
 {"id":"90002","key":"SYN-2","fields":{"summary":"Second","labels":["solo"],"updated":"2026-09-22T14:27:19.196+1200","comment":{"startAt":0,"total":0,"comments":[]}}}
],"isLast":true}
'@
    $synRaw = Join-Path $work 'synthetic-raw.json'
    [System.IO.File]::WriteAllText($synRaw, $synthetic, [System.Text.UTF8Encoding]::new($false))
    $synSnap = Join-Path $work 'synthetic-snapshot.json'
    Build $synRaw $synSnap '2026-09-24T03:25:06.124Z' 'TEST' 'SYN-1' 'SYN-MARKER' | Out-Null
    $doc = [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($synSnap))
    try {
        $f = $doc.RootElement.GetProperty('issues')[0].GetProperty('fields')
        $labels = @($f.GetProperty('labels').EnumerateArray())
        $kinds = ($labels | ForEach-Object { [string]$_.ValueKind }) -join ','
        $values = ($labels | ForEach-Object { $_.GetString() }) -join ','
        $single = $f.GetProperty('single')
        $items = { param($name) '[' + ((@($f.GetProperty($name).EnumerateArray()) | ForEach-Object { $_.GetRawText() }) -join ',') + ']' }
        $shapes = "single=$($single.ValueKind)/$($single.GetArrayLength()) numbers=$(& $items 'numbers') withNull=$(& $items 'withNull') empty=$(& $items 'empty')"
        Record 'synthetic: three labels stay exact JSON strings' ($kinds -ceq 'String,String,String' -and $values -ceq 'operator-ux,pf-opux-v1,printflow') "kinds=$kinds values=$values"
        Record 'synthetic: scalar/number/null/empty/single arrays preserved' ($shapes -ceq 'single=Array/1 numbers=[1,2.5,-3] withNull=["a",null] empty=[]') $shapes
    }
    finally { $doc.Dispose() }
    $o = Invoke-Oracle $synRaw $synSnap $null
    Record 'synthetic: independent oracle PASS (only transport fields dropped)' ($o.Exit -eq 0) $o.Text

    if ($OriginalTransformerPath) {
        $origSnap = Join-Path $work 'synthetic-original-transformer.json'
        & $pwsh -NoProfile -File $OriginalTransformerPath -RawPath $synRaw -OutputPath $origSnap -ReadAt 'x' 2>&1 | Out-Null
        $o = Invoke-Oracle $synRaw $origSnap $null
        Record 'red control: original transformer output is rejected by the oracle' ($o.Exit -ne 0 -and $o.Text -match 'labels\[0\] kind raw=String snapshot=Object') (($o.Text -split "`n" | Select-Object -First 2) -join ' | ')
    }

    # 2. Preserved authenticated raw response -> corrected projection -> exporter -> oracle (reconstruction of the original readAt).
    $recSnap = Join-Path $work 'reconstructed-readback.json'
    $recCsv = Join-Path $work 'reconstructed-final.csv'
    Build $RawPath $recSnap $RawReadAt 'PF-OPUX-v1-SCRUM-11145-ui-v1' 'SCRUM-11145' 'PF-OPUX-v1-SCRUM-11145-ui-v1' `
        "RECONSTRUCTION_OF_READBACK_$RawReadAt" `
        "RECONSTRUCTION, NOT A FRESH READ: corrected projection of the preserved authenticated raw response read at $RawReadAt (closeout PF-OPUX-v1-SCRUM-11145-closeout-v1). The exporter's Readback_Status column describes that original read only." | Out-Null
    $e = Invoke-Exporter $recSnap $recCsv
    Record 'raw: exporter PASS on corrected projection' ($e.Exit -eq 0 -and $e.Text -match 'Validation\s*:\s*PASS') (($e.Text -split "`n" | Where-Object { $_ -match 'Validation|Rows|Timestamps|HeaderColumns' }) -join ' ')
    $o = Invoke-Oracle $RawPath $recSnap $recCsv
    Record 'raw: oracle PASS raw->snapshot->CSV (labels, IDs, AC, links, 23 columns, BOM, timestamps)' ($o.Exit -eq 0) $o.Text

    # 3. Rejections: the supplied Length-object snapshot and a mixed string/object label array.
    $e = Invoke-Exporter $FaultySnapshotPath (Join-Path $work 'faulty.csv')
    Record 'reject: exporter refuses supplied Length-object snapshot' ($e.Exit -ne 0 -and -not $e.Created -and $e.Text -match 'label element is not a string') (($e.Text -split "`n" | Where-Object { $_ -match 'label' } | Select-Object -First 1))
    $o = Invoke-Oracle $RawPath $FaultySnapshotPath $null
    Record 'reject: oracle FAIL on supplied Length-object snapshot' ($o.Exit -ne 0) ($o.Text -split "`n" | Select-Object -First 1)

    $mixedSnap = Join-Path $work 'mixed-snapshot.json'
    $text = [System.IO.File]::ReadAllText($recSnap)
    $mixed = [regex]::new('("labels": \[\s*"[^"]+",\s*)"([^"]+)"').Replace($text, { param($m) $m.Groups[1].Value + '{"Length": ' + $m.Groups[2].Value.Length + '}' }, 1)
    if ($mixed -ceq $text) { throw 'Mixed-array mutation did not apply.' }
    [System.IO.File]::WriteAllText($mixedSnap, $mixed, [System.Text.UTF8Encoding]::new($false))
    $e = Invoke-Exporter $mixedSnap (Join-Path $work 'mixed.csv')
    Record 'reject: exporter refuses mixed string/object labels' ($e.Exit -ne 0 -and -not $e.Created -and $e.Text -match 'label element is not a string') (($e.Text -split "`n" | Where-Object { $_ -match 'label' } | Select-Object -First 1))
    $o = Invoke-Oracle $RawPath $mixedSnap $null
    Record 'reject: oracle FAIL on mixed string/object labels' ($o.Exit -ne 0 -and $o.Text -match 'snapshot=Object') (($o.Text -split "`n" | Select-Object -First 2) -join ' | ')

    # 4. CSV-level negative controls against the raw response (independent of the exporter's own checks).
    $csvText = [System.IO.File]::ReadAllText($recCsv, [System.Text.Encoding]::UTF8)
    $bom = [System.Text.UTF8Encoding]::new($true)
    $cases = [ordered]@{
        'reject: oracle FAIL on CSV Labels cell @{Length=..}' = { param($t) [regex]::new('"([a-z0-9-]+;[a-z0-9-]+;[a-z0-9-]+)"').Replace($t, '"@{Length=10};@{Length=11};@{Length=9}"', 1) }
        'reject: oracle FAIL on normalised timestamp (fraction dropped)' = { param($t) [regex]::new('(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)\.\d{3}(\+\d{4})').Replace($t, '$1$2', 1) }
    }
    foreach ($name in $cases.Keys) {
        $changed = & $cases[$name] $csvText
        if ($changed -ceq $csvText) { throw "Mutation did not apply: $name" }
        $path = Join-Path $work ("tamper-" + [guid]::NewGuid() + '.csv')
        [System.IO.File]::WriteAllText($path, $changed, $bom)
        $o = Invoke-Oracle $RawPath $recSnap $path
        Record $name ($o.Exit -ne 0) ($o.Text -split "`n" | Select-Object -Skip 1 -First 1)
    }
    # Field-level controls: each single changed value must be caught against the raw response.
    $parsed = @(Import-Csv -LiteralPath $recCsv -Encoding utf8)
    $withDependency = [array]::FindIndex($parsed, [Predicate[object]] { param($r) $r.Verified_Dependency_Issue_Keys.Length -gt 0 })
    if ($withDependency -lt 0) { throw 'No row with a dependency to tamper.' }
    $fieldCases = [ordered]@{
        'reject: oracle FAIL on a changed Issue_ID' = @(0, 'Issue_ID', { param($v) '0' + $v })
        'reject: oracle FAIL on a changed Issue_Key' = @(0, 'Issue_Key', { param($v) $v + '0' })
        'reject: oracle FAIL on a changed Description' = @(1, 'Description', { param($v) $v + ' ' })
        'reject: oracle FAIL on Acceptance_Criteria not taken from the description' = @(1, 'Acceptance_Criteria', { param($v) 'x' + $v })
        'reject: oracle FAIL on a dropped dependency' = @($withDependency, 'Verified_Dependency_Issue_Keys', { param($v) (@($v -split ';') | Select-Object -SkipLast 1) -join ';' })
    }
    foreach ($name in $fieldCases.Keys) {
        ($index, $column, $change) = $fieldCases[$name]
        $copy = @(Import-Csv -LiteralPath $recCsv -Encoding utf8)
        $copy[$index].$column = & $change $copy[$index].$column
        $path = Join-Path $work ("field-" + [guid]::NewGuid() + '.csv')
        $copy | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding utf8BOM
        $o = Invoke-Oracle $RawPath $recSnap $path
        Record $name ($o.Exit -ne 0) ($o.Text -split "`n" | Select-Object -Skip 1 -First 1)
    }
    $unchanged = Join-Path $work 'field-unchanged.csv'
    $parsed | Export-Csv -LiteralPath $unchanged -NoTypeInformation -Encoding utf8BOM
    $o = Invoke-Oracle $RawPath $recSnap $unchanged
    Record 'control: re-serialised unchanged CSV still PASSes (the field controls test values, not formatting)' ($o.Exit -eq 0) $o.Text

    $noBom = Join-Path $work 'tamper-nobom.csv'
    [System.IO.File]::WriteAllText($noBom, $csvText, [System.Text.UTF8Encoding]::new($false))
    $o = Invoke-Oracle $RawPath $recSnap $noBom
    Record 'reject: oracle FAIL without UTF-8 BOM' ($o.Exit -ne 0 -and $o.Text -match 'BOM') ($o.Text -split "`n" | Select-Object -Skip 1 -First 1)

    if ($EvidenceDir) {
        Copy-Item -LiteralPath $recSnap -Destination (Join-Path $EvidenceDir 'reconstructed-20260924T032506124Z-readback.json')
        Copy-Item -LiteralPath $recCsv -Destination (Join-Path $EvidenceDir 'reconstructed-20260924T032506124Z-final.csv')
    }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force
}

$results | Format-Table -AutoSize -Wrap | Out-String -Width 220
$failed = @($results | Where-Object Result -eq 'FAIL').Count
"Cases=$($results.Count) Passed=$($results.Count - $failed) Failed=$failed"
if ($failed) { exit 1 }
