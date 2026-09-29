#Requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter()]
    [string] $SnapshotPath = (Join-Path $PSScriptRoot 'WAVE1_JIRA_READBACK.json'),

    [Parameter()]
    [string] $OutputPath = (Join-Path $PSScriptRoot 'WAVE1_JIRA_FINAL.csv'),

    [Parameter()]
    [string] $LedgerPath = (Join-Path $PSScriptRoot 'JIRA_WRITE_LEDGER.json'),

    [Parameter()]
    [string] $JiraBaseUrl = 'https://yituoxx.atlassian.net'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedHeaders = @(
    'Planning_ID',
    'Issue_ID',
    'Issue_Key',
    'Issue_URL',
    'Issue_Type',
    'Summary',
    'Description',
    'Acceptance_Criteria',
    'Priority',
    'Status',
    'Parent_Issue_ID',
    'Parent_Issue_Key',
    'Labels',
    'Planned_Dependency_Planning_IDs',
    'Verified_Dependency_Issue_IDs',
    'Verified_Dependency_Issue_Keys',
    'Dependency_Link_Status',
    'Recommended_Order',
    'Write_Action',
    'Readback_Status',
    'Jira_Updated_At',
    'Readback_At',
    'Notes'
)

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory)]
        [object] $Object,

        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Context
    )

    $property = $Object.PSObject.Properties[$Name]
    Assert-Condition ($null -ne $property) "$Context is missing property '$Name'."
    return $property.Value
}

function Get-LabelStrings {
    param(
        [Parameter(Mandatory)][object] $Fields,
        [Parameter(Mandatory)][string] $IssueKey
    )

    # Jira labels are JSON strings. A cast to [string] would silently accept a damaged
    # snapshot element such as {"Length": 11} as '@{Length=11}'.
    $labels = [System.Collections.Generic.List[string]]::new()
    foreach ($label in @(Get-RequiredProperty $Fields 'labels' "Issue $IssueKey fields")) {
        Assert-Condition ($label -is [string]) "$IssueKey label element is not a string: $(ConvertTo-Json -InputObject $label -Compress -Depth 3)"
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($label) -and -not $label.Contains(';')) "$IssueKey label '$label' cannot be exported unambiguously."
        $labels.Add($label)
    }
    return @($labels | Sort-Object)
}

function Get-PlanningId {
    param(
        [Parameter(Mandatory)]
        [string] $Description,

        [Parameter(Mandatory)]
        [string] $IssueKey
    )

    $match = [regex]::Match($Description, '(?m)^Planning-ID:[ \t]*(?<id>[^\r\n]+?)[ \t]*$')
    Assert-Condition $match.Success "$IssueKey has no unambiguous Planning-ID line."
    return $match.Groups['id'].Value
}

function Get-AcceptanceCriteria {
    param(
        [Parameter(Mandatory)]
        [string] $Description,

        [Parameter(Mandatory)]
        [bool] $IsEpic,

        [Parameter(Mandatory)]
        [string] $IssueKey
    )

    if ($IsEpic) {
        $startPattern = '(?m)^Completion criteria for this Epic[ \t]*\r?$'
        $endPattern = '(?m)^Evidence status at planning time[ \t]*\r?$'
    }
    else {
        $startPattern = '(?m)^5\\?\. Acceptance criteria[ \t]*\r?$'
        $endPattern = '(?m)^6\\?\. Validation[ \t]*\r?$'
    }

    $startMatch = [regex]::Match($Description, $startPattern)
    $endMatch = if ($startMatch.Success) {
        [regex]::Match($Description, $endPattern, [System.Text.RegularExpressions.RegexOptions]::Multiline, [TimeSpan]::FromSeconds(2))
    }
    else {
        [System.Text.RegularExpressions.Match]::Empty
    }

    Assert-Condition $startMatch.Success "$IssueKey has no acceptance-criteria start heading."
    Assert-Condition $endMatch.Success "$IssueKey has no acceptance-criteria end heading."
    Assert-Condition ($endMatch.Index -gt $startMatch.Index) "$IssueKey has acceptance-criteria headings in the wrong order."

    $contentStart = $startMatch.Index + $startMatch.Length
    if ($contentStart -lt $Description.Length -and $Description[$contentStart] -eq "`r") {
        $contentStart++
    }
    Assert-Condition ($contentStart -lt $Description.Length -and $Description[$contentStart] -eq "`n") "$IssueKey acceptance heading is not followed by a line break."
    $contentStart++

    $contentEnd = $endMatch.Index
    if ($contentEnd -gt $contentStart -and $Description[$contentEnd - 1] -eq "`n") {
        $contentEnd--
        if ($contentEnd -gt $contentStart -and $Description[$contentEnd - 1] -eq "`r") {
            $contentEnd--
        }
    }

    Assert-Condition ($contentEnd -gt $contentStart) "$IssueKey has empty acceptance criteria."
    return $Description.Substring($contentStart, $contentEnd - $contentStart)
}

function Get-WriteAction {
    param(
        [Parameter(Mandatory)][string] $PlanningId,
        [Parameter(Mandatory)][object] $Issue
    )

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11152-impl-v1') {
        # Only SCRUM-11152 is transitioned and commented; every other initiative issue is read-only.
        if ($Issue.key -eq 'SCRUM-11152') {
            $comments = Get-RequiredProperty $Issue.fields 'comment' "Issue $($Issue.key) fields"
            $saved = @(Get-RequiredProperty $comments 'comments' "Issue $($Issue.key) comments")
            Assert-Condition (@($saved | Where-Object { $_.body -cmatch '(?m)^PF-OPUX-v1-SCRUM-11152-impl-v1s*$' }).Count -eq 1) 'Expected exactly one saved SCRUM-11152 implementation evidence marker.'
            return 'STATUS_TRANSITION_AND_COMMENT'
        }
        return 'READ_ONLY'
    }
    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11151-impl-v1') {
        # Only SCRUM-11151 is transitioned and commented; every other initiative issue is read-only.
        if ($Issue.key -eq 'SCRUM-11151') {
            $comments = Get-RequiredProperty $Issue.fields 'comment' "Issue $($Issue.key) fields"
            $saved = @(Get-RequiredProperty $comments 'comments' "Issue $($Issue.key) comments")
            Assert-Condition (@($saved | Where-Object { $_.body -cmatch '(?m)^PF-OPUX-v1-SCRUM-11151-impl-v1\s*$' }).Count -eq 1) 'Expected exactly one saved SCRUM-11151 implementation evidence marker.'
            return 'STATUS_TRANSITION_AND_COMMENT'
        }
        return 'READ_ONLY'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11150-impl-v1') {
        # Only SCRUM-11150 is transitioned and commented; every other initiative issue is read-only.
        if ($Issue.key -eq 'SCRUM-11150') {
            $comments = Get-RequiredProperty $Issue.fields 'comment' "Issue $($Issue.key) fields"
            $saved = @(Get-RequiredProperty $comments 'comments' "Issue $($Issue.key) comments")
            Assert-Condition (@($saved | Where-Object { $_.body -cmatch '(?m)^PF-OPUX-v1-SCRUM-11150-impl-v1\s*$' }).Count -eq 1) 'Expected exactly one saved SCRUM-11150 implementation evidence marker.'
            return 'STATUS_TRANSITION_AND_COMMENT'
        }
        return 'READ_ONLY'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11149-impl-v1') {
        # Only SCRUM-11149 is transitioned and commented; every other initiative issue is read-only.
        if ($Issue.key -eq 'SCRUM-11149') {
            $comments = Get-RequiredProperty $Issue.fields 'comment' "Issue $($Issue.key) fields"
            $saved = @(Get-RequiredProperty $comments 'comments' "Issue $($Issue.key) comments")
            Assert-Condition (@($saved | Where-Object { $_.body -cmatch '(?m)^PF-OPUX-v1-SCRUM-11149-impl-v1\s*$' }).Count -eq 1) 'Expected exactly one saved SCRUM-11149 implementation evidence marker.'
            return 'STATUS_TRANSITION_AND_COMMENT'
        }
        return 'READ_ONLY'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-master-publication-v1') {
        # Status synchronization covers SCRUM-11139 through SCRUM-11148 only; later issues stay read-only.
        if ([int]($Issue.key -replace '^SCRUM-', '') -in 11139..11148) {
            $comments = Get-RequiredProperty $Issue.fields 'comment' "Issue $($Issue.key) fields"
            $saved = @(Get-RequiredProperty $comments 'comments' "Issue $($Issue.key) comments")
            Assert-Condition (@($saved | Where-Object { $_.body -cmatch '(?m)^PF-OPUX-v1-master-publication-v1\s*$' }).Count -eq 1) "Expected exactly one saved $($Issue.key) publication status marker."
            return 'STATUS_TRANSITION_AND_COMMENT'
        }
        return 'READ_ONLY'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11148-impl-v1') {
        if ($Issue.key -eq 'SCRUM-11148') {
            $comments = Get-RequiredProperty $Issue.fields 'comment' "Issue $($Issue.key) fields"
            $saved = @(Get-RequiredProperty $comments 'comments' "Issue $($Issue.key) comments")
            Assert-Condition (@($saved | Where-Object { $_.body -cmatch '(?m)^PF-OPUX-v1-SCRUM-11148-impl-v1\s*$' }).Count -eq 1) 'Expected exactly one saved SCRUM-11148 implementation evidence marker.'
            return 'EVIDENCE_COMMENT_ONLY'
        }
        return 'READ_ONLY'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11147-impl-v1') {
        if ($Issue.key -eq 'SCRUM-11147') {
            $comments = Get-RequiredProperty $Issue.fields 'comment' "Issue $($Issue.key) fields"
            $saved = @(Get-RequiredProperty $comments 'comments' "Issue $($Issue.key) comments")
            Assert-Condition (@($saved | Where-Object { $_.body -cmatch '(?m)^PF-OPUX-v1-SCRUM-11147-impl-v1\s*$' }).Count -eq 1) 'Expected exactly one saved SCRUM-11147 implementation evidence marker.'
            return 'EVIDENCE_COMMENT_ONLY'
        }
        return 'READ_ONLY'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11145-closeout-v1') {
        if ($PlanningId -eq 'PF-OPUX-v1-completion-delivery-summary') {
            $comments = Get-RequiredProperty $Issue.fields 'comment' "Issue $($Issue.key) fields"
            $saved = @(Get-RequiredProperty $comments 'comments' "Issue $($Issue.key) comments")
            Assert-Condition (@($saved | Where-Object { $_.body -cmatch '(?m)^PF-OPUX-v1-SCRUM-11145-closeout-v1\s*$' }).Count -eq 1) 'Expected exactly one saved SCRUM-11145 closeout evidence marker.'
            return 'EVIDENCE_COMMENT_ONLY'
        }
        return 'READ_ONLY'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11145-ui-v1') {
        if ($PlanningId -eq 'PF-OPUX-v1-completion-delivery-summary') {
            $comments = Get-RequiredProperty $Issue.fields 'comment' "Issue $($Issue.key) fields"
            $saved = @(Get-RequiredProperty $comments 'comments' "Issue $($Issue.key) comments")
            Assert-Condition (@($saved | Where-Object { $_.body -cmatch '(?m)^PF-OPUX-v1-SCRUM-11145-ui-v1\s*$' }).Count -eq 1) 'Expected exactly one saved SCRUM-11145 UI evidence marker.'
            return 'EVIDENCE_COMMENT_ONLY'
        }
        return 'READ_ONLY'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11144-backend-v1') {
        if ($PlanningId -eq 'PF-OPUX-v1-approved-artifact-export') {
            $comments = Get-RequiredProperty $Issue.fields 'comment' "Issue $($Issue.key) fields"
            $saved = @(Get-RequiredProperty $comments 'comments' "Issue $($Issue.key) comments")
            Assert-Condition (@($saved | Where-Object { $_.body -cmatch '(?m)^PF-OPUX-v1-SCRUM-11144-backend-v1\s*$' }).Count -eq 1) 'Expected exactly one saved SCRUM-11144 backend evidence marker.'
            return 'EVIDENCE_COMMENT_ONLY'
        }
        return 'READ_ONLY'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11146-dev-v1') {
        if ($Issue.key -eq 'SCRUM-11146') {
            $comments = Get-RequiredProperty $Issue.fields 'comment' "Issue $($Issue.key) fields"
            $saved = @(Get-RequiredProperty $comments 'comments' "Issue $($Issue.key) comments")
            Assert-Condition (@($saved | Where-Object { $_.body -cmatch '(?m)^PF-OPUX-v1-SCRUM-11146-dev-v1\s*$' }).Count -eq 1) 'Expected exactly one saved SCRUM-11146 evidence marker.'
            return 'EVIDENCE_COMMENT_ONLY'
        }
        return 'READ_ONLY'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-wave1a') {
        # This follow-up permits only evidence comments, never historical amendments.
        if ($Issue.key -in @('SCRUM-11142', 'SCRUM-11143')) {
            $comments = Get-RequiredProperty $Issue.fields 'comment' "Issue $($Issue.key) fields"
            $saved = @(Get-RequiredProperty $comments 'comments' "Issue $($Issue.key) comments")
            if (@($saved | Where-Object { $_.body -cmatch 'PF-OPUX-v1-wave1a' }).Count -gt 0) {
                return 'EVIDENCE_COMMENT_ONLY'
            }
        }
        return 'READ_ONLY'
    }

    switch ($PlanningId) {
        'PF-OPUX-v1-glossary-decision' { return 'EVIDENCE_COMMENT_ONLY' }
        'PF-OPUX-v1-novice-walkthrough' { return 'EVIDENCE_COMMENT_ONLY' }
        'PF-OPUX-v1-app-specific-takeover-wording' { return 'EVIDENCE_COMMENT_ONLY' }
        'PF-OPUX-v1-session-next-step-panel' { return 'DESCRIPTION_AMENDED_AND_EVIDENCE_COMMENT' }
        'PF-OPUX-v1-completion-delivery-summary' { return 'DESCRIPTION_AMENDED' }
        'PF-OPUX-v1-consistency-keyboard-pass' { return 'DESCRIPTION_AMENDED' }
        default { return 'READ_ONLY' }
    }
}

function Get-WriteActionNote {
    param([Parameter(Mandatory)][string] $WriteAction)

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11152-impl-v1') {
        if ($WriteAction -eq 'STATUS_TRANSITION_AND_COMMENT') {
            return 'SCRUM-11152 evidence-based status transitions and implementation evidence comment verified in authenticated current readback; no other issue-field mutation authorized.'
        }
        return 'SCRUM-11152 implementation readback only; no Jira write to this issue in this run.'
    }
    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11151-impl-v1') {
        if ($WriteAction -eq 'STATUS_TRANSITION_AND_COMMENT') {
            return 'SCRUM-11151 evidence-based status transitions and implementation evidence comment verified in authenticated current readback; no other issue-field mutation authorized.'
        }
        return 'SCRUM-11151 implementation readback only; no Jira write to this issue in this run.'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11150-impl-v1') {
        if ($WriteAction -eq 'STATUS_TRANSITION_AND_COMMENT') {
            return 'SCRUM-11150 evidence-based status transitions and implementation evidence comment verified in authenticated current readback; no other issue-field mutation authorized.'
        }
        return 'SCRUM-11150 implementation readback only; no Jira write to this issue in this run.'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11149-impl-v1') {
        if ($WriteAction -eq 'STATUS_TRANSITION_AND_COMMENT') {
            return 'SCRUM-11149 evidence-based status transitions and implementation evidence comment verified in authenticated current readback; no other issue-field mutation authorized.'
        }
        return 'SCRUM-11149 implementation readback only; no Jira write to this issue in this run.'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-master-publication-v1') {
        if ($WriteAction -eq 'STATUS_TRANSITION_AND_COMMENT') {
            return 'Evidence-based status transition and publication status comment verified in authenticated current readback; no other issue-field mutation authorized.'
        }
        return 'Master publication readback only; SCRUM-11149 to SCRUM-11155 are read-only in this run.'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11148-impl-v1') {
        if ($WriteAction -eq 'EVIDENCE_COMMENT_ONLY') {
            return 'SCRUM-11148 implementation evidence comment verified in authenticated current readback; no issue-field mutation authorized.'
        }
        return 'SCRUM-11148 implementation readback only; no Jira write to this issue in this run.'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11147-impl-v1') {
        if ($WriteAction -eq 'EVIDENCE_COMMENT_ONLY') {
            return 'SCRUM-11147 implementation evidence comment verified in authenticated current readback; no issue-field mutation authorized.'
        }
        return 'SCRUM-11147 implementation readback only; no Jira write to this issue in this run.'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11145-closeout-v1') {
        if ($WriteAction -eq 'EVIDENCE_COMMENT_ONLY') {
            return 'SCRUM-11145 closeout evidence comment verified in authenticated current readback; no issue-field mutation authorized.'
        }
        return 'SCRUM-11145 closeout readback only; no Jira write to this issue in this run.'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11145-ui-v1') {
        if ($WriteAction -eq 'EVIDENCE_COMMENT_ONLY') {
            return 'SCRUM-11145 UI evidence comment verified in authenticated current readback; no issue-field mutation authorized.'
        }
        return 'SCRUM-11145 UI readback only; no Jira write to this issue in this run.'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11144-backend-v1') {
        if ($WriteAction -eq 'EVIDENCE_COMMENT_ONLY') {
            return 'SCRUM-11144 backend evidence comment verified in authenticated current readback; no issue-field mutation authorized.'
        }
        return 'SCRUM-11144 backend readback only; no Jira write to this issue in this run.'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-SCRUM-11146-dev-v1') {
        if ($WriteAction -eq 'EVIDENCE_COMMENT_ONLY') {
            return 'SCRUM-11146 evidence comment verified in authenticated current readback; no issue-field mutation authorized.'
        }
        return 'SCRUM-11146 follow-up readback only; no Jira write to this issue in this run.'
    }

    if ($snapshotTask -eq 'PF-OPUX-v1-wave1a') {
        if ($WriteAction -eq 'EVIDENCE_COMMENT_ONLY') {
            return 'Wave 1A evidence comment verified in authenticated current readback; no issue-field mutation authorized.'
        }
        return 'Wave 1A readback only; no Jira write in this follow-up.'
    }

    switch ($WriteAction) {
        'EVIDENCE_COMMENT_ONLY' { return 'Wave 1 write scope: evidence comment only; issue status is the current Jira value.' }
        'DESCRIPTION_AMENDED_AND_EVIDENCE_COMMENT' { return 'Wave 1 write scope: description amendment and evidence comment; issue status is the current Jira value.' }
        'DESCRIPTION_AMENDED' { return 'Wave 1 write scope: description amendment; issue status is the current Jira value.' }
        default { return 'Wave 1 readback only; no Jira write recorded for this issue.' }
    }
}

$snapshotFullPath = [System.IO.Path]::GetFullPath($SnapshotPath)
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$ledgerFullPath = [System.IO.Path]::GetFullPath($LedgerPath)
$defaultFinalOutput = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'WAVE1_JIRA_FINAL.csv'))
$defaultFinalSnapshot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'WAVE1_JIRA_READBACK.json'))

Assert-Condition (Test-Path -LiteralPath $snapshotFullPath -PathType Leaf) "Snapshot not found: $snapshotFullPath"
Assert-Condition (Test-Path -LiteralPath $ledgerFullPath -PathType Leaf) "Planning ledger not found: $ledgerFullPath"
if ($outputFullPath -eq $defaultFinalOutput) {
    Assert-Condition ($snapshotFullPath -eq $defaultFinalSnapshot) 'The final CSV path may only be generated from WAVE1_JIRA_READBACK.json. Use an explicit temporary output path when validating another snapshot.'
}
Assert-Condition ($snapshotFullPath -ne $outputFullPath) 'Snapshot and output paths must differ.'
Assert-Condition (-not (Test-Path -LiteralPath $outputFullPath)) 'Output already exists; choose a new path to preserve historical exports.'

$snapshot = Get-Content -LiteralPath $snapshotFullPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100 -DateKind String
$ledger = Get-Content -LiteralPath $ledgerFullPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100

$snapshotTask = Get-RequiredProperty $snapshot 'task' 'Snapshot'
Assert-Condition ($snapshotTask -in @('PF-OPUX-v1-wave1', 'PF-OPUX-v1-wave1a', 'PF-OPUX-v1-SCRUM-11146-dev-v1', 'PF-OPUX-v1-SCRUM-11144-backend-v1', 'PF-OPUX-v1-SCRUM-11145-ui-v1', 'PF-OPUX-v1-SCRUM-11145-closeout-v1', 'PF-OPUX-v1-SCRUM-11147-impl-v1', 'PF-OPUX-v1-SCRUM-11148-impl-v1', 'PF-OPUX-v1-master-publication-v1', 'PF-OPUX-v1-SCRUM-11149-impl-v1', 'PF-OPUX-v1-SCRUM-11150-impl-v1', 'PF-OPUX-v1-SCRUM-11151-impl-v1', 'PF-OPUX-v1-SCRUM-11152-impl-v1')) 'Snapshot task must identify Wave 1, Wave 1A, SCRUM-11146, the bounded SCRUM-11144 backend, the SCRUM-11145 UI follow-up or its closeout, the SCRUM-11147/11148/11149/11150/11151/11152 implementation, or the master publication status synchronization.'
Assert-Condition ((Get-RequiredProperty $snapshot 'initiative' 'Snapshot') -eq 'PF-OPUX-v1') "Snapshot initiative is not PF-OPUX-v1."
Assert-Condition ([bool](Get-RequiredProperty $snapshot 'isLast' 'Snapshot')) 'Snapshot is incomplete: isLast must be true after pagination.'
$readAt = [string](Get-RequiredProperty $snapshot 'readAt' 'Snapshot')
$source = [string](Get-RequiredProperty $snapshot 'source' 'Snapshot')
$issues = @(Get-RequiredProperty $snapshot 'issues' 'Snapshot')
Assert-Condition (-not [string]::IsNullOrWhiteSpace($readAt)) 'Snapshot readAt is empty.'
Assert-Condition (-not [string]::IsNullOrWhiteSpace($source)) 'Snapshot source is empty.'
Assert-Condition ($issues.Count -eq 17) "Snapshot must contain exactly 17 issues; found $($issues.Count)."

$ledgerItems = @(Get-RequiredProperty $ledger 'items' 'Planning ledger')
Assert-Condition ($ledgerItems.Count -eq 17) "Planning ledger must contain exactly 17 items; found $($ledgerItems.Count)."
$ledgerByPlanningId = @{}
foreach ($ledgerItem in $ledgerItems) {
    $ledgerPlanningId = [string](Get-RequiredProperty $ledgerItem 'planningId' 'Planning ledger item')
    Assert-Condition (-not $ledgerByPlanningId.ContainsKey($ledgerPlanningId)) "Duplicate planning ID in ledger: $ledgerPlanningId"
    $ledgerByPlanningId[$ledgerPlanningId] = $ledgerItem
}

$issueById = @{}
$issueByKey = @{}
$issueByPlanningId = @{}
$descriptionByPlanningId = @{}
$acceptanceByPlanningId = @{}
foreach ($issue in $issues) {
    $issueId = [string](Get-RequiredProperty $issue 'id' 'Issue')
    $issueKey = [string](Get-RequiredProperty $issue 'key' "Issue $issueId")
    $fields = Get-RequiredProperty $issue 'fields' "Issue $issueKey"
    $description = [string](Get-RequiredProperty $fields 'description' "Issue $issueKey fields")
    $planningId = Get-PlanningId -Description $description -IssueKey $issueKey

    Assert-Condition (-not [string]::IsNullOrWhiteSpace($issueId)) "Issue $issueKey has an empty ID."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($issueKey)) "Issue ID $issueId has an empty key."
    Assert-Condition (-not $issueById.ContainsKey($issueId)) "Duplicate issue ID: $issueId"
    Assert-Condition (-not $issueByKey.ContainsKey($issueKey)) "Duplicate issue key: $issueKey"
    Assert-Condition (-not $issueByPlanningId.ContainsKey($planningId)) "Duplicate Planning-ID in snapshot: $planningId"
    Assert-Condition $ledgerByPlanningId.ContainsKey($planningId) "Snapshot Planning-ID is absent from the planning ledger: $planningId"

    $issueType = [string](Get-RequiredProperty (Get-RequiredProperty $fields 'issuetype' "Issue $issueKey fields") 'name' "Issue $issueKey issue type")
    $isEpic = $issueType -eq 'Epic'
    $acceptance = Get-AcceptanceCriteria -Description $description -IsEpic $isEpic -IssueKey $issueKey

    $issueById[$issueId] = $issue
    $issueByKey[$issueKey] = $issue
    $issueByPlanningId[$planningId] = $issue
    $descriptionByPlanningId[$planningId] = $description
    $acceptanceByPlanningId[$planningId] = $acceptance
}

Assert-Condition ($issueById.Count -eq 17) 'Snapshot does not contain 17 unique issue IDs.'
Assert-Condition ($issueByKey.Count -eq 17) 'Snapshot does not contain 17 unique issue keys.'
Assert-Condition ($issueByPlanningId.Count -eq 17) 'Snapshot does not contain 17 unique planning IDs.'
foreach ($plannedId in $ledgerByPlanningId.Keys) {
    Assert-Condition $issueByPlanningId.ContainsKey($plannedId) "Planning ledger item is absent from snapshot: $plannedId"
}

$linkOccurrences = @{}
$actualBlockedByPlanningIds = @{}
foreach ($planningId in $issueByPlanningId.Keys) {
    $actualBlockedByPlanningIds[$planningId] = [System.Collections.Generic.List[string]]::new()
}

foreach ($planningId in $issueByPlanningId.Keys) {
    $issue = $issueByPlanningId[$planningId]
    $links = @(Get-RequiredProperty $issue.fields 'issuelinks' "Issue $($issue.key) fields")
    foreach ($link in $links) {
        $linkId = [string](Get-RequiredProperty $link 'id' "Issue $($issue.key) link")
        $linkType = Get-RequiredProperty $link 'type' "Issue $($issue.key) link $linkId"
        Assert-Condition (([string](Get-RequiredProperty $linkType 'name' "Issue $($issue.key) link $linkId type")) -eq 'Blocks') "Issue $($issue.key) has unsupported link type on link $linkId."

        $inwardProperty = $link.PSObject.Properties['inwardIssue']
        $outwardProperty = $link.PSObject.Properties['outwardIssue']
        Assert-Condition (($null -ne $inwardProperty) -xor ($null -ne $outwardProperty)) "Issue $($issue.key) link $linkId must have exactly one inwardIssue/outwardIssue."

        if ($null -ne $inwardProperty) {
            $other = $inwardProperty.Value
            $sourceId = [string](Get-RequiredProperty $other 'id' "Issue $($issue.key) inward link $linkId")
            $sourceKey = [string](Get-RequiredProperty $other 'key' "Issue $($issue.key) inward link $linkId")
            $targetId = [string]$issue.id
            $targetKey = [string]$issue.key
        }
        else {
            $other = $outwardProperty.Value
            $sourceId = [string]$issue.id
            $sourceKey = [string]$issue.key
            $targetId = [string](Get-RequiredProperty $other 'id' "Issue $($issue.key) outward link $linkId")
            $targetKey = [string](Get-RequiredProperty $other 'key' "Issue $($issue.key) outward link $linkId")
        }

        Assert-Condition $issueById.ContainsKey($sourceId) "Link $linkId source ID $sourceId is outside the 17-issue snapshot."
        Assert-Condition $issueById.ContainsKey($targetId) "Link $linkId target ID $targetId is outside the 17-issue snapshot."
        Assert-Condition ([string]$issueById[$sourceId].key -eq $sourceKey) "Link $linkId source ID/key do not resolve to the same issue."
        Assert-Condition ([string]$issueById[$targetId].key -eq $targetKey) "Link $linkId target ID/key do not resolve to the same issue."

        $canonical = "$sourceId->$targetId"
        if (-not $linkOccurrences.ContainsKey($linkId)) {
            $linkOccurrences[$linkId] = [System.Collections.Generic.List[string]]::new()
        }
        $linkOccurrences[$linkId].Add($canonical)

        if ($null -ne $inwardProperty) {
            $sourceDescription = [string]$issueById[$sourceId].fields.description
            $sourcePlanningId = Get-PlanningId -Description $sourceDescription -IssueKey $sourceKey
            $actualBlockedByPlanningIds[$planningId].Add($sourcePlanningId)
        }
    }
}

Assert-Condition ($linkOccurrences.Count -eq 26) "Expected 26 unique Blocks links; found $($linkOccurrences.Count)."
foreach ($linkId in $linkOccurrences.Keys) {
    $occurrences = @($linkOccurrences[$linkId])
    Assert-Condition ($occurrences.Count -eq 2) "Blocks link $linkId must appear at both endpoints; found $($occurrences.Count) occurrences."
    Assert-Condition ($occurrences[0] -eq $occurrences[1]) "Blocks link $linkId has inconsistent endpoint direction."
}

foreach ($planningId in $issueByPlanningId.Keys) {
    $ledgerItem = $ledgerByPlanningId[$planningId]
    $plannedBlockedBy = @(@(Get-RequiredProperty $ledgerItem 'blockedByPlanningIds' "Ledger item $planningId") | ForEach-Object { [string]$_ } | Sort-Object)
    $actualBlockedBy = @($actualBlockedByPlanningIds[$planningId] | Sort-Object)
    Assert-Condition (($plannedBlockedBy -join "`n") -ceq ($actualBlockedBy -join "`n")) "Actual inward Blocks dependencies differ from the planned mapping for $planningId."

    $plannedParentId = Get-RequiredProperty $ledgerItem 'parentPlanningId' "Ledger item $planningId"
    $parentProperty = $issueByPlanningId[$planningId].fields.PSObject.Properties['parent']
    if ($null -eq $plannedParentId) {
        Assert-Condition ($null -eq $parentProperty -or $null -eq $parentProperty.Value) "$planningId should not have a Jira parent."
    }
    else {
        Assert-Condition ($null -ne $parentProperty -and $null -ne $parentProperty.Value) "$planningId is missing its Jira parent."
        $actualParentId = [string](Get-RequiredProperty $parentProperty.Value 'id' "Issue $planningId parent")
        $actualParentKey = [string](Get-RequiredProperty $parentProperty.Value 'key' "Issue $planningId parent")
        Assert-Condition $issueById.ContainsKey($actualParentId) "$planningId parent ID $actualParentId is outside the snapshot."
        Assert-Condition ([string]$issueById[$actualParentId].key -eq $actualParentKey) "$planningId parent ID/key do not resolve to the same issue."
        $actualParentPlanningId = Get-PlanningId -Description ([string]$issueById[$actualParentId].fields.description) -IssueKey $actualParentKey
        Assert-Condition ($actualParentPlanningId -eq [string]$plannedParentId) "$planningId Jira parent differs from the planned parent."
    }
}

$rows = foreach ($ledgerItem in ($ledgerItems | Sort-Object { [int](Get-RequiredProperty $_ 'recommendedOrder' 'Planning ledger item') })) {
    $planningId = [string]$ledgerItem.planningId
    $issue = $issueByPlanningId[$planningId]
    $fields = $issue.fields
    $issueKey = [string]$issue.key
    $issueType = [string]$fields.issuetype.name
    $parent = $fields.PSObject.Properties['parent']
    $parentId = if ($null -ne $parent -and $null -ne $parent.Value) { [string]$parent.Value.id } else { '' }
    $parentKey = if ($null -ne $parent -and $null -ne $parent.Value) { [string]$parent.Value.key } else { '' }
    $blockedBy = @($actualBlockedByPlanningIds[$planningId] | Sort-Object)
    $blockedByIds = @($blockedBy | ForEach-Object { [string]$issueByPlanningId[$_].id })
    $blockedByKeys = @($blockedBy | ForEach-Object { [string]$issueByPlanningId[$_].key })
    $writeAction = Get-WriteAction -PlanningId $planningId -Issue $issue

    $summary = [string](Get-RequiredProperty $fields 'summary' "Issue $issueKey fields")
    $priority = [string](Get-RequiredProperty (Get-RequiredProperty $fields 'priority' "Issue $issueKey fields") 'name' "Issue $issueKey priority")
    $status = [string](Get-RequiredProperty (Get-RequiredProperty $fields 'status' "Issue $issueKey fields") 'name' "Issue $issueKey status")
    $updated = [string](Get-RequiredProperty $fields 'updated' "Issue $issueKey fields")
    $labels = @(Get-LabelStrings -Fields $fields -IssueKey $issueKey)
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($summary)) "$issueKey summary is empty."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($priority)) "$issueKey priority is empty."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($status)) "$issueKey status is empty."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($updated)) "$issueKey updated timestamp is empty."
    Assert-Condition ($labels.Count -gt 0) "$issueKey labels are empty."

    [pscustomobject][ordered]@{
        Planning_ID                       = $planningId
        Issue_ID                          = [string]$issue.id
        Issue_Key                         = $issueKey
        Issue_URL                         = "{0}/browse/{1}" -f $JiraBaseUrl.TrimEnd('/'), $issueKey
        Issue_Type                        = $issueType
        Summary                           = $summary
        Description                       = $descriptionByPlanningId[$planningId]
        Acceptance_Criteria               = $acceptanceByPlanningId[$planningId]
        Priority                          = $priority
        Status                            = $status
        Parent_Issue_ID                   = $parentId
        Parent_Issue_Key                  = $parentKey
        Labels                            = $labels -join ';'
        Planned_Dependency_Planning_IDs   = $blockedBy -join ';'
        Verified_Dependency_Issue_IDs     = $blockedByIds -join ';'
        Verified_Dependency_Issue_Keys    = $blockedByKeys -join ';'
        Dependency_Link_Status            = if ($blockedBy.Count -eq 0) { 'NONE_PLANNED' } else { 'VERIFIED' }
        Recommended_Order                 = [string](Get-RequiredProperty $ledgerItem 'recommendedOrder' "Ledger item $planningId")
        Write_Action                      = $writeAction
        Readback_Status                   = 'VERIFIED_CURRENT'
        Jira_Updated_At                   = $updated
        Readback_At                       = $readAt
        Notes                             = Get-WriteActionNote -WriteAction $writeAction
    }
}

$outputDirectory = Split-Path -Parent $outputFullPath
Assert-Condition (Test-Path -LiteralPath $outputDirectory -PathType Container) "Output directory does not exist: $outputDirectory"

$encoding = if ($PSVersionTable.PSVersion.Major -ge 6) { 'utf8BOM' } else { 'utf8' }
$rows | Export-Csv -LiteralPath $outputFullPath -NoTypeInformation -Encoding $encoding

$bytes = [System.IO.File]::ReadAllBytes($outputFullPath)
Assert-Condition ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) 'Generated CSV is not UTF-8 with BOM.'

$parsedRows = @(Import-Csv -LiteralPath $outputFullPath -Encoding utf8)
Assert-Condition ($parsedRows.Count -eq 17) "Generated CSV must parse to 17 rows; found $($parsedRows.Count)."
$parsedHeaders = @($parsedRows[0].PSObject.Properties.Name)
Assert-Condition (($parsedHeaders -join "`n") -ceq ($expectedHeaders -join "`n")) 'Generated CSV header does not exactly match the required 23-column schema.'

$parsedByPlanningId = @{}
$parsedIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$parsedKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($row in $parsedRows) {
    Assert-Condition (-not $parsedByPlanningId.ContainsKey($row.Planning_ID)) "Generated CSV has duplicate Planning_ID $($row.Planning_ID)."
    Assert-Condition $parsedIds.Add($row.Issue_ID) "Generated CSV has duplicate Issue_ID $($row.Issue_ID)."
    Assert-Condition $parsedKeys.Add($row.Issue_Key) "Generated CSV has duplicate Issue_Key $($row.Issue_Key)."
    $parsedByPlanningId[$row.Planning_ID] = $row

    foreach ($requiredColumn in @('Planning_ID', 'Issue_ID', 'Issue_Key', 'Issue_URL', 'Issue_Type', 'Summary', 'Description', 'Acceptance_Criteria', 'Priority', 'Status', 'Labels', 'Recommended_Order', 'Write_Action', 'Readback_Status', 'Jira_Updated_At', 'Readback_At', 'Notes')) {
        Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$row.$requiredColumn)) "Generated CSV row $($row.Planning_ID) has an empty required field: $requiredColumn"
    }
}

Assert-Condition ($parsedByPlanningId.Count -eq 17) 'Generated CSV does not contain 17 unique Planning_ID values.'
foreach ($planningId in $issueByPlanningId.Keys) {
    Assert-Condition $parsedByPlanningId.ContainsKey($planningId) "Generated CSV is missing $planningId."
    $row = $parsedByPlanningId[$planningId]
    $issue = $issueByPlanningId[$planningId]
    Assert-Condition ([string]$row.Description -ceq [string]$issue.fields.description) "$planningId description changed during CSV serialization."
    Assert-Condition ([string]$row.Acceptance_Criteria -ceq [string]$acceptanceByPlanningId[$planningId]) "$planningId acceptance criteria changed during CSV serialization."
    Assert-Condition ([string]$row.Status -ceq [string]$issue.fields.status.name) "$planningId status differs from the snapshot."
    Assert-Condition ($row.Jira_Updated_At -ceq $issue.fields.updated) "$planningId updated timestamp changed during CSV serialization."
    Assert-Condition ($row.Readback_At -ceq $snapshot.readAt) "$planningId readback timestamp changed during CSV serialization."
    Assert-Condition ([string]$row.Labels -ceq ((Get-LabelStrings -Fields $issue.fields -IssueKey ([string]$issue.key)) -join ';')) "$planningId labels changed during CSV serialization."
    Assert-Condition ([string]$row.Parent_Issue_ID -ceq $(if ($null -ne $issue.fields.PSObject.Properties['parent'] -and $null -ne $issue.fields.parent) { [string]$issue.fields.parent.id } else { '' })) "$planningId parent ID differs from the snapshot."

    $actualBlockedBy = @($actualBlockedByPlanningIds[$planningId] | Sort-Object)
    $expectedDependencyIds = @($actualBlockedBy | ForEach-Object { [string]$issueByPlanningId[$_].id }) -join ';'
    $expectedDependencyKeys = @($actualBlockedBy | ForEach-Object { [string]$issueByPlanningId[$_].key }) -join ';'
    Assert-Condition ([string]$row.Verified_Dependency_Issue_IDs -ceq $expectedDependencyIds) "$planningId dependency IDs differ from the snapshot."
    Assert-Condition ([string]$row.Verified_Dependency_Issue_Keys -ceq $expectedDependencyKeys) "$planningId dependency keys differ from the snapshot."
}

[pscustomobject]@{
    SnapshotPath     = $snapshotFullPath
    OutputPath       = $outputFullPath
    Rows             = $parsedRows.Count
    UniqueIssueIds   = $parsedIds.Count
    UniqueIssueKeys  = $parsedKeys.Count
    UniquePlanningIds = $parsedByPlanningId.Count
    BlocksLinks      = $linkOccurrences.Count
    HeaderColumns    = $parsedHeaders.Count
    Utf8Bom          = $true
    ExactTimestamps  = $parsedRows.Count * 2
    Validation       = 'PASS'
} | Format-List
