#Requires -Version 7.5
# Independent fidelity oracle: saved authenticated raw response -> readback snapshot -> exported CSV.
# Parses JSON with System.Text.Json (not ConvertFrom-Json), so the snapshot is never its own reference.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RawPath,
    [Parameter(Mandatory)][string] $SnapshotPath,
    [string] $CsvPath,
    [string] $ResultPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$transportFields = @('self', 'iconUrl', 'avatarUrls', 'expand', 'avatarId')
$expectedHeaders = @('Planning_ID', 'Issue_ID', 'Issue_Key', 'Issue_URL', 'Issue_Type', 'Summary', 'Description', 'Acceptance_Criteria',
    'Priority', 'Status', 'Parent_Issue_ID', 'Parent_Issue_Key', 'Labels', 'Planned_Dependency_Planning_IDs', 'Verified_Dependency_Issue_IDs',
    'Verified_Dependency_Issue_Keys', 'Dependency_Link_Status', 'Recommended_Order', 'Write_Action', 'Readback_Status', 'Jira_Updated_At',
    'Readback_At', 'Notes')
$failures = [System.Collections.Generic.List[string]]::new()
function Fail([string] $message) { $failures.Add($message) }

function Compare-Element($raw, $snap, [string] $path) {
    if ($raw.ValueKind -ne $snap.ValueKind) { Fail "$path kind raw=$($raw.ValueKind) snapshot=$($snap.ValueKind)"; return }
    switch ($raw.ValueKind) {
        'Object' {
            $rawNames = @($raw.EnumerateObject() | Where-Object Name -NotIn $transportFields | ForEach-Object Name)
            $snapNames = @($snap.EnumerateObject() | ForEach-Object Name)
            foreach ($n in $snapNames) { if ($n -cnotin $rawNames) { Fail "$path.$n absent from raw" } }
            foreach ($n in $rawNames) {
                $s = [System.Text.Json.JsonElement]::new()
                if (-not $snap.TryGetProperty($n, [ref]$s)) { Fail "$path.$n missing from snapshot"; continue }
                Compare-Element $raw.GetProperty($n) $s "$path.$n"
            }
        }
        'Array' {
            $r = @($raw.EnumerateArray()); $s = @($snap.EnumerateArray())
            if ($r.Count -ne $s.Count) { Fail "$path length raw=$($r.Count) snapshot=$($s.Count)"; return }
            for ($i = 0; $i -lt $r.Count; $i++) { Compare-Element $r[$i] $s[$i] "$path[$i]" }
        }
        'String' { if ($raw.GetString() -cne $snap.GetString()) { Fail "$path string differs" } }
        default { if ($raw.GetRawText() -cne $snap.GetRawText()) { Fail "$path value raw=$($raw.GetRawText()) snapshot=$($snap.GetRawText())" } }
    }
}

function Get-LabelEvidence($issue, [string] $side) {
    $labels = $issue.GetProperty('fields').GetProperty('labels')
    $types = [System.Collections.Generic.List[string]]::new(); $values = [System.Collections.Generic.List[string]]::new()
    foreach ($l in $labels.EnumerateArray()) {
        $types.Add([string]$l.ValueKind)
        if ($l.ValueKind -eq 'String') { $values.Add($l.GetString()) } else { $values.Add($l.GetRawText()) }
    }
    [pscustomobject]@{ types = $types.ToArray(); values = $values.ToArray() }
}

$rawDoc = [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($RawPath))
$snapDoc = [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($SnapshotPath))
try {
    $rawRoot = $rawDoc.RootElement; $snapRoot = $snapDoc.RootElement
    if (-not $rawRoot.GetProperty('isLast').GetBoolean()) { Fail 'raw response is not the last page' }
    $rawIssues = @($rawRoot.GetProperty('issues').EnumerateArray())
    $snapIssues = @($snapRoot.GetProperty('issues').EnumerateArray())
    $readAt = $snapRoot.GetProperty('readAt').GetString()
    if ($rawIssues.Count -ne $snapIssues.Count) { Fail "issue count raw=$($rawIssues.Count) snapshot=$($snapIssues.Count)" }

    $labelEvidence = [System.Collections.Generic.List[object]]::new()
    $rawById = @{}
    for ($i = 0; $i -lt [Math]::Min($rawIssues.Count, $snapIssues.Count); $i++) {
        $r = $rawIssues[$i]; $s = $snapIssues[$i]
        $key = $r.GetProperty('key').GetString()
        $rawById[$r.GetProperty('id').GetString()] = $r
        Compare-Element $r $s "issues[$i]($key)"
        $rl = Get-LabelEvidence $r 'raw'; $sl = Get-LabelEvidence $s 'snapshot'
        if (@($rl.types | Where-Object { $_ -ne 'String' }).Count) { Fail "$key raw labels contain non-string elements" }
        if (@($sl.types | Where-Object { $_ -ne 'String' }).Count) { Fail "$key snapshot labels contain non-string elements: $($sl.types -join ',')" }
        if (($rl.values -join "`n") -cne ($sl.values -join "`n")) { Fail "$key snapshot label values differ from raw" }
        $labelEvidence.Add([ordered]@{ issueId = $r.GetProperty('id').GetString(); key = $key
            raw = [ordered]@{ types = $rl.types; values = $rl.values }; snapshot = [ordered]@{ types = $sl.types; values = $sl.values }; csv = $null })
    }

    $csvSummary = $null
    if ($CsvPath) {
        $bytes = [System.IO.File]::ReadAllBytes($CsvPath)
        $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        if (-not $bom) { Fail 'CSV is not UTF-8 with BOM' }
        $firstLine = ([System.Text.Encoding]::UTF8.GetString($bytes) -split "`r?`n", 2)[0].TrimStart([char]0xFEFF)
        $quotedHeader = ($expectedHeaders | ForEach-Object { '"' + $_ + '"' }) -join ','
        if ($firstLine -cne $quotedHeader) { Fail 'CSV header line is not the exact 23-column schema' }
        $rows = @(Import-Csv -LiteralPath $CsvPath -Encoding utf8)
        if ($rows.Count -ne $rawIssues.Count) { Fail "CSV rows=$($rows.Count) raw issues=$($rawIssues.Count)" }
        $site = $snapRoot.GetProperty('site').GetString().TrimEnd('/')
        $timestamps = 0
        foreach ($row in $rows) {
            $r = $rawById[$row.Issue_ID]
            if ($null -eq $r) { Fail "CSV Issue_ID $($row.Issue_ID) not in raw"; continue }
            $f = $r.GetProperty('fields'); $key = $r.GetProperty('key').GetString()
            if ($row.Issue_Key -cne $key) { Fail "$($row.Issue_ID) key CSV=$($row.Issue_Key) raw=$key" }
            if ($row.Issue_URL -cne "$site/browse/$key") { Fail "$key Issue_URL differs" }
            if ($row.Issue_Type -cne $f.GetProperty('issuetype').GetProperty('name').GetString()) { Fail "$key Issue_Type differs" }
            if ($row.Summary -cne $f.GetProperty('summary').GetString()) { Fail "$key Summary differs" }
            $description = $f.GetProperty('description').GetString()
            if ($row.Description -cne $description) { Fail "$key Description differs from raw" }
            if ([string]::IsNullOrWhiteSpace($row.Acceptance_Criteria) -or -not $description.Contains($row.Acceptance_Criteria, [StringComparison]::Ordinal)) { Fail "$key Acceptance_Criteria is not an exact excerpt of the raw description" }
            if ($row.Priority -cne $f.GetProperty('priority').GetProperty('name').GetString()) { Fail "$key Priority differs" }
            if ($row.Status -cne $f.GetProperty('status').GetProperty('name').GetString()) { Fail "$key Status differs" }
            $p = [System.Text.Json.JsonElement]::new()
            $hasParent = $f.TryGetProperty('parent', [ref]$p) -and $p.ValueKind -eq 'Object'
            if ($row.Parent_Issue_ID -cne $(if ($hasParent) { $p.GetProperty('id').GetString() } else { '' })) { Fail "$key Parent_Issue_ID differs" }
            if ($row.Parent_Issue_Key -cne $(if ($hasParent) { $p.GetProperty('key').GetString() } else { '' })) { Fail "$key Parent_Issue_Key differs" }
            $blockers = @($f.GetProperty('issuelinks').EnumerateArray() | ForEach-Object {
                $in = [System.Text.Json.JsonElement]::new()
                if ($_.GetProperty('type').GetProperty('name').GetString() -ceq 'Blocks' -and $_.TryGetProperty('inwardIssue', [ref]$in)) {
                    [pscustomobject]@{ id = $in.GetProperty('id').GetString(); key = $in.GetProperty('key').GetString() }
                } })
            $csvIds = @($row.Verified_Dependency_Issue_IDs -split ';' | Where-Object { $_ }) | Sort-Object -CaseSensitive
            $csvKeys = @($row.Verified_Dependency_Issue_Keys -split ';' | Where-Object { $_ }) | Sort-Object -CaseSensitive
            if ((@($csvIds) -join ';') -cne (@($blockers | ForEach-Object id | Sort-Object -CaseSensitive) -join ';')) { Fail "$key dependency IDs differ from raw inward Blocks" }
            if ((@($csvKeys) -join ';') -cne (@($blockers | ForEach-Object key | Sort-Object -CaseSensitive) -join ';')) { Fail "$key dependency keys differ from raw inward Blocks" }
            if ($row.Jira_Updated_At -cne $f.GetProperty('updated').GetString()) { Fail "$key Jira_Updated_At '$($row.Jira_Updated_At)' is not the raw string" } else { $timestamps++ }
            if ($row.Readback_At -cne $readAt) { Fail "$key Readback_At differs from snapshot readAt" } else { $timestamps++ }

            # Labels: each raw element must be a JSON string without the ';' delimiter; the cell holds exactly those strings.
            $rawLabels = @($f.GetProperty('labels').EnumerateArray())
            if (@($rawLabels | Where-Object ValueKind -ne 'String').Count) { Fail "$key raw labels contain non-string elements" }
            $rawValues = @($rawLabels | Where-Object ValueKind -eq 'String' | ForEach-Object { $_.GetString() })
            if (@($rawValues | Where-Object { $_.Contains(';') -or [string]::IsNullOrWhiteSpace($_) }).Count) { Fail "$key raw label cannot be represented unambiguously" }
            $cellValues = @($row.Labels -split ';')
            if (((@($cellValues | Sort-Object -CaseSensitive)) -join "`n") -cne ((@($rawValues | Sort-Object -CaseSensitive)) -join "`n")) { Fail "$key CSV Labels '$($row.Labels)' differ from raw labels" }
            $entry = $labelEvidence | Where-Object { $_.key -ceq $key } | Select-Object -First 1
            if ($entry) { $entry.csv = $row.Labels }
        }
        foreach ($column in @('Issue_ID', 'Issue_Key', 'Planning_ID')) {
            if (@($rows.$column | Sort-Object -Unique -CaseSensitive).Count -ne $rows.Count) { Fail "CSV $column values are not unique" }
        }
        $csvSummary = [ordered]@{ path = $CsvPath; sha256 = (Get-FileHash -LiteralPath $CsvPath -Algorithm SHA256).Hash; utf8Bom = $bom
            headerExact23 = ($firstLine -ceq $quotedHeader); rows = $rows.Count
            uniqueIds = @($rows.Issue_ID | Sort-Object -Unique).Count; uniqueKeys = @($rows.Issue_Key | Sort-Object -Unique).Count
            exactRawTimestampStrings = $timestamps; readbackStatuses = @($rows.Readback_Status | Sort-Object -Unique) }
    }

    $result = [ordered]@{
        verdict = if ($failures.Count) { 'FAIL' } else { 'PASS' }
        rawPath = $RawPath; rawSha256 = (Get-FileHash -LiteralPath $RawPath -Algorithm SHA256).Hash
        snapshotPath = $SnapshotPath; snapshotSha256 = (Get-FileHash -LiteralPath $SnapshotPath -Algorithm SHA256).Hash
        snapshotReadAt = $readAt
        issues = $rawIssues.Count
        labelElements = [ordered]@{ raw = @($labelEvidence | ForEach-Object { $_.raw.types }).Count; snapshotString = @($labelEvidence | ForEach-Object { $_.snapshot.types } | Where-Object { $_ -eq 'String' }).Count }
        csv = $csvSummary
        failureCount = $failures.Count
        failures = @($failures | Select-Object -First 200)
        labels = $labelEvidence.ToArray()
    }
    if ($ResultPath) { $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ResultPath -Encoding utf8 }
    "Verdict=$($result.verdict) issues=$($result.issues) rawLabelElements=$($result.labelElements.raw) snapshotStringLabels=$($result.labelElements.snapshotString) failures=$($failures.Count)"
    if ($failures.Count) { $failures | Select-Object -First 8 | ForEach-Object { "  FAIL: $_" }; exit 1 }
}
finally { $rawDoc.Dispose(); $snapDoc.Dispose() }
