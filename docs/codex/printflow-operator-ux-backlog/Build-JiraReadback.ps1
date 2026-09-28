#Requires -Version 7.5
# Builds an initiative readback snapshot from a saved authenticated searchJiraIssuesUsingJql response.
# Supersedes the SCRUM-11145 scratch transformer, whose `-is [pscustomobject]` test matched
# pipeline-wrapped label strings ([pscustomobject] is the PSObject accelerator) and rebuilt each label
# as {"Length": n}. See artifacts/pf-opux-scrum11145-closeout/label-loss-trace.txt.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RawPath,
    [Parameter(Mandatory)][string] $OutputPath,
    [Parameter(Mandatory)][string] $ReadAt,
    [Parameter(Mandatory)][string] $Task,
    [Parameter(Mandatory)][string] $Phase,
    [Parameter(Mandatory)][string] $Source,
    [Parameter(Mandatory)][string] $RawEvidence,
    [Parameter(Mandatory)][string] $MarkerIssueKey,
    [Parameter(Mandatory)][string] $Marker,
    [string] $Jql = 'project = SCRUM AND labels = pf-opux-v1 ORDER BY key ASC',
    [string] $CloudId = '6ea5df13-e1e2-4df3-bb4b-691a3fb8626c',
    [string] $Site = 'https://yituoxx.atlassian.net'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw "Output exists: $OutputPath" }
$raw = Get-Content -LiteralPath $RawPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100 -DateKind String
if (-not $raw.isLast) { throw 'Raw readback is not the last page.' }

# Transport noise only; every other value, scalar array, null, id, relationship and raw timestamp string is kept.
$drop = @('self', 'iconUrl', 'avatarUrls', 'expand', 'avatarId')
function Remove-TransportField($node) {
    # Exact runtime types, and no pipeline: a pipeline would wrap scalars in PSObject.
    if ($node -is [System.Management.Automation.PSCustomObject]) {
        $copy = [ordered]@{}
        foreach ($p in $node.PSObject.Properties) {
            if ($p.Name -in $drop) { continue }
            $copy[$p.Name] = Remove-TransportField $p.Value
        }
        return [pscustomobject]$copy
    }
    if ($node -is [object[]]) {
        $items = [System.Collections.Generic.List[object]]::new()
        foreach ($item in $node) { $items.Add((Remove-TransportField $item)) }
        return , $items.ToArray()
    }
    return $node
}

$issues = [System.Collections.Generic.List[object]]::new()
foreach ($issue in $raw.issues) { $issues.Add((Remove-TransportField $issue)) }
$pagination = foreach ($issue in $raw.issues) {
    $c = $issue.fields.comment
    [ordered]@{ issueId = $issue.id; issueKey = $issue.key; startAt = $c.startAt; total = $c.total
        returned = @($c.comments).Count; complete = (@($c.comments).Count -eq $c.total -and $c.startAt -eq 0) }
}
$target = @($raw.issues | Where-Object key -CEQ $MarkerIssueKey)
if ($target.Count -ne 1) { throw "Marker issue $MarkerIssueKey found $($target.Count) times." }
$pattern = '(?m)^' + [regex]::Escape($Marker) + '\s*$'
$markerComments = @($target[0].fields.comment.comments | Where-Object { $_.body -cmatch $pattern })

$snapshot = [ordered]@{
    initiative = 'PF-OPUX-v1'
    task = $Task
    phase = $Phase
    cloudId = $CloudId
    site = $Site
    readAt = $ReadAt
    source = $Source
    jql = $Jql
    pages = @([ordered]@{ readAt = $ReadAt; count = $issues.Count; isLast = $true; appliedContentFormat = 'markdown' })
    commentReadback = [ordered]@{
        issueId = $target[0].id; issueKey = $target[0].key; commentId = if ($markerComments.Count) { $markerComments[0].id } else { $null }
        marker = $Marker; complete = $true; markerCount = $markerComments.Count
        created = if ($markerComments.Count) { $markerComments[0].created } else { $null }
        rawEvidence = $RawEvidence
    }
    commentPagination = @($pagination)
    isLast = $true
    issues = $issues.ToArray()
}
$json = $snapshot | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($OutputPath, $json + "`n", [System.Text.UTF8Encoding]::new($false))
"issues=$($issues.Count) markerCount=$($markerComments.Count) commentId=$(if ($markerComments.Count) { $markerComments[0].id })"
