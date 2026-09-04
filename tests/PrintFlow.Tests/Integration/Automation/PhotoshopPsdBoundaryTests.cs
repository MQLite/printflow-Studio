using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Automation;

public sealed class PhotoshopPsdBoundaryTests
{
    [Theory]
    [InlineData("success", null)]
    [InlineData("missing", FailureCode.OutputMissing)]
    [InlineData("malformed", FailureCode.PsdPreparationFailed)]
    [InlineData("wrong-size", FailureCode.PsdPreparationFailed)]
    [InlineData("native-failure", FailureCode.PhotoshopUnknownState)]
    [InlineData("identity", FailureCode.PhotoshopDocumentIdentityUnconfirmed)]
    [InlineData("open", FailureCode.PhotoshopOpenInputFailed)]
    [InlineData("modal", FailureCode.PhotoshopBlockingDialog)]
    [InlineData("target-loss", FailureCode.PhotoshopTargetLost)]
    [InlineData("cancel", FailureCode.Cancelled)]
    [InlineData("w1", FailureCode.PsdUnsupported)]
    [InlineData("cmyk", FailureCode.PsdUnsupported)]
    public async Task Production_psd_path_enforces_real_guards_and_independent_validation(string variant, FailureCode? expected)
    {
        using TempWorkspace temp = new();
        string executable = temp.CreateSourceFile("Photoshop.exe", [0x4D, 0x5A, 0x90, 0]);
        var version = FileVersionInfo.GetVersionInfo(executable);
        var baseline = PhotoshopFakes.Baseline() with
        {
            ExecutablePath = executable,
            ExecutableSha256 = Sha256.FromBytes(SHA256.HashData(File.ReadAllBytes(executable))),
            AcceptedProductVersion = version.ProductVersion ?? "(unreadable)",
            AcceptedFileVersion = version.FileVersion ?? "(unreadable)",
        };
        string input = temp.CreateSourceFile("input.psd", PsdInputPreparationTests.RgbCompositePsd());
        byte[] before = File.ReadAllBytes(input);
        var process = new ExternalProcessRef(7777, executable, DateTimeOffset.UnixEpoch);
        var window = PhotoshopFakes.Window();
        FakeWindowLocator locator = new(); locator.Register(process, window);
        Driver driver = new(input) { Fault = variant };
        using CancellationTokenSource cancel = new();
        Native native = new(variant, () =>
        {
            if (variant == "cancel") cancel.Cancel();
            if (variant == "target-loss") locator.DeadProcessIds.Add(7777);
        });
        var adapter = new ProductionPhotoshopOutputProcessor(new StubPhotoshopBaselineProvider(baseline),
            locator, driver, new StubPhotoshopWorkspace(temp.Root), new PhotoshopAutomationOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(2), TiffSettleTimeout = TimeSpan.FromMilliseconds(40),
                AttachTimeout = TimeSpan.FromMilliseconds(20), OpenConfirmationTimeout = TimeSpan.FromMilliseconds(20),
            }, TimeProvider.System) { PsdNative = native };
        var result = await adapter.PreparePsdAsync(new PsdPreparationRequest(
            WorkspaceFileRef.Create("Sessions/S/Working/A/input.psd", WorkspaceArea.Working),
            WorkspaceFileRef.Create("Sessions/S/Working/A/prepared.png", WorkspaceArea.Working)), cancel.Token);
        if (expected is { } failure)
        {
            result.IsFailure.ShouldBeTrue();
            result.Failure.Code.ShouldBe(failure);
        }
        else
        {
            result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
            result.Value.PsdInspection!.HasTransparency.ShouldBe(true);
            driver.CloseCount.ShouldBe(1);
        }
        if (variant is "open" or "identity" or "modal") native.Calls.ShouldBe(0);
        if (variant is "native-failure" or "target-loss" or "cancel") driver.CloseCount.ShouldBe(0);
        if (variant is "w1" or "cmyk") result.Failure.PsdInspection.ShouldNotBeNull();
        File.ReadAllBytes(input).ShouldBe(before);
    }

    private sealed class Native(string variant, Action after) : IPhotoshopPsdNativeBridge
    {
        public int Calls { get; private set; }
        public OperationResult<PsdNativeOutcome> ExportOnce(PsdNativeCommand command, PhotoshopBaseline baseline)
        {
            Calls++;
            if (variant == "native-failure") return OperationResult.Fail<PsdNativeOutcome>(FailureCode.PhotoshopUnknownState, "No native evidence.");
            var facts = new PsdInspection(4, 3, variant == "cmyk" ? "CMYK" : "RGB", 8, true, true,
                variant == "w1" ? [new("W1", "SPOTCOLOR")] : [], "native-test-double");
            if (variant is "w1" or "cmyk") return OperationResult.Ok(new PsdNativeOutcome(false, facts, "Unsupported PSD state."));
            if (variant != "missing") File.WriteAllBytes(command.OutputPath, variant == "malformed" ? [1, 2, 3] :
                SyntheticImages.Png(variant == "wrong-size" ? 5 : 4, 3));
            after();
            return OperationResult.Ok(new PsdNativeOutcome(true, facts, "Prepared."));
        }
    }

    private sealed class Driver(string input) : IPhotoshopUiDriver
    {
        public string Fault { get; init; } = "";
        public int CloseCount { get; private set; }
        private bool _open;
        public Task<OperationResult<PhotoshopStateSnapshot>> InspectStateAsync(PhotoshopTarget target, string? name, CancellationToken token)
        {
            var state = Fault == "modal" ? PhotoshopStartingState.KnownModal :
                _open ? PhotoshopStartingState.KnownEditorWithOtherDocument : PhotoshopStartingState.KnownStartScreen;
            string title = _open ? PhotoshopFakes.TitleFor(Path.GetFileName(input)) : PhotoshopFakes.NoDocumentTitle;
            return Task.FromResult(OperationResult.Ok(new PhotoshopStateSnapshot(state, [], new PhotoshopObservation(title, [], [], true, name))));
        }
        public Task<OperationResult<PhotoshopTarget>> ActivateAsync(PhotoshopTarget target, CancellationToken token) => Task.FromResult(OperationResult.Ok(target));
        public Task<OperationResult<PhotoshopTarget>> OpenManagedDocumentAsync(PhotoshopTarget target, string path, CancellationToken token)
        {
            _open = true;
            return Task.FromResult(Fault == "open" ? OperationResult.Fail<PhotoshopTarget>(FailureCode.PhotoshopOpenInputFailed, "Cannot open.") : OperationResult.Ok(target));
        }
        public Task<OperationResult<PhotoshopDocumentIdentity>> ProbeDocumentIdentityAsync(PhotoshopTarget target, CancellationToken token)
        {
            string path = Fault == "identity" ? Path.Combine(Path.GetDirectoryName(input)!, "wrong.psd") : input;
            return Task.FromResult(OperationResult.Ok(new PhotoshopDocumentIdentity(Path.GetFileName(path), Path.GetDirectoryName(path)!, path,
                PhotoshopFakes.TitleFor(Path.GetFileName(input)))));
        }
        public Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(PhotoshopTarget target, string path, CancellationToken token)
        {
            CloseCount++; _open = false; return Task.FromResult(OperationResult.Ok(target));
        }
        public OperationResult<EvidenceRef> CaptureEvidence(PhotoshopTarget target, string reason) => OperationResult.Fail<EvidenceRef>(FailureCode.WorkspaceError, "No screenshot in test.");
    }
}
