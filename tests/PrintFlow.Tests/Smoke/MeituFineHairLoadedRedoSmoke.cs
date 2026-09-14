using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// Resumes the one exact fine-hair Working copy retained after a post-open target loss. It does
/// not open another document: one signed ordinary editor and the exact Save-default identity are
/// mandatory before Background Removal can be invoked.
/// </summary>
public sealed class MeituFineHairLoadedRedoSmoke
{
    [Fact]
    public async Task Process_export_and_register_the_exact_retained_loaded_working_copy()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_MEITU_FINE_HAIR_LOADED_REDO") != "1")
        {
            return;
        }

        string root = Required("PRINTFLOW_MEITU_FINE_HAIR_REDO_ROOT");
        string preset = Required("PRINTFLOW_MEITU_SMOKE_PRESET_MANIFEST");
        Sha256 presetHash = Sha256.Parse(Required("PRINTFLOW_MEITU_SMOKE_PRESET_SHA256"));
        string workspaceRoot = Path.Combine(root, "workspace");
        string databasePath = Path.Combine(root, "redo.db");
        string evidenceDirectory = Path.Combine(root, "loaded-resume-evidence");

        Directory.Exists(workspaceRoot).ShouldBeTrue($"Missing retained workspace '{workspaceRoot}'.");
        File.Exists(databasePath).ShouldBeTrue($"Missing retained database '{databasePath}'.");
        Directory.CreateDirectory(evidenceDirectory);

        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            Path.Combine(RepositoryRoot(), "appsettings.json"));
        SqliteConnectionFactory connections = new(databasePath);
        using ServiceProvider provider = ServiceRegistration.BuildServiceProvider(
            configuration, workspaceRoot, connections);
        ISessionService sessions = provider.GetRequiredService<ISessionService>();
        ISessionRepository repository = provider.GetRequiredService<ISessionRepository>();
        IWorkspace workspace = new FileWorkspace(workspaceRoot);

        IReadOnlyList<SessionListItem> recent = await Must(repository.ListRecentAsync(
            10, DateTimeOffset.MinValue, CancellationToken.None));
        recent.Count.ShouldBe(1, "The task-owned redo database must contain exactly one session.");
        SessionAggregate session = (await Must(repository.LoadAsync(
            recent[0].Id, CancellationToken.None)))!;
        ProcessingAttempt failedAttempt = session.Attempts.Single(attempt =>
            attempt.Step == StepKind.BackgroundRemoval && attempt.Status == AttemptStatus.Failed);
        Revision inputRevision = session.Revisions.Single(revision =>
            revision.Id == failedAttempt.InputRevisionId);
        WorkspaceFileRef workingCopy = WorkspaceFileRef.Create(
            $"{session.Session.Workspace.RelativePath}/Working/{failedAttempt.Id}/{inputRevision.File.FileName}",
            WorkspaceArea.Working);
        WorkspaceFileRef output = WorkspaceFileRef.Create(
            $"{session.Session.Workspace.RelativePath}/Working/{failedAttempt.Id}/" +
            "FIX-FINE-HAIR-001-REDO_CUTOUT.png",
            WorkspaceArea.Working);
        workingCopy.FileName.ShouldBe("FIX-FINE-HAIR-001.jpg");
        File.Exists(workspace.ResolveAbsolute(output)).ShouldBeFalse(
            "The guarded export destination must be unused.");

        MeituAutomationOptions options = new();
        PresetMeituBaselineProvider baselines = new(preset, presetHash);
        Win32ExternalAppWindowLocator locator = new();
        UiaElementProvider elements = new();
        Win32ScopedInputSink input = new(locator);
        GdiWindowEvidenceSink evidence = new(evidenceDirectory, TimeProvider.System);
        GuardedMeituUiDriver driver = new(
            locator, elements, input, evidence, baselines, options, TimeProvider.System);
        ProductionMeituProcessor adapter = new(
            baselines, locator, driver, workspace, new WicFileInspector(),
            new WicMeituTransparencyInspector(), new FileSystemMeituOutputProbe(),
            options, TimeProvider.System);

        FileFacts sourceBefore = await Must(adapter.InspectManagedFileAsync(
            workingCopy, CancellationToken.None));
        sourceBefore.Sha256.ShouldBe(inputRevision.Sha256);

        MeituExportedOutput exported;
        MeituReadiness ready;
        MeituBackgroundRemovalOutcome processed;
        await using (WorkstationAutomationLeaseScope automationLease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync())
        {
            MeituBaseline baseline = await VerifiedBaselineAsync(baselines);
            IReadOnlyList<ExternalProcessRef> processes = await MustValue(
                locator.FindProcessesByExecutable(baseline.ExecutablePath));
            processes.Count.ShouldBe(1,
                "Exactly one accepted already-running Meitu process is required; none is launched or chosen.");

            IReadOnlyList<ExternalWindowRef> windows = await MustValue(
                locator.FindTopLevelWindows(processes[0]));
            List<MeituTarget> candidates = [];
            List<string> observed = [];
            foreach (ExternalWindowRef window in windows.Where(window => window.IsVisible))
            {
                if (window.OwningProcessId != processes[0].ProcessId)
                {
                    observed.Add($"{window.Handle}:wrong-owner");
                    continue;
                }

                MeituTarget target = new(processes[0], window);
                OperationResult<MeituStateSnapshot> inspection = await driver.InspectStateAsync(
                    target, workingCopy.FileName, CancellationToken.None);
                if (inspection.IsFailure)
                {
                    observed.Add($"{window.Handle}:inspection-{inspection.Failure.Code}");
                    continue;
                }

                MeituDocumentSurfacePhase phase =
                    MeituDocumentIdentityRule.ClassifyIdentityProbeSurface(
                        baseline, inspection.Value.Observation);
                observed.Add($"{window.Handle}:{phase}");
                if (phase == MeituDocumentSurfacePhase.LoadedEditor)
                {
                    candidates.Add(target);
                }
            }

            candidates.Count.ShouldBe(1,
                "Expected one signed ordinary editor retaining the exact redo input; observed " +
                string.Join(" | ", observed));

            Console.WriteLine("Retained editor candidates: " + string.Join(" | ", observed));
            OperationResult<MeituBackgroundRemovalOutcome> processing =
                await driver.RunBackgroundRemovalAsync(
                candidates[0],
                workingCopy.FileName,
                BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                InertAutomationStopSignal.Instance,
                CancellationToken.None);
            if (processing.IsFailure)
            {
                OperationResult<EvidenceRef> capture = driver.CaptureEvidence(
                    candidates[0], "loaded-redo-action-refused");
                Console.WriteLine("Processing refusal: " + processing.Failure);
                Console.WriteLine("Processing refusal context: " + JsonSerializer.Serialize(
                    processing.Failure.Context));
                Console.WriteLine(capture.IsSuccess
                    ? "Processing refusal evidence: " + capture.Value.AbsolutePath
                    : "Processing refusal evidence capture failed: " + capture.Failure);
            }

            processing.IsSuccess.ShouldBeTrue(
                processing.IsFailure ? processing.Failure.ToString() : string.Empty);
            processed = processing.Value;
            processed.ObservedDocumentIdentity.ShouldBe("FIX-FINE-HAIR-001_副本");

            exported = await Must(adapter.ExportBackgroundRemovalResultAsync(
                processed,
                workingCopy,
                sourceBefore,
                output,
                InertAutomationStopSignal.Instance,
                CancellationToken.None));

            await Must(driver.DismissExportResultSurfaceAsync(
                processed.Target, CancellationToken.None));
            await Must(driver.CloseDocumentAsync(processed.Target, CancellationToken.None));
            ready = await Must(adapter.EnsureReadyAsync(CancellationToken.None));
            ready.State.IsSafeStartingState.ShouldBeTrue();
        }

        exported.Facts.Format.ShouldBe(ImageFormat.Png);
        exported.Facts.PixelWidth.ShouldBe(sourceBefore.PixelWidth);
        exported.Facts.PixelHeight.ShouldBe(sourceBefore.PixelHeight);
        exported.Transparency.ShouldNotBeNull();
        MeituTransparencyRule.Validate(exported.Transparency!).IsSuccess.ShouldBeTrue();

        await Must(sessions.ExecuteAsync(
            session.Session.Id,
            new WorkflowCommand.HandOff(
                StepKind.BackgroundRemoval,
                "Exact retained loaded redo input processed, exported and validated for fresh review."),
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None));
        SessionView submitted = await Must(sessions.ExecuteAsync(
            session.Session.Id,
            new WorkflowCommand.SubmitManualResult(
                StepKind.BackgroundRemoval, workspace.ResolveAbsolute(exported.File)),
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None));
        submitted.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);

        SessionAggregate final = (await Must(repository.LoadAsync(
            session.Session.Id, CancellationToken.None)))!;
        Revision manual = final.Revisions.Single(revision =>
            revision.Operation == OperationKind.ManualResultImport &&
            revision.Sha256 == exported.Facts.Sha256);
        manual.ReviewState.ShouldBe(ReviewState.NotReviewed);
        final.Attempts.Single(attempt => attempt.Id == failedAttempt.Id)
            .Status.ShouldBe(AttemptStatus.Failed);
        final.Attempts.Single(attempt => attempt.OutputRevisionId == manual.Id)
            .Operation.ShouldBe(OperationKind.ManualResultImport);
        (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();

        await using (WorkstationAutomationLeaseScope releasedProbe =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync())
        {
            // Acquiring after the operation independently proves the canonical lease was released.
        }

        string receipt = Path.Combine(root, "loaded-redo-receipt.json");
        await File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(new
        {
            SessionId = session.Session.Id.ToString(),
            FailedStagingAttemptId = failedAttempt.Id.ToString(),
            ManualResultRevisionId = manual.Id.ToString(),
            WorkingFile = workingCopy.RelativePath,
            SourceSha256 = sourceBefore.Sha256.ToString(),
            OutputFile = exported.File.RelativePath,
            OutputSha256 = exported.Facts.Sha256.ToString(),
            exported.Facts.ByteLength,
            exported.Facts.PixelWidth,
            exported.Facts.PixelHeight,
            Transparency = exported.Transparency,
            processed.ObservedDocumentIdentity,
            BusyObserved = true,
            CompletionObserved = true,
            GuardedExport = true,
            ReviewState = manual.ReviewState.ToString(),
            MeituReadiness = ready.State.State.ToString(),
            SharedLeaseReleased = true,
            EnhancementInvoked = false,
            PresetPublicationAllowed = false,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(receipt);
    }

    private static async Task<MeituBaseline> VerifiedBaselineAsync(
        PresetMeituBaselineProvider baselines)
    {
        OperationResult<MeituBaseline> verified = await Task.FromResult(baselines.GetVerifiedBaseline());
        verified.IsSuccess.ShouldBeTrue(verified.IsFailure ? verified.Failure.ToString() : string.Empty);
        MeituBaseline baseline = verified.Value;
        File.Exists(baseline.ExecutablePath).ShouldBeTrue();
        using FileStream stream = new(
            baseline.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: false);
        Sha256 actual = Sha256.FromBytes(SHA256.HashData(stream));
        actual.ShouldBe(baseline.ExecutableSha256,
            "The live binary must remain the exact preset-bound Meitu executable.");
        return baseline;
    }

    private static Task<T> MustValue<T>(OperationResult<T> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return Task.FromResult(result.Value);
    }

    private static async Task<T> Must<T>(Task<OperationResult<T>> pending)
    {
        OperationResult<T> result = await pending;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Required loaded-redo association '{name}' was not supplied.");

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
