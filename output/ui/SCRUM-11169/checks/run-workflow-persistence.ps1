$ErrorActionPreference = 'Stop'
# Audited persistence tests use synthetic files, local temporary storage and fake adapters.
# No UI namespace, Window.Show, Application.Run, native input or workstation opt-in.
$classes = @(
    'ReturnToStepTests', 'RetryAndReviewTests', 'StopAndTakeOverTests',
    'StartupRecoveryTests', 'PdfPreparationWorkflowTests', 'PsdPreparationWorkflowTests',
    'PhotoshopTiffWorkflowOutputTests', 'PhotoshopTiffFinalReviewTests',
    'FinalSaveCoordinatorTests', 'ColleagueCorrectionTests', 'ManualCropStepTests',
    'TrimAdjustmentStepTests', 'AddAnotherSizeTests', 'FlexibleSizeWorkflowTests',
    'EnvironmentGateTests', 'SessionServiceTests'
)
$filter = ($classes | ForEach-Object { 'FullyQualifiedName~PrintFlow.Tests.Integration.Persistence.' + $_ + '.' }) -join '|'
$filter | Set-Content (Join-Path $PSScriptRoot 'workflow-persistence-filter.txt') -Encoding utf8
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-build --filter $filter --results-directory $PSScriptRoot --logger 'trx;LogFileName=e2-workflow-persistence-final.trx' > (Join-Path $PSScriptRoot 'e2-workflow-persistence-final.log') 2>&1
$testExitCode = $LASTEXITCODE
Get-Content (Join-Path $PSScriptRoot 'e2-workflow-persistence-final.log') -Tail 10
exit $testExitCode
