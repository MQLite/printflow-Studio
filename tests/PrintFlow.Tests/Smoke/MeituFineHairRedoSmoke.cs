using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// Opt-in, one-object redo of the fine-hair cutout when the standard regression wrapper is
/// blocked by an unrelated Photoshop readiness probe. The live Meitu work remains outside the
/// workflow gate and is registered truthfully as a manual-result import requiring fresh review.
/// </summary>
public sealed class MeituFineHairRedoSmoke
{
    [Fact]
    public async Task Redo_export_and_register_the_fine_hair_cutout_for_review()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_MEITU_FINE_HAIR_REDO") != "1")
        {
            return;
        }

        string root = Required("PRINTFLOW_MEITU_FINE_HAIR_REDO_ROOT");
        string source = Required("PRINTFLOW_MEITU_FINE_HAIR_SOURCE");
        string preset = Required("PRINTFLOW_MEITU_SMOKE_PRESET_MANIFEST");
        Sha256 presetHash = Sha256.Parse(Required("PRINTFLOW_MEITU_SMOKE_PRESET_SHA256"));

        if (Directory.Exists(root))
        {
            throw new InvalidOperationException($"Redo root already exists: '{root}'. Nothing was run.");
        }

        Directory.CreateDirectory(root);
        string workspaceRoot = Path.Combine(root, "workspace");
        string databasePath = Path.Combine(root, "redo.db");
        string evidenceDirectory = Path.Combine(root, "evidence");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(evidenceDirectory);

        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            Path.Combine(RepositoryRoot(), "appsettings.json"));
        SqliteConnectionFactory connections = new(databasePath);
        using (var connection = connections.Open())
        {
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        }

        using ServiceProvider provider = ServiceRegistration.BuildServiceProvider(
            configuration,
            workspaceRoot,
            connections,
            services => services.AddSingleton<IMeituProcessor>(serviceProvider =>
            {
                FakeMeituProcessor fake = new(serviceProvider.GetRequiredService<IWorkspace>());
                fake.SetScenario(FakeAdapterScenario.Timeout);
                return fake;
            }));
        ISessionService sessions = provider.GetRequiredService<ISessionService>();
        ISessionRepository repository = provider.GetRequiredService<ISessionRepository>();
        IWorkspace workspace = provider.GetRequiredService<IWorkspace>();

        SessionId sessionId = (await Must(sessions.ImportAsync(
            WorkflowType.PrepareAsset,
            source,
            "FIX-FINE-HAIR-001-REDO",
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None))).Id;
        await Must(sessions.ExecuteAsync(
            sessionId,
            new WorkflowCommand.ConfirmOriginal("Fresh redo of the confirmed fine-hair object."),
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None));
        await Must(sessions.ExecuteAsync(
            sessionId,
            new WorkflowCommand.Skip(
                StepKind.Enhancement,
                "Enhancement remains out of scope; redo only the fine-hair cutout."),
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None));

        SessionAggregate prepared = (await repository.LoadAsync(sessionId, CancellationToken.None)).Value!;
        (RevisionId Id, Sha256 Sha256) upstream =
            prepared.ToSnapshot().UpstreamResultOf(StepKind.BackgroundRemoval)!.Value;
        await Must(sessions.ExecuteAsync(
            sessionId,
            new WorkflowCommand.SetBackgroundRemovalDecision(
                BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                upstream.Id,
                upstream.Sha256),
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None));

        OperationResult<SessionView> stagedFailure = await sessions.ExecuteAsync(
            sessionId,
            new WorkflowCommand.StartStep(StepKind.BackgroundRemoval),
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None);
        stagedFailure.IsFailure.ShouldBeTrue();

        SessionAggregate staged = (await repository.LoadAsync(sessionId, CancellationToken.None)).Value!;
        ProcessingAttempt failedAttempt = staged.Attempts.Last(attempt =>
            attempt.Step == StepKind.BackgroundRemoval && attempt.Status == AttemptStatus.Failed);
        Revision inputRevision = staged.Revisions.Single(revision => revision.Id == failedAttempt.InputRevisionId);
        WorkspaceFileRef workingCopy = WorkspaceFileRef.Create(
            $"{staged.Session.Workspace.RelativePath}/Working/{failedAttempt.Id}/{inputRevision.File.FileName}",
            WorkspaceArea.Working);
        WorkspaceFileRef output = WorkspaceFileRef.Create(
            $"{staged.Session.Workspace.RelativePath}/Working/{failedAttempt.Id}/FIX-FINE-HAIR-001-REDO_CUTOUT.png",
            WorkspaceArea.Working);

        IMeituProcessor processor = MeituAutomationComposition.CreateProductionProcessor(
            preset, presetHash, workspace, evidenceDirectory, TimeProvider.System);
        OperationResult<AdapterOutput> processed;
        await using (WorkstationAutomationLeaseScope automationLease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync())
        {
            processed = await processor.ProcessAsync(
                new MeituRequest(
                    workingCopy,
                    MeituOperation.RemoveBackground,
                    BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                    staged.Session.Workspace,
                    output),
                CancellationToken.None);
        }

        processed.IsSuccess.ShouldBeTrue(processed.IsFailure ? processed.Failure.ToString() : string.Empty);

        string outputPath = workspace.ResolveAbsolute(processed.Value.ProducedFile);
        WicFileInspector inspector = new();
        FileFacts outputFacts = (await inspector.InspectAsync(outputPath, CancellationToken.None)).Value;
        MeituTransparencyFacts transparency = (await new WicMeituTransparencyInspector()
            .InspectAsync(outputPath, CancellationToken.None)).Value;
        outputFacts.Format.ShouldBe(ImageFormat.Png);
        outputFacts.PixelWidth.ShouldBe(inputRevision.Facts.PixelWidth);
        outputFacts.PixelHeight.ShouldBe(inputRevision.Facts.PixelHeight);
        MeituTransparencyRule.Validate(transparency).IsSuccess.ShouldBeTrue();

        await Must(sessions.ExecuteAsync(
            sessionId,
            new WorkflowCommand.HandOff(
                StepKind.BackgroundRemoval,
                "Fresh supervised Meitu redo exported and validated; import exact result for review."),
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None));
        SessionView submitted = await Must(sessions.ExecuteAsync(
            sessionId,
            new WorkflowCommand.SubmitManualResult(StepKind.BackgroundRemoval, outputPath),
            "PF-FIX-MEITU-CONFIRM",
            CancellationToken.None));
        submitted.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);

        SessionAggregate final = (await repository.LoadAsync(sessionId, CancellationToken.None)).Value!;
        Revision manual = final.Revisions.Single(revision =>
            revision.Operation == OperationKind.ManualResultImport &&
            revision.Sha256 == outputFacts.Sha256);
        manual.ReviewState.ShouldBe(ReviewState.NotReviewed);
        final.Attempts.Single(attempt => attempt.Id == failedAttempt.Id).Status.ShouldBe(AttemptStatus.Failed);
        final.Attempts.Single(attempt => attempt.OutputRevisionId == manual.Id)
            .Operation.ShouldBe(OperationKind.ManualResultImport);
        (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();

        string receipt = Path.Combine(root, "redo-receipt.json");
        await File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(new
        {
            SessionId = sessionId.ToString(),
            FailedStagingAttemptId = failedAttempt.Id.ToString(),
            ManualResultRevisionId = manual.Id.ToString(),
            SourcePath = source,
            SourceSha256 = inputRevision.Sha256.ToString(),
            OutputPath = outputPath,
            OutputSha256 = outputFacts.Sha256.ToString(),
            outputFacts.ByteLength,
            outputFacts.PixelWidth,
            outputFacts.PixelHeight,
            Transparency = transparency,
            ReviewState = manual.ReviewState.ToString(),
            MachineCompletionAndGuardedExport = true,
            ProcessingInvoked = true,
            EnhancementInvoked = false,
            PresetPublicationAllowed = false,
            AdapterNotes = processed.Value.AdapterNotes,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(receipt);
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
            : throw new InvalidOperationException($"Required redo association '{name}' was not supplied.");

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
