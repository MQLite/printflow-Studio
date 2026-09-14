using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// Opt-in recovery of one already-observed Meitu result. It never opens a document or invokes an
/// operation, and is inert unless every exact association input is supplied.
/// </summary>
public sealed class MeituObservedResultRecoverySmoke
{
    private const string Enable = "PRINTFLOW_MEITU_OBSERVED_RESULT_RECOVERY";

    [Fact]
    public async Task Export_validate_register_and_approve_the_exact_observed_cutout()
    {
        if (Environment.GetEnvironmentVariable(Enable) != "1")
        {
            return;
        }

        string workspaceRoot = Required("PRINTFLOW_RECOVERY_WORKSPACE_ROOT");
        string databasePath = Required("PRINTFLOW_RECOVERY_DATABASE");
        string sessionValue = Required("PRINTFLOW_RECOVERY_SESSION_ID");
        string workingRelative = Required("PRINTFLOW_RECOVERY_WORKING_FILE");
        string outputRelative = Required("PRINTFLOW_RECOVERY_OUTPUT_FILE");
        string presetManifest = Required("PRINTFLOW_RECOVERY_PRESET_MANIFEST");
        Sha256 presetSha256 = Sha256.Parse(Required("PRINTFLOW_RECOVERY_PRESET_SHA256"));
        string evidenceDirectory = Required("PRINTFLOW_RECOVERY_EVIDENCE_DIRECTORY");

        Directory.CreateDirectory(evidenceDirectory);
        FileWorkspace workspace = new(workspaceRoot);
        WorkspaceFileRef workingCopy = WorkspaceFileRef.Create(workingRelative, WorkspaceArea.Working);
        WorkspaceFileRef output = WorkspaceFileRef.Create(outputRelative, WorkspaceArea.Working);

        await using WorkstationAutomationLeaseScope automationLease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();

        IMeituAutomationFoundation foundation = MeituAutomationComposition.CreateFoundation(
            presetManifest, presetSha256, workspace, evidenceDirectory, TimeProvider.System);

        OperationResult<FileFacts> sourceBefore =
            await foundation.InspectManagedFileAsync(workingCopy, CancellationToken.None);
        sourceBefore.IsSuccess.ShouldBeTrue(
            sourceBefore.IsFailure ? sourceBefore.Failure.TechnicalDetail : string.Empty);

        OperationResult<MeituObservedResultExport> recovered = await foundation.ExportObservedResultAsync(
            MeituOperation.RemoveBackground,
            workingCopy,
            sourceBefore.Value,
            output,
            InertAutomationStopSignal.Instance,
            CancellationToken.None);
        recovered.IsSuccess.ShouldBeTrue(
            recovered.IsFailure ? recovered.Failure.ToString() : string.Empty);
        recovered.Value.SurfacePhase.ShouldBe(MeituDocumentSurfacePhase.BackgroundRemovalResult);
        recovered.Value.ObservedDocumentIdentity.ShouldBe("FIX-FINE-HAIR-001_副本");

        MeituExportedOutput exported = recovered.Value.Output;
        exported.Facts.Format.ShouldBe(ImageFormat.Png);
        exported.Facts.PixelWidth.ShouldBe(sourceBefore.Value.PixelWidth);
        exported.Facts.PixelHeight.ShouldBe(sourceBefore.Value.PixelHeight);
        exported.Transparency.ShouldNotBeNull();
        MeituTransparencyRule.Validate(exported.Transparency!).IsSuccess.ShouldBeTrue();

        OperationResult<bool> dismissed = await foundation.DismissExportResultSurfaceAsync(
            recovered.Value.Target, CancellationToken.None);
        dismissed.IsSuccess.ShouldBeTrue(dismissed.IsFailure ? dismissed.Failure.ToString() : string.Empty);

        string repositoryRoot = RepositoryRoot();
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            Path.Combine(repositoryRoot, "appsettings.json"));
        SqliteConnectionFactory connections = new(databasePath);
        using ServiceProvider provider = ServiceRegistration.BuildServiceProvider(
            configuration, workspaceRoot, connections);
        ISessionService sessions = provider.GetRequiredService<ISessionService>();
        ISessionRepository repository = provider.GetRequiredService<ISessionRepository>();
        SessionId sessionId = SessionId.From(Guid.Parse(sessionValue));

        OperationResult<SessionView> handedOff = await sessions.ExecuteAsync(
            sessionId,
            new WorkflowCommand.HandOff(
                StepKind.BackgroundRemoval,
                "Operator-confirmed retained cutout; guarded export recovered the exact observed result."),
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None);
        handedOff.IsSuccess.ShouldBeTrue(handedOff.IsFailure ? handedOff.Failure.ToString() : string.Empty);

        OperationResult<SessionView> submitted = await sessions.ExecuteAsync(
            sessionId,
            new WorkflowCommand.SubmitManualResult(
                StepKind.BackgroundRemoval,
                workspace.ResolveAbsolute(output)),
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None);
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Failure.ToString() : string.Empty);
        submitted.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);

        OperationResult<SessionAggregate?> stored = await repository.LoadAsync(
            sessionId, CancellationToken.None);
        stored.IsSuccess.ShouldBeTrue(stored.IsFailure ? stored.Failure.ToString() : string.Empty);
        SessionAggregate record = stored.Value!;
        Revision revision = record.Revisions.Single(revision =>
            revision.Operation == OperationKind.ManualResultImport &&
            revision.Sha256 == exported.Facts.Sha256);
        revision.ReviewState.ShouldBe(ReviewState.NotReviewed);

        OperationResult<SessionView> approved = await sessions.ExecuteAsync(
            sessionId,
            new WorkflowCommand.Approve(StepKind.BackgroundRemoval, revision.Sha256),
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None);
        approved.IsSuccess.ShouldBeTrue(approved.IsFailure ? approved.Failure.ToString() : string.Empty);

        OperationResult<MeituTarget> closed = await foundation.CloseDocumentAsync(
            recovered.Value.Target, CancellationToken.None);
        closed.IsSuccess.ShouldBeTrue(closed.IsFailure ? closed.Failure.ToString() : string.Empty);

        OperationResult<MeituReadiness> ready = await foundation.EnsureReadyAsync(CancellationToken.None);
        ready.IsSuccess.ShouldBeTrue(ready.IsFailure ? ready.Failure.ToString() : string.Empty);
        ready.Value.State.IsSafeStartingState.ShouldBeTrue();

        OperationResult<SessionAggregate?> verified = await repository.LoadAsync(
            sessionId, CancellationToken.None);
        SessionAggregate finalRecord = verified.Value!;
        Revision finalRevision = finalRecord.Revisions.Single(item => item.Id == revision.Id);
        finalRevision.ReviewState.ShouldBe(ReviewState.Approved);
        finalRecord.Reviews.ShouldContain(review =>
            review.SubjectId == revision.Id.Value &&
            review.ReviewedSha256 == revision.Sha256 &&
            review.IsApproved);
        finalRecord.Attempts.Single(attempt => attempt.OutputRevisionId == revision.Id)
            .Operation.ShouldBe(OperationKind.ManualResultImport);
        finalRecord.Attempts.Single(attempt =>
            attempt.Id.Value == Guid.Parse("01a09dc9-d787-7e7a-a96e-a9f10557911e"))
            .Status.ShouldBe(PrintFlow.Domain.Attempts.AttemptStatus.Failed);
        (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();

        string receipt = Path.Combine(evidenceDirectory, "recovery-receipt.json");
        await File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(new
        {
            SessionId = sessionId.ToString(),
            OriginalFailedAttemptId = "01a09dc9-d787-7e7a-a96e-a9f10557911e",
            SurfacePhase = recovered.Value.SurfacePhase.ToString(),
            recovered.Value.ObservedDocumentIdentity,
            WorkingFile = workingCopy.RelativePath,
            SourceSha256 = sourceBefore.Value.Sha256.ToString(),
            OutputFile = output.RelativePath,
            OutputSha256 = exported.Facts.Sha256.ToString(),
            exported.Facts.ByteLength,
            exported.Facts.PixelWidth,
            exported.Facts.PixelHeight,
            Transparency = exported.Transparency,
            ManualResultRevisionId = revision.Id.ToString(),
            ReviewState = finalRevision.ReviewState.ToString(),
            NextStep = finalRecord.Session.CurrentStep.ToString(),
            MeituReadiness = ready.Value.State.State.ToString(),
            SharedLeaseHeldInsideRecovery = true,
            SessionAutomationLockHeld = false,
            ProcessingInvoked = false,
            PresetPublicationAllowed = false,
        }, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine(receipt);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Required recovery association '{name}' was not supplied.");

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintFlowStudio.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
