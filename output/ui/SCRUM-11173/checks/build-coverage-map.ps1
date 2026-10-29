$ErrorActionPreference='Stop'
$all=@()
foreach($name in 'e6-ui-architecture-final','e6-workflow-persistence-final','e6-q01-final') {
    $path=Join-Path $PSScriptRoot ($name+'.trx')
    [xml]$trx=Get-Content -LiteralPath $path -Raw
    $all += @($trx.TestRun.Results.UnitTestResult | ForEach-Object {
        [pscustomobject]@{Name=$_.testName;Outcome=$_.outcome;Source=($name+'.trx')}
    })
}
$groups=[ordered]@{
    'Q01.AC1-2 synthetic viewport and Chinese layout'='GuidedAcceptanceTests\.(Long_Chinese|Multiple_sizes)|GuidedSessionShellTests|GuidedProcessingCropTests|GuidedCompletionVisualEvidenceTests'
    'Q01.AC3 names and candidate Tab set'='GuidedAcceptanceTests\.Current_actions'
    'Q01.AC3 retained input and exact identity'='GuidedSessionInputTests|GuidedSessionEditorInputTests|GuidedImportDraftTests|GuidedImportRouteTests|GuidedUtilityReturnTests|GuidedSessionSaveOwnershipTests'
    'Q01.AC4 resources and bindings'='GuidedThemeResourceTests|PrintFlow.Tests.Architecture\.'
    'Q02.AC1 three routes'='SessionServiceTests\.(Full_walkthrough|PrepareCustomerDesign)|PhotoshopTiffWorkflowOutputTests|PhotoshopTiffFinalReviewTests|GuidedSessionProjectionTests|GuidedImportResumeTests|GuidedProcessingPrintTests|SessionSmokeTests\.(Production_smoke_A|Production_smoke_C|Smoke_I)'
    'Q02.AC1 optional skip reject retry'='RetryAndReviewTests|GuidedSessionShellTests|GuidedProcessingReviewTests|SessionSmokeTests\.Smoke_C|SessionControlsTests\.(Skipping_a_skippable|Skip_is_not_offered)'
    'Q02.AC1 return and downstream invalidation'='ReturnToStepTests|GuidedSessionNavigationTests|GuidedSessionReviewFixTests'
    'Q02.AC1 correction and handoff'='ColleagueCorrectionTests|GuidedCompletionRecoveryTests|StopAndTakeOverTests'
    'Q02.AC1 controlled restart recovery'='StartupRecoveryTests|GuidedSessionInputTests\.Reopened_task'
    'Q02.AC1 multiple sizes'='AddAnotherSizeTests|GuidedCompletionSaveTests|GuidedAcceptanceTests\.Multiple_sizes'
    'Q02.AC1 save conflicts failures uncertainty'='FinalSaveCoordinatorTests|GuidedCompletionSaveTests|GuidedSessionSaveOwnershipTests|GuidedCompletionFeedbackTests'
    'Q02.AC2 exact approval'='GuidedSessionInputTests|GuidedSessionEditorInputTests|PhotoshopTiffFinalReviewTests|FinalSaveCoordinatorTests|GuidedCompletionFeedbackTests\.Refreshed_return'
    'Q02.AC2 originals and snapshots'='SessionServiceTests\.(Import_produces|Mutating_an_approved)|HomeInputAcceptanceTests'
    'Q02.AC2 environment and lock'='EnvironmentGateTests|SessionServiceTests\.A_step_that_needs_the_automation_lock|StartupRecoveryTests|GuidedUtilityReturnTests'
    'Q02.AC2 controlled TIFF contracts'='PhotoshopTiffWorkflowOutputTests|PhotoshopTiffFinalReviewTests|FlexibleSizeWorkflowTests|GuidedProcessingPrintTests'
}
$entries=@()
foreach($entry in $groups.GetEnumerator()) {
    $selected=@($all | Where-Object Name -Match $entry.Value | Sort-Object Source,Name)
    if($selected.Count -eq 0) { throw "No executed tests for $($entry.Key)" }
    $entries += [ordered]@{Requirement=$entry.Key;SelectionRegex=$entry.Value;ExecutedCases=$selected.Count;FailedCases=@($selected | Where-Object Outcome -NE 'Passed').Count;Tests=$selected}
}
$sources=@('e6-ui-architecture-final','e6-workflow-persistence-final','e6-q01-final' | ForEach-Object {
    $path=Join-Path $PSScriptRoot ($_+'.trx')
    [ordered]@{Path=($_+'.trx');Sha256=(Get-FileHash -LiteralPath $path).Hash}
})
[ordered]@{
    Interpretation='Executed synthetic service/offscreen cases only. Overlapping groups are not additional tests or blanket AC PASS. Read COVERAGE.md for inherited failures and limits.'
    Sources=$sources;Groups=$entries
    NotRun=@('Real outer-window client area','Actual 100/125/150 percent DPI','Physical keyboard/mouse','Windows UIA client and screen reader','Real novice','Runtime candidate preparation and interactive host','External software and production')
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'coverage-map.json') -Encoding utf8
$entries | ForEach-Object { "$($_.Requirement): $($_.ExecutedCases) cases, $($_.FailedCases) non-passed (see exact names)" }
