using System.IO;
using System.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class PdfPreparationWorkflowTests
{
    internal sealed class AllowedGate : IEnvironmentGate
    {
        public OperationResult<PrintFlow.Domain.Results.Unit> Verify(AdapterExecutionMode mode) => OperationResult.Ok();
    }

    internal static ISessionService Service(SessionServiceHarness h, IPdfPreparationProcessor pdf, IEnvironmentGate? gate = null) =>
        new SessionService(WorkflowEngine.Instance, h.Repository, h.FileWorkspace, h.RecycleBin, h.FileInspector,
            h.FakeMeitu, h.FakePhotoshop, h.Trim, h.ManualCrop, h.Preset, gate ?? new AllowedGate(), SystemIdGenerator.Instance, h.Clock, pdf);

    [Theory]
    [InlineData("two")]
    [InlineData("compressed-two")]
    [InlineData("incremental-two")]
    public async Task Multipage_is_counted_before_any_page_selection_or_render_and_only_source_revision_exists(string fixture)
    {
        using SessionServiceHarness h = new();
        var authority = new RecordingAuthority();
        var service = Service(h, new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector) { Authority = authority });
        byte[] bytes = PdfFixtures.Read(fixture);
        string source = h.Workspace.CreateSourceFile(fixture + ".pdf", bytes);
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", default);
        imported.IsSuccess.ShouldBeTrue();
        var result = await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", default);
        result.Failure.Code.ShouldBe(FailureCode.PdfMultiplePages);
        result.Failure.IsRetryable.ShouldBeFalse();
        authority.Selections.ShouldBe(0);
        authority.Renders.ShouldBe(0);
        var stored = (await h.Repository.LoadAsync(imported.Value.Id, default)).Value!;
        var attempt = stored.Attempts.Single(a => a.Operation == OperationKind.PreparePdf);
        attempt.PdfInspection!.PageCount.ShouldBe(2);
        attempt.PdfInspection.PreparedPageNumber.ShouldBeNull();
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        attempt.OutputRevisionId.ShouldBeNull();
        stored.Revisions.Count.ShouldBe(1);
        stored.Revisions.Single().Facts.Format.ShouldBe(ImageFormat.Pdf);
        Directory.GetFiles(h.Workspace.Root, "*.png", SearchOption.AllDirectories).ShouldBeEmpty();
        stored.Attempts.ShouldNotContain(a => a.Operation == OperationKind.PhotoshopOutput);
        File.ReadAllBytes(source).ShouldBe(bytes);
        (await h.Repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
        (await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.Retry(StepKind.OriginalConfirmation), "qa", default))
            .Failure.Code.ShouldBe(FailureCode.PdfMultiplePages);
    }

    [Theory]
    [InlineData("single", 600, 300, 0, true)]
    [InlineData("rotated", 300, 600, 90, true)]
    [InlineData("crop", 450, 225, 0, true)]
    [InlineData("opaque", 600, 300, 0, false)]
    [InlineData("fractional", 601, 300, 0, true)]
    public async Task Real_windows_page_geometry_matches_validated_raster_and_persisted_provenance(
        string fixture, int width, int height, int rotation, bool transparent)
    {
        using SessionServiceHarness h = new();
        var authority = new RecordingAuthority();
        var processor = new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector) { Authority = authority };
        var service = Service(h, processor);
        byte[] bytes = PdfFixtures.Read(fixture);
        string source = h.Workspace.CreateSourceFile(fixture + ".pdf", bytes);
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", default);
        (await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.ConfirmOriginal(), "qa", default)).IsFailure.ShouldBeTrue();
        var prepared = await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", default);
        prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Failure.ToString() : "");
        var stored = (await h.Repository.LoadAsync(imported.Value.Id, default)).Value!;
        var root = stored.Revisions.Single(r => r.IsRoot);
        var raster = stored.Revisions.Single(r => r.Operation == OperationKind.PreparePdf);
        var facts = stored.Attempts.Single(a => a.Operation == OperationKind.PreparePdf).PdfInspection!;
        facts.IsPreparedSinglePage.ShouldBeTrue();
        facts.RotationDegrees.ShouldBe(rotation);
        facts.RequestedRasterDpi.ShouldBe(300);
        facts.HasTransparency.ShouldBe(transparent);
        facts.PageCount.ShouldBe(1); facts.PreparedPageNumber.ShouldBe(1);
        raster.Facts.PixelWidth.ShouldBe(width); raster.Facts.PixelHeight.ShouldBe(height);
        raster.Facts.DpiX!.Value.ShouldBe(300, .02);
        raster.SourceRevisionId.ShouldBe(root.Id);
        raster.Sha256.ShouldNotBe(root.Sha256);
        stored.Snapshot!.RootRevisionId.ShouldBe(root.Id);
        File.ReadAllBytes(source).ShouldBe(bytes);
        File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(root.File)).ShouldBe(bytes);
        stored.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        prepared.Value.HasBeforeAfterComparison.ShouldBeFalse();
        (await h.Previews.GetPreviewAsync(imported.Value.Id, raster.Id, default)).IsSuccess.ShouldBeTrue();
        (await h.Previews.GetPreviewAsync(imported.Value.Id, root.Id, default)).IsFailure.ShouldBeTrue();

        var restarted = Service(h, processor);
        var resumed = await restarted.LoadAsync(imported.Value.Id, default);
        resumed.Value.PdfInspection.ShouldBe(facts);
        resumed.Value.CurrentArtefact!.RevisionId.ShouldBe(raster.Id);
        resumed.Value.CurrentArtefact.Sha256.ShouldBe(raster.Sha256);
        (await restarted.ExecuteAsync(imported.Value.Id, new WorkflowCommand.Approve(StepKind.OriginalConfirmation, raster.Sha256), "qa", default))
            .IsSuccess.ShouldBeTrue();
        (await restarted.ExecuteAsync(imported.Value.Id, new WorkflowCommand.SetPrintDimensions(PrintDimensions.FromMillimetres(50, 25, SizePreset.Custom)), "qa", default))
            .IsSuccess.ShouldBeTrue();
        (await h.Repository.LoadAsync(imported.Value.Id, default)).Value!.Session.PrintPreparationPlan!.SourceRevisionId.ShouldBe(raster.Id);
        authority.Opens.ShouldBe(1); authority.Renders.ShouldBe(1);
        (await h.Repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
    }

    [Theory]
    [InlineData(WorkflowType.PrepareAsset)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    public async Task Approved_pdf_raster_reaches_existing_trim(WorkflowType workflow)
    {
        using SessionServiceHarness h = new();
        var service = Service(h, new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector));
        var source = h.Workspace.CreateSourceFile("single.pdf", PdfFixtures.Read("single"));
        var imported = await service.ImportAsync(workflow, source, null, "qa", default);
        var id = imported.Value.Id;
        var prepared = await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", default);
        prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Failure.ToString() : "");
        var raster = prepared.Value.CurrentArtefact!;
        (await service.ExecuteAsync(id, new WorkflowCommand.Approve(StepKind.OriginalConfirmation, raster.Sha256), "qa", default)).IsSuccess.ShouldBeTrue();
        (await service.ExecuteAsync(id, new WorkflowCommand.Skip(StepKind.Enhancement, "ready"), "qa", default)).IsSuccess.ShouldBeTrue();
        (await service.ExecuteAsync(id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval, "ready"), "qa", default)).IsSuccess.ShouldBeTrue();
        var trimmed = await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim), "qa", default);
        trimmed.IsSuccess.ShouldBeTrue(trimmed.IsFailure ? trimmed.Failure.ToString() : "");
        trimmed.Value.CurrentArtefact!.SourceRevisionId.ShouldBe(raster.RevisionId);
    }

    [Theory]
    [InlineData("corrupt", FailureCode.PdfUnreadable)]
    [InlineData("zero", FailureCode.PdfUnreadable)]
    [InlineData("encrypted", FailureCode.PdfEncrypted)]
    public async Task Unreadable_or_secret_protected_source_is_refused_at_preparation(string fixture, FailureCode failure)
    {
        using SessionServiceHarness h = new();
        var authority = new RecordingAuthority();
        var service = Service(h, new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector) { Authority = authority });
        var bytes = PdfFixtures.Read(fixture);
        var source = h.Workspace.CreateSourceFile(fixture + ".pdf", bytes);
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", default);
        var result = await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", default);
        result.Failure.Code.ShouldBe(failure);
        var stored = (await h.Repository.LoadAsync(imported.Value.Id, default)).Value!;
        stored.Revisions.Count.ShouldBe(1);
        var attempt = stored.Attempts.Single(a => a.Operation == OperationKind.PreparePdf);
        attempt.PdfInspection.ShouldNotBeNull();
        if (fixture == "encrypted") attempt.PdfInspection.IsEncrypted.ShouldBe(true);
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        authority.Selections.ShouldBe(0); authority.Renders.ShouldBe(0);
        File.ReadAllBytes(source).ShouldBe(bytes);
        (await h.Repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("missing")]
    [InlineData("partial")]
    [InlineData("dimensions")]
    [InlineData("format")]
    [InlineData("alpha")]
    [InlineData("unsettled")]
    public async Task Renderer_or_validation_failure_retains_attempt_and_retry_creates_independent_output(string fault)
    {
        using SessionServiceHarness h = new();
        var authority = new RecordingAuthority { Fault = fault };
        IFileInspector outputInspector = fault == "unsettled" ? new ChangingInspector(h.FileInspector) : h.FileInspector;
        var service = Service(h, new WindowsPdfPreparationProcessor(h.FileWorkspace, outputInspector) { Authority = authority });
        var bytes = PdfFixtures.Read("single");
        var source = h.Workspace.CreateSourceFile("single.pdf", bytes);
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", default);
        var id = imported.Value.Id;
        var failed = await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", default);
        failed.Failure.Code.ShouldBe(FailureCode.PdfPreparationFailed);
        var stored = (await h.Repository.LoadAsync(id, default)).Value!;
        stored.Revisions.Count.ShouldBe(1);
        var first = stored.Attempts.Single(a => a.Operation == OperationKind.PreparePdf);
        first.Status.ShouldBe(AttemptStatus.Failed); first.OutputRevisionId.ShouldBeNull();
        first.PdfInspection!.PageCount.ShouldBe(1);
        byte[]? partial = File.Exists(authority.Paths[0]) ? File.ReadAllBytes(authority.Paths[0]) : null;
        authority.Fault = null;
        (await service.ExecuteAsync(id, new WorkflowCommand.Retry(StepKind.OriginalConfirmation), "qa", default)).IsSuccess.ShouldBeTrue();
        var result = await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", default);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
        stored = (await h.Repository.LoadAsync(id, default)).Value!;
        var second = stored.Attempts.Single(a => a.Operation == OperationKind.PreparePdf && a.Status == AttemptStatus.Succeeded);
        second.RetrySequence.ShouldBe(1); second.RetryOfAttemptId.ShouldBe(first.Id);
        second.Id.ShouldNotBe(first.Id);
        second.OutputRevisionId.ShouldBe(result.Value.CurrentArtefact!.RevisionId);
        Path.GetDirectoryName(authority.Paths[0]).ShouldNotBe(Path.GetDirectoryName(authority.Paths[1]));
        if (partial is not null) File.ReadAllBytes(authority.Paths[0]).ShouldBe(partial);
        File.ReadAllBytes(source).ShouldBe(bytes);
        (await h.Repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
    }

    [Theory]
    [InlineData(AutomationStopMode.StopOperation)]
    [InlineData(AutomationStopMode.TakeOver)]
    public async Task Cancellation_after_count_prevents_render_and_releases_lock(AutomationStopMode mode)
    {
        using SessionServiceHarness h = new();
        var authority = new RecordingAuthority();
        var service = Service(h, new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector) { Authority = authority });
        var source = h.Workspace.CreateSourceFile("single.pdf", PdfFixtures.Read("single"));
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", default);
        authority.OnSelection = () => service.RequestStop(imported.Value.Id, mode).IsSuccess.ShouldBeTrue();
        await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", default);
        var stored = (await h.Repository.LoadAsync(imported.Value.Id, default)).Value!;
        authority.Renders.ShouldBe(0);
        stored.Revisions.Count.ShouldBe(1);
        var attempt = stored.Attempts.Single(a => a.Operation == OperationKind.PreparePdf);
        attempt.Status.ShouldBe(AttemptStatus.Cancelled);
        attempt.PdfInspection!.PageCount.ShouldBe(1);
        (await h.Repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Advertised_pdf_has_explicit_preparation_and_real_production_gate()
    {
        new ResourceManager("PrintFlow.App.Resources.Strings", typeof(HomeViewModel).Assembly)
            .GetString("Home_ImportFilter")!.ShouldContain("*.pdf");
        using SessionServiceHarness h = new();
        var authority = new RecordingAuthority();
        var service = Service(h, new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector) { Authority = authority }, h.EnvironmentGate);
        var source = h.Workspace.CreateSourceFile("single.pdf", PdfFixtures.Read("single"));
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", default);
        authority.Opens.ShouldBe(0);
        imported.Value.AvailableCommands.ShouldContain(CommandKind.StartStep);
        (await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", default))
            .Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        authority.Opens.ShouldBe(0);
        (await h.Repository.LoadAsync(imported.Value.Id, default)).Value!.Attempts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Output_changed_after_adapter_validation_is_not_adopted()
    {
        using SessionServiceHarness h = new();
        var inner = new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector);
        var service = Service(h, new MutatingProcessor(inner, h.FileWorkspace));
        var source = h.Workspace.CreateSourceFile("single.pdf", PdfFixtures.Read("single"));
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", default);
        var result = await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", default);
        result.Failure.Code.ShouldBe(FailureCode.PdfPreparationFailed);
        (await h.Repository.LoadAsync(imported.Value.Id, default)).Value!.Revisions.Count.ShouldBe(1);
        (await h.Repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancellation_token_after_inspection_closes_attempt_and_source_stream_denies_writes()
    {
        using SessionServiceHarness h = new();
        using CancellationTokenSource cancellation = new();
        var authority = new RecordingAuthority();
        var service = Service(h, new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector) { Authority = authority });
        var source = h.Workspace.CreateSourceFile("single.pdf", PdfFixtures.Read("single"));
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", default);
        authority.OnSelection = () =>
        {
            string working = Directory.GetFiles(h.Workspace.Root, "*.pdf", SearchOption.AllDirectories)
                .Single(p => p.Contains(Path.DirectorySeparatorChar + "Working" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            Should.Throw<IOException>(() => { using var writer = new FileStream(working, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
            cancellation.Cancel();
        };
        var result = await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", cancellation.Token);
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        var stored = (await h.Repository.LoadAsync(imported.Value.Id, default)).Value!;
        stored.Attempts.Single(a => a.Operation == OperationKind.PreparePdf).PdfInspection!.PageCount.ShouldBe(1);
        stored.Revisions.Count.ShouldBe(1); authority.Renders.ShouldBe(0);
        (await h.Repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
    }

    [Theory]
    [InlineData("en-US", "single", "Prepare PDF", "300 PPI")]
    [InlineData("zh-CN", "single", "准备 PDF", "300 PPI")]
    [InlineData("en-US", "two", "Prepare PDF", "multiple pages")]
    [InlineData("zh-CN", "two", "准备 PDF", "多页")]
    public async Task Home_and_review_ui_offer_truthful_pdf_preparation(string culture, string fixture, string label, string notice)
    {
        using SessionServiceHarness h = new();
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var authority = new RecordingAuthority();
            var service = Service(h, new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector) { Authority = authority });
            var navigation = new RecordingNavigation();
            var picker = new StubFilePicker(h.Workspace.CreateSourceFile(fixture + ".pdf", PdfFixtures.Read(fixture)));
            var home = new HomeViewModel(service, navigation, picker, new PrintFlow.App.Startup.StartupStatusAccessor());
            await home.ChooseFileCommand.ExecuteAsync(null);
            var imported = navigation.WorkflowSelectionFor.ShouldNotBeNull();
            authority.Opens.ShouldBe(0);
            var chosen = await service.ExecuteAsync(imported.Id, new WorkflowCommand.SelectWorkflow(WorkflowType.GeneratePrintTiff), "qa", default);
            var screen = new SessionViewModel(service, h.Previews, h.TiffReviews, navigation);
            screen.Open(chosen.Value);
            screen.CanConfirmOriginal.ShouldBeFalse();
            screen.RunStepLabel.ShouldContain(label);
            screen.HasPdfSource.ShouldBeTrue();
            await screen.RunStepCommand.ExecuteAsync(null);
            if (fixture == "single")
            {
                screen.CanApprove.ShouldBeTrue();
                screen.PdfSourceNotice.ShouldContain(notice);
                screen.PdfSourceNotice.ShouldContain("600");
                await screen.ApproveCommand.ExecuteAsync(null);
                screen.PdfSourceNotice.ShouldBeEmpty("the full-page preparation notice must not describe later trimmed or resized results");
                (await h.Repository.LoadAsync(imported.Id, default)).Value!.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
            }
            else
            {
                screen.CanRetry.ShouldBeFalse(); screen.CanApprove.ShouldBeFalse();
                new ResourceManager("PrintFlow.App.Resources.Strings", typeof(HomeViewModel).Assembly)
                    .GetString("Failure_PdfMultiplePages")!.ShouldContain(notice);
            }
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    private sealed class ChangingInspector(IFileInspector inner) : IFileInspector
    {
        private bool _changed;
        public async Task<OperationResult<FileFacts>> InspectAsync(string absolutePath, CancellationToken cancellationToken)
        {
            var result = await inner.InspectAsync(absolutePath, cancellationToken);
            if (!_changed && result.IsSuccess)
            {
                _changed = true;
                using var file = new FileStream(absolutePath, FileMode.Append); file.WriteByte(0);
            }
            return result;
        }
    }

    private sealed class MutatingProcessor(IPdfPreparationProcessor inner, IWorkspace workspace) : IPdfPreparationProcessor
    {
        public string AdapterId => inner.AdapterId;
        public AdapterExecutionMode Mode => inner.Mode;
        public async Task<OperationResult<AdapterOutput>> PrepareAsync(PdfPreparationRequest request, CancellationToken cancellationToken)
        {
            var result = await inner.PrepareAsync(request, cancellationToken);
            result.IsSuccess.ShouldBeTrue();
            // Valid decodable PNG, same geometry; different bytes after the adapter accepted it.
            using var file = new FileStream(workspace.ResolveAbsolute(request.ExpectedOutput), FileMode.Append);
            file.WriteByte(0);
            return result;
        }
    }

    internal sealed class RecordingAuthority : IPdfDocumentAuthority
    {
        private readonly WindowsPdfDocumentAuthority _inner = new();
        public string Provider => _inner.Provider;
        public int Opens { get; private set; }
        public int Selections { get; private set; }
        public int Renders { get; private set; }
        public string? Fault { get; set; }
        public Action? OnSelection { get; set; }
        public List<string> Paths { get; } = [];
        public async Task<IPdfDocument> OpenAsync(Stream input, CancellationToken cancellationToken)
        {
            Opens++;
            return new Document(this, await _inner.OpenAsync(input, cancellationToken));
        }
        private sealed class Document(RecordingAuthority owner, IPdfDocument inner) : IPdfDocument
        {
            public int PageCount => inner.PageCount;
            public bool IsEncrypted => inner.IsEncrypted;
            public PdfInspection InspectSinglePage()
            {
                owner.Selections++; var result = inner.InspectSinglePage(); owner.OnSelection?.Invoke(); return result;
            }
            public async Task<bool> RenderAsync(string output, CancellationToken cancellationToken)
            {
                owner.Renders++; owner.Paths.Add(output);
                if (owner.Fault == "throw") { File.WriteAllText(output, "partial"); throw new IOException("Synthetic renderer failure."); }
                if (owner.Fault == "missing") return false;
                bool alpha = await inner.RenderAsync(output, cancellationToken);
                switch (owner.Fault)
                {
                    case "partial": File.WriteAllBytes(output, [137, 80, 78, 71]); break;
                    case "dimensions": File.WriteAllBytes(output, SyntheticImages.Png(5, 5)); break;
                    case "format": File.WriteAllBytes(output, SyntheticImages.Jpeg(600, 300)); break;
                    case "alpha": return !alpha;
                }
                return alpha;
            }
            public void Dispose() => inner.Dispose();
        }
    }
}
