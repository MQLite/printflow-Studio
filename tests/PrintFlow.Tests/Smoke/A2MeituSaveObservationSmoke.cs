using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Automation;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Tests.Fixtures;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// Separately gated read-only native/UIA observation and explicitly bound retained synthetic
/// probe cancellation. Only the recovery method can activate, cancel or close through Product.
/// </summary>
public sealed class A2MeituSaveObservationSmoke(ITestOutputHelper output)
{
    [Fact]
    public async Task Recover_exact_retained_synthetic_identity_probe_through_Product()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_A2_MEITU_RECOVER_SYNTHETIC") != "1") return;
        string file = Path.GetFullPath(Environment.GetEnvironmentVariable("PRINTFLOW_A2_MEITU_SYNTHETIC_FILE")!);
        string expectedHash = Environment.GetEnvironmentVariable("PRINTFLOW_A2_MEITU_SYNTHETIC_SHA256")!;
        string controlledRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PrintFlowMeituSmoke")) + Path.DirectorySeparatorChar;
        file.StartsWith(controlledRoot, StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        Path.GetFileName(file).StartsWith("PF_BACKGROUND_C1_", StringComparison.Ordinal).ShouldBeTrue();
        File.Exists(file).ShouldBeTrue();
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ShouldBe(expectedHash);
        string transcriptPath = Environment.GetEnvironmentVariable("PRINTFLOW_A2_MEITU_FAILURE_TRANSCRIPT")!;
        string transcriptHash = Environment.GetEnvironmentVariable("PRINTFLOW_A2_MEITU_FAILURE_SHA256")!;
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(transcriptPath))).ShouldBe(transcriptHash);
        string transcript = File.ReadAllText(transcriptPath);
        transcript.ShouldContain("synthetic working copy: " + file);
        transcript.ShouldContain("ENHANCEMENT STOPPED");
        transcript.ShouldContain("The signed Save surface is not the exact foreground window; no dialog control was used.");
        transcript.ShouldContain("No output was exported and no Revision was created.");

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PrintFlowStudio.sln"))) root = root.Parent;
        root.ShouldNotBeNull();
        var configuration = PrintFlowConfiguration.LoadFromFile(Path.Combine(root.FullName, "appsettings.json"));
        PresetMeituBaselineProvider provider = new(
            Path.Combine(configuration.Workspace.Root, configuration.Preset.Path), Sha256.Parse(configuration.Preset.ExpectedSha256));
        var baseline = provider.GetVerifiedBaseline();
        baseline.IsSuccess.ShouldBeTrue();
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baseline.Value.ExecutablePath)))
            .ShouldBe(baseline.Value.ExecutableSha256.Value);
        await using WorkstationAutomationLeaseScope lease = await WorkstationAutomationLeaseScope.AcquireDefaultAsync();
        Win32ExternalAppWindowLocator locator = new();
        var processes = locator.FindProcessesByExecutable(baseline.Value.ExecutablePath);
        processes.IsSuccess.ShouldBeTrue();
        processes.Value.Count.ShouldBe(1);
        var process = processes.Value[0];
        transcript.ShouldContain("process id           : " + process.ProcessId);
        process.StartedUtc.ShouldBe(DateTimeOffset.Parse(
            Environment.GetEnvironmentVariable("PRINTFLOW_A2_MEITU_PROCESS_STARTED_UTC")!,
            System.Globalization.CultureInfo.InvariantCulture));
        var windows = locator.FindTopLevelWindows(process);
        windows.IsSuccess.ShouldBeTrue();
        var editors = windows.Value.Where(w => w.Title == baseline.Value.DocumentIdentity!.Editor.WindowTitle).ToArray();
        editors.Length.ShouldBe(1);
        MeituTarget target = new(process, editors[0]);
        string evidence = Path.Combine(Path.GetTempPath(), "PrintFlowMeituRecovery", Guid.NewGuid().ToString("N"));
        GuardedMeituUiDriver driver = new(locator, new UiaElementProvider(), new Win32ScopedInputSink(locator),
            new GdiWindowEvidenceSink(evidence, TimeProvider.System), provider, new MeituAutomationOptions(), TimeProvider.System);
        output.WriteLine("ObservedAtUtc: " + DateTimeOffset.UtcNow.ToString("O"));
        output.WriteLine("ExactSyntheticFile: " + file + "; SHA256=" + expectedHash);
        output.WriteLine("Process: " + JsonSerializer.Serialize(process));
        output.WriteLine("FailedProbeTranscript: " + transcriptPath + "; SHA256=" + transcriptHash);
        var signature = baseline.Value.DocumentIdentity!;
        UiaElementProvider elements = new();
        ExternalWindowRef ReadExactPendingSurface()
        {
            locator.IsAlive(process).ShouldBeTrue();
            var dialogs = locator.FindOwnedDialogs(process, editors[0]);
            dialogs.IsSuccess.ShouldBeTrue();
            dialogs.Value.Count.ShouldBe(1);
            var surface = locator.Refresh(dialogs.Value[0].Handle);
            surface.IsSuccess.ShouldBeTrue();
            surface.Value.OwningProcessId.ShouldBe(process.ProcessId);
            surface.Value.OwnerHandle.ShouldBe(editors[0].Handle);
            surface.Value.Title.ShouldBe(signature.DialogTitle);
            surface.Value.ClassName.ShouldBe(signature.DialogClassName);
            surface.Value.IsVisible.ShouldBeTrue();
            surface.Value.IsEnabled.ShouldBeTrue();
            return surface.Value;
        }
        UiElementRef Resolve(ExternalWindowRef surface, MeituControlSignature shape)
        {
            var found = elements.FindAll(surface.Handle, new UiElementQuery(UiControlKind.Any));
            found.IsSuccess.ShouldBeTrue();
            var described = found.Value.Select(e => (Element: e, Identity: elements.Describe(e)))
                .Where(e => e.Identity.IsSuccess).ToArray();
            var selected = MeituCardTargetRule.SelectSignedControl(shape, process.ProcessId,
                described.Select(e => e.Identity.Value).ToArray());
            selected.IsSuccess.ShouldBeTrue();
            return described[selected.Value].Element;
        }
        void CheckExactRetainedName(ExternalWindowRef surface)
        {
            var value = elements.GetValue(Resolve(surface, signature.FileNameControl));
            value.IsSuccess.ShouldBeTrue();
            MeituDocumentIdentityRule.MatchesExpectedWorkingCopy(signature, Path.GetFileName(file), value.Value).ShouldBeTrue();
            _ = Resolve(surface, signature.CancelControl);
        }
        ExternalWindowRef pendingSurface = ReadExactPendingSurface();
        CheckExactRetainedName(pendingSurface);
        ExternalWindowRef beforeActivation = ReadExactPendingSurface();
        beforeActivation.Handle.ShouldBe(pendingSurface.Handle);
        locator.Activate(beforeActivation).IsSuccess.ShouldBeTrue();
        DateTimeOffset deadline = DateTimeOffset.UtcNow + new MeituAutomationOptions().DialogTimeout;
        while (locator.ReadForeground().Value.Handle != pendingSurface.Handle && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(new MeituAutomationOptions().PollInterval);
        ReadExactPendingSurface().Handle.ShouldBe(pendingSurface.Handle);
        CheckExactRetainedName(pendingSurface);
        var cancelled = await driver.CancelIdentityDialogAsync(target, pendingSurface, signature, CancellationToken.None);
        output.WriteLine("ExplicitInterruptedProbeCancellation: " + (cancelled.IsSuccess ? "cancel-only" : cancelled.Failure.TechnicalDetail));
        cancelled.IsSuccess.ShouldBeTrue(cancelled.IsFailure ? cancelled.Failure.TechnicalDetail : string.Empty);
        // Cancellation carries no document identity authority. Generate/read/cancel a fresh
        // ordinary probe before any document close; never promote the retained editable value.
        var confirmed = await driver.ConfirmWorkingCopyIdentityAsync(target, Path.GetFileName(file), CancellationToken.None);
        output.WriteLine("IdentityRecovery: " + (confirmed.IsSuccess ? confirmed.Value.State.ToString() : confirmed.Failure.TechnicalDetail));
        confirmed.IsSuccess.ShouldBeTrue(confirmed.IsFailure ? confirmed.Failure.TechnicalDetail : string.Empty);
        confirmed.Value.State.ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);
        var activated = await driver.ActivateAsync(target, CancellationToken.None);
        activated.IsSuccess.ShouldBeTrue();
        var closed = await driver.CloseDocumentAsync(activated.Value, CancellationToken.None);
        output.WriteLine("CloseExactSynthetic: " + (closed.IsSuccess ? "KnownEditorEmpty" : closed.Failure.TechnicalDetail));
        closed.IsSuccess.ShouldBeTrue(closed.IsFailure ? closed.Failure.TechnicalDetail : string.Empty);
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ShouldBe(expectedHash);
        output.WriteLine("Backing file preserved unchanged; no Save, SaveAs, discard or file cleanup.");
    }

    [Fact]
    public async Task Read_current_Save_relationship_without_input()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_A2_MEITU_OBSERVATION") != "1") return;

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PrintFlowStudio.sln")))
            root = root.Parent;
        root.ShouldNotBeNull();
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            Path.Combine(root.FullName, "appsettings.json"));
        var baseline = new PresetMeituBaselineProvider(
            Path.Combine(configuration.Workspace.Root, configuration.Preset.Path),
            Sha256.Parse(configuration.Preset.ExpectedSha256)).GetVerifiedBaseline();
        baseline.IsSuccess.ShouldBeTrue();

        await using WorkstationAutomationLeaseScope lease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();
        Win32ExternalAppWindowLocator locator = new();
        var processes = locator.FindProcessesByExecutable(baseline.Value.ExecutablePath);
        processes.IsSuccess.ShouldBeTrue();
        processes.Value.Count.ShouldBe(1);
        var process = processes.Value[0];
        output.WriteLine("ObservedAtUtc: " + DateTimeOffset.UtcNow.ToString("O"));
        output.WriteLine("Process: " + JsonSerializer.Serialize(process));
        output.WriteLine("ForegroundBefore: " + locator.ReadForeground().Value);
        var windows = locator.FindTopLevelWindows(process);
        windows.IsSuccess.ShouldBeTrue();
        foreach (var window in windows.Value)
        {
            output.WriteLine($"Native: handle={window.Handle}; owner=0x{NativeMethods.GetWindow(window.Handle.Value, NativeMethods.GW_OWNER):X}; " +
                $"style=0x{NativeMethods.GetWindowLongPtr(window.Handle.Value, NativeMethods.GWL_STYLE):X}; " +
                $"title={window.Title}; class={window.ClassName}; visible={window.IsVisible}; enabled={window.IsEnabled}");
            AutomationElement element = AutomationElement.FromHandle(window.Handle.Value);
            for (int depth = 0; depth < 5 && element is not null; depth++)
            {
                var current = element.Current;
                output.WriteLine($"UIA[{depth}]: hwnd=0x{current.NativeWindowHandle:X}; pid={current.ProcessId}; " +
                    $"id={current.AutomationId}; name={current.Name}; class={current.ClassName}; type={current.ControlType.ProgrammaticName}");
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            UiaElementProvider elements = new();
            var fields = elements.FindAll(window.Handle, new UiElementQuery(UiControlKind.Edit));
            if (fields.IsFailure) continue;
            foreach (var field in fields.Value)
            {
                var identity = elements.Describe(field);
                if (identity.IsFailure || !identity.Value.AutomationId.Contains("SaveMaskWidget", StringComparison.Ordinal)) continue;
                var value = elements.GetValue(field);
                output.WriteLine("SaveField: " + identity.Value + "; value=" +
                    (value.IsSuccess ? value.Value : value.Failure.TechnicalDetail));
            }
        }
        locator.IsAlive(process).ShouldBeTrue();
        output.WriteLine("ForegroundAfter: " + locator.ReadForeground().Value);
        output.WriteLine("ReadOnly: no activation, input, close, cleanup or readiness result");
    }
}
