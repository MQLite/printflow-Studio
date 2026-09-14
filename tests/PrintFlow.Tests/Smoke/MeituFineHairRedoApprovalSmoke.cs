using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// Records the operator's fresh quality approval for exactly the registered fine-hair redo.
/// This one-shot task seam never opens Meitu, exports a file or invokes processing.
/// </summary>
public sealed class MeituFineHairRedoApprovalSmoke
{
    private const string ExpectedSession = "01a0a22e-e1a5-77c8-bb67-19543bfc0a33";
    private const string ExpectedFailedAttempt = "01a0a22e-e3e7-7f59-b2cf-75e2737d3e37";
    private const string ExpectedRevision = "01a0a248-d7eb-7d20-ad5d-389610b1287a";
    private const string ExpectedSourceHash =
        "5A705FE390AF87D1D48A0554D4908C425D4703A8807CA78EC73AC0E55E3C8D8E";
    private const string ExpectedOutputHash =
        "731BE2E042A3FA47EFDD21254FA8F774E97F39E046BFFCE73BF63DEA0918DB19";
    private const string ExpectedExportRelative =
        "Sessions/S_20260914T230936Z_3bfc0a33/Working/" +
        "01a0a22e-e3e7-7f59-b2cf-75e2737d3e37/FIX-FINE-HAIR-001-REDO_CUTOUT.png";

    [Fact]
    public async Task Approve_only_the_exact_registered_fine_hair_redo()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_MEITU_FINE_HAIR_REDO_APPROVAL") != "1")
        {
            return;
        }

        string root = Required("PRINTFLOW_MEITU_FINE_HAIR_REDO_ROOT");
        string workspaceRoot = Path.Combine(root, "workspace");
        string databasePath = Path.Combine(root, "redo.db");
        string priorReceipt = Path.Combine(root, "loaded-redo-receipt.json");
        string approvalReceipt = Path.Combine(root, "loaded-redo-approval-receipt.json");
        File.Exists(priorReceipt).ShouldBeTrue("The preserved export/registration receipt is required.");
        File.Exists(approvalReceipt).ShouldBeFalse(
            "The one-shot approval receipt already exists; do not rewrite a completed operator decision.");

        FileWorkspace workspace = new(workspaceRoot);
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            Path.Combine(RepositoryRoot(), "appsettings.json"));
        SqliteConnectionFactory connections = new(databasePath);
        using ServiceProvider provider = ServiceRegistration.BuildServiceProvider(
            configuration, workspaceRoot, connections);
        ISessionService sessions = provider.GetRequiredService<ISessionService>();
        ISessionRepository repository = provider.GetRequiredService<ISessionRepository>();

        SessionId sessionId = SessionId.From(Guid.Parse(ExpectedSession));
        RevisionId revisionId = RevisionId.From(Guid.Parse(ExpectedRevision));
        AttemptId failedAttemptId = AttemptId.From(Guid.Parse(ExpectedFailedAttempt));
        Sha256 sourceHash = Sha256.Parse(ExpectedSourceHash);
        Sha256 outputHash = Sha256.Parse(ExpectedOutputHash);

        IReadOnlyList<SessionListItem> recent = await Must(repository.ListRecentAsync(
            10, DateTimeOffset.MinValue, CancellationToken.None));
        recent.Count.ShouldBe(1, "The task-owned redo database must contain exactly one session.");
        recent[0].Id.ShouldBe(sessionId);

        SessionAggregate before = (await Must(repository.LoadAsync(
            sessionId, CancellationToken.None)))!;
        before.Session.OutputName.Value.ShouldBe("FIX-FINE-HAIR-001-REDO");
        before.Session.WorkflowType.ShouldBe(WorkflowType.PrepareAsset);
        before.Snapshot.ShouldNotBeNull();
        Revision input = before.Revisions.Single(revision => revision.Id == before.Snapshot!.RootRevisionId);
        input.Operation.ShouldBe(OperationKind.Import);
        input.Sha256.ShouldBe(sourceHash);

        ProcessingAttempt failed = before.Attempts.Single(attempt => attempt.Id == failedAttemptId);
        failed.Step.ShouldBe(StepKind.BackgroundRemoval);
        failed.Status.ShouldBe(AttemptStatus.Failed);
        before.Revisions.Single(revision => revision.Id == failed.InputRevisionId).Sha256.ShouldBe(sourceHash);

        Revision result = before.Revisions.Single(revision => revision.Id == revisionId);
        result.SessionId.ShouldBe(sessionId);
        result.Operation.ShouldBe(OperationKind.ManualResultImport);
        result.IsValid.ShouldBeTrue();
        result.ReviewState.ShouldBe(ReviewState.NotReviewed);
        result.Sha256.ShouldBe(outputHash);
        result.Facts.Format.ShouldBe(ImageFormat.Png);
        result.Facts.ByteLength.ShouldBe(1_430_946);
        result.Facts.PixelWidth.ShouldBe(1_200);
        result.Facts.PixelHeight.ShouldBe(1_600);

        ProcessingAttempt manual = before.Attempts.Single(attempt => attempt.OutputRevisionId == revisionId);
        manual.Step.ShouldBe(StepKind.BackgroundRemoval);
        manual.Operation.ShouldBe(OperationKind.ManualResultImport);
        manual.Status.ShouldBe(AttemptStatus.Succeeded);
        string exactExport = workspace.ResolveAbsolute(WorkspaceFileRef.Create(
            ExpectedExportRelative, WorkspaceArea.Working));
        Path.GetFullPath(manual.ManualResultSourcePath!).ShouldBe(
            Path.GetFullPath(exactExport), StringCompareShould.IgnoreCase);

        FileFacts exportedFacts = await Must(new WicFileInspector().InspectAsync(
            exactExport, CancellationToken.None));
        exportedFacts.ShouldBe(result.Facts);
        FileFacts registeredFacts = await Must(new WicFileInspector().InspectAsync(
            workspace.ResolveAbsolute(result.File), CancellationToken.None));
        registeredFacts.ShouldBe(result.Facts);
        MeituTransparencyFacts transparency = await Must(
            new WicMeituTransparencyInspector().InspectAsync(
                workspace.ResolveAbsolute(result.File), CancellationToken.None));
        transparency.ShouldBe(new MeituTransparencyFacts(1_920_000, 764_826, 1_545_347));
        MeituTransparencyRule.Validate(transparency).IsSuccess.ShouldBeTrue();

        SessionStep background = before.Steps.Single(step => step.Step == StepKind.BackgroundRemoval);
        background.State.ShouldBe(StepState.ReviewRequired);
        background.CurrentRevisionId.ShouldBe(revisionId);
        background.CurrentRevisionSha256.ShouldBe(outputHash);
        before.Session.CurrentStep.ShouldBe(StepKind.BackgroundRemoval);
        before.Steps.Single(step => step.Step == StepKind.Enhancement).State.ShouldBe(StepState.Skipped);
        before.Reviews.ShouldNotContain(review => review.Step == StepKind.Enhancement);
        before.Reviews.ShouldNotContain(review => review.SubjectId == revisionId.Value);

        SessionView approved = await Must(sessions.ExecuteAsync(
            sessionId,
            new WorkflowCommand.Approve(
                StepKind.BackgroundRemoval,
                outputHash,
                "Operator approved the displayed fresh redo for PF-FIX-MEITU-CONFIRM."),
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None));
        approved.CurrentStep!.Step.ShouldBe(StepKind.Trim);
        approved.CurrentStep.State.ShouldBe(StepState.Waiting);

        SessionAggregate after = (await Must(repository.LoadAsync(
            sessionId, CancellationToken.None)))!;
        Revision approvedRevision = after.Revisions.Single(revision => revision.Id == revisionId);
        approvedRevision.ReviewState.ShouldBe(ReviewState.Approved);
        after.Steps.Single(step => step.Step == StepKind.BackgroundRemoval).State
            .ShouldBe(StepState.Approved);
        after.Session.CurrentStep.ShouldBe(StepKind.Trim);
        after.Steps.Single(step => step.Step == StepKind.Trim).State.ShouldBe(StepState.Waiting);

        ReviewDecision review = after.Reviews.Single(review => review.SubjectId == revisionId.Value);
        review.Step.ShouldBe(StepKind.BackgroundRemoval);
        review.SubjectKind.ShouldBe(ReviewSubjectKind.Revision);
        review.ReviewedSha256.ShouldBe(outputHash);
        review.IsApproved.ShouldBeTrue();
        review.QuickReason.ShouldBeNull();
        after.Reviews.ShouldNotContain(item => item.Step == StepKind.Enhancement);
        after.Attempts.Single(attempt => attempt.Id == failedAttemptId).ShouldBe(failed);
        (await Must(repository.GetAutomationLockAsync(CancellationToken.None))).IsHeld.ShouldBeFalse();

        await File.WriteAllTextAsync(approvalReceipt, JsonSerializer.Serialize(new
        {
            SessionId = sessionId.ToString(),
            RevisionId = revisionId.ToString(),
            OutputSha256 = outputHash.ToString(),
            RegisteredFile = result.File.RelativePath,
            ExportedFile = ExpectedExportRelative,
            result.Facts.ByteLength,
            result.Facts.PixelWidth,
            result.Facts.PixelHeight,
            Transparency = transparency,
            ReviewId = review.Id.ToString(),
            ReviewState = approvedRevision.ReviewState.ToString(),
            BackgroundRemovalState = StepState.Approved.ToString(),
            NextStep = after.Session.CurrentStep.ToString(),
            NextStepState = after.Steps.Single(step => step.Step == StepKind.Trim).State.ToString(),
            FailedStagingAttemptId = failedAttemptId.ToString(),
            FailedStagingAttemptState = AttemptStatus.Failed.ToString(),
            EnhancementReviewCreated = false,
            MeituTouched = false,
            ProcessingInvoked = false,
            ExportInvoked = false,
            SessionAutomationLockHeld = false,
            PriorReceipt = priorReceipt,
            PriorReceiptSha256 = FileSha256(priorReceipt).ToString(),
        }, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine(approvalReceipt);
    }

    private static async Task<T> Must<T>(Task<OperationResult<T>> pending)
    {
        OperationResult<T> result = await pending;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private static Sha256 FileSha256(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Sha256.FromBytes(SHA256.HashData(stream));
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Required approval association '{name}' was not supplied.");

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
