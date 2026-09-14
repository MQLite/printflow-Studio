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
/// Recovers the one exact fine-hair cutout retained after the redo completed but its terminal
/// identity confirmation failed. It neither opens another document nor invokes processing: one
/// signed ordinary editor, the exact Save-default identity and the preserved before/result
/// captures are mandatory before guarded export.
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
        string beforeEvidence = Path.Combine(
            root, "evidence", "20260914T230940Z_open-unsettled_61F76.png");
        string resultEvidence = Path.Combine(
            evidenceDirectory, "20260914T233151Z_loaded-redo-action-refused_61F76.png");

        Directory.Exists(workspaceRoot).ShouldBeTrue($"Missing retained workspace '{workspaceRoot}'.");
        File.Exists(databasePath).ShouldBeTrue($"Missing retained database '{databasePath}'.");
        File.Exists(beforeEvidence).ShouldBeTrue("The preserved pre-processing editor capture is required.");
        File.Exists(resultEvidence).ShouldBeTrue("The preserved current cutout capture is required.");
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
        string observedDocumentIdentity;
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
                "Expected one signed ordinary editor retaining the exact observed cutout; observed " +
                string.Join(" | ", observed));

            Console.WriteLine("Retained editor candidates: " + string.Join(" | ", observed));
            MeituStateSnapshot identity = await Must(driver.ConfirmWorkingCopyIdentityAsync(
                candidates[0], workingCopy.FileName, CancellationToken.None));
            observedDocumentIdentity = identity.Observation.ObservedDocumentIdentity!;
            observedDocumentIdentity.ShouldBe("FIX-FINE-HAIR-001_副本");

            MeituCutoutOutputRule.ValidateDestination(workingCopy, output).IsSuccess.ShouldBeTrue();
            string destination = workspace.ResolveAbsolute(output);
            MeituExportEvidence exportEvidence = await Must(driver.ExportResultAsync(
                candidates[0],
                workingCopy.FileName,
                observedDocumentIdentity,
                destination,
                InertAutomationStopSignal.Instance,
                CancellationToken.None));
            int stabilityObservations = await AwaitSettledOutputAsync(
                destination, options, CancellationToken.None);
            FileFacts outputFacts = await Must(adapter.InspectManagedFileAsync(
                output, CancellationToken.None));
            MeituTransparencyFacts transparency = await Must(
                new WicMeituTransparencyInspector().InspectAsync(
                    destination, CancellationToken.None));
            MeituCutoutOutputRule.Validate(
                sourceBefore, outputFacts, transparency).IsSuccess.ShouldBeTrue();
            FileFacts sourceAfter = await Must(adapter.InspectManagedFileAsync(
                workingCopy, CancellationToken.None));
            MeituEnhancementOutputRule.ConfirmSourceUnchanged(
                sourceBefore, sourceAfter, workingCopy.FileName).IsSuccess.ShouldBeTrue();
            exported = new MeituExportedOutput(
                output, outputFacts, exportEvidence, stabilityObservations, transparency);

            await Must(driver.DismissExportResultSurfaceAsync(
                candidates[0], CancellationToken.None));
            await Must(driver.CloseDocumentAsync(candidates[0], CancellationToken.None));
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
                "Exact retained redo cutout observed, guarded-exported and validated for fresh review."),
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
            ObservedDocumentIdentity = observedDocumentIdentity,
            BeforeProcessingEvidence = beforeEvidence,
            BeforeProcessingEvidenceSha256 = FileSha256(beforeEvidence).ToString(),
            ResultObservationEvidence = resultEvidence,
            ResultObservationEvidenceSha256 = FileSha256(resultEvidence).ToString(),
            ExternalProcessingAndResultObserved = true,
            BusyObservationPersisted = false,
            ProcessingInvokedDuringRecovery = false,
            GuardedExport = true,
            ReviewState = manual.ReviewState.ToString(),
            MeituReadiness = ready.State.State.ToString(),
            SharedLeaseReleased = true,
            EnhancementInvoked = false,
            PresetPublicationAllowed = false,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(receipt);
    }

    private static async Task<int> AwaitSettledOutputAsync(
        string destination,
        MeituAutomationOptions options,
        CancellationToken cancellationToken)
    {
        FileSystemMeituOutputProbe probe = new();
        List<MeituOutputObservation> observations = [];
        DateTimeOffset deadline = DateTimeOffset.UtcNow + options.OutputStabilityTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observations.Add(probe.Probe(destination));
            if (MeituOutputStabilityRule.IsSettled(observations))
            {
                return observations.Count;
            }

            DateTimeOffset.UtcNow.ShouldBeLessThan(
                deadline,
                "The controlled output did not appear and settle; no Revision may be created.");
            await Task.Delay(options.OutputPollInterval, cancellationToken);
        }
    }

    private static Sha256 FileSha256(string path)
    {
        using FileStream stream = new(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: false);
        return Sha256.FromBytes(SHA256.HashData(stream));
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
