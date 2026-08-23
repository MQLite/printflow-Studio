using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Automation;

// The `Unit` result type, aliased inside the namespace: `PrintFlow.Tests.Unit` is a namespace
// in this assembly, and an enclosing namespace wins over a compilation-unit alias.
using Unit = PrintFlow.Domain.Results.Unit;

/// <summary>
/// The rule the whole epic exists for: verify the target, then act — never act, then verify
/// (Epic 11300 Part A §3, §10, §19, §25).
/// </summary>
/// <remarks>
/// Every negative case here asserts on a <i>recorder</i> being empty rather than only on the
/// failure code. A failure code proves PrintFlow reported a problem; an empty recorder proves it
/// did not first send a keystroke into someone else's window and report the problem afterwards.
/// That distinction is the entire difference between this slice and the incident that motivated
/// it.
/// </remarks>
public sealed class GuardedMeituUiDriverTests
{
    private static readonly MeituAutomationOptions FastOptions = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ActivationTimeout = TimeSpan.FromMilliseconds(60),
        DialogTimeout = TimeSpan.FromMilliseconds(60),
        AttachTimeout = TimeSpan.FromMilliseconds(60),
        LaunchTimeout = TimeSpan.FromMilliseconds(60),
        OpenConfirmationTimeout = TimeSpan.FromMilliseconds(60),
    };

    private sealed record Harness(
        GuardedMeituUiDriver Driver,
        FakeWindowLocator Locator,
        RecordingUiElementProvider Elements,
        RecordingInputSink Input,
        RecordingEvidenceSink Evidence,
        MeituTarget Target);

    private static Harness Build(bool meituInForeground)
    {
        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        RecordingInputSink input = new();
        RecordingEvidenceSink evidence = new();

        MeituTarget target = MeituFakes.Target();
        locator.Register(target.Process, target.Window);

        if (meituInForeground)
        {
            locator.PutInForeground(target.Window);
        }

        GuardedMeituUiDriver driver = new(
            locator, elements, input, evidence,
            new StubMeituBaselineProvider(MeituFakes.Baseline()),
            FastOptions,
            TimeProvider.System);

        return new Harness(driver, locator, elements, input, evidence, target);
    }

    // -----------------------------------------------------------------------------
    // Correct target
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task A_verified_foreground_Meitu_window_may_be_interacted_with()
    {
        Harness h = Build(meituInForeground: true);
        h.Elements.MakeFindable(new UiElementQuery(UiControlKind.Any, Name: "图片编辑"));

        OperationResult<Unit> invoked = await h.Driver.InvokeKnownElementAsync(
            h.Target, KnownMeituElement.WelcomeOpenEntry, CancellationToken.None);

        invoked.IsSuccess.ShouldBeTrue();
        h.Elements.Invocations.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_verified_foreground_Meitu_window_may_receive_a_named_shortcut()
    {
        Harness h = Build(meituInForeground: true);

        OperationResult<Unit> sent = await h.Driver.SendVerifiedShortcutAsync(
            h.Target, KnownShortcut.OpenFile, CancellationToken.None);

        sent.IsSuccess.ShouldBeTrue();
        h.Input.Sends.ShouldHaveSingleItem();
        h.Input.Sends[0].Target.ShouldBe(h.Target.Window.Handle);
    }

    // -----------------------------------------------------------------------------
    // Wrong foreground process — the Explorer regression (§19)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task No_keystroke_is_sent_when_Explorer_holds_the_foreground()
    {
        Harness h = Build(meituInForeground: false);
        h.Locator.Foreground = new ForegroundIdentity(new WindowHandle(0xE1E1), 777, "explorer");

        OperationResult<Unit> sent = await h.Driver.SendVerifiedShortcutAsync(
            h.Target, KnownShortcut.OpenFile, CancellationToken.None);

        sent.IsFailure.ShouldBeTrue();
        sent.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        sent.Failure.TechnicalDetail.ShouldContain("explorer");
        h.Input.Sends.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_element_is_invoked_when_Explorer_holds_the_foreground()
    {
        Harness h = Build(meituInForeground: false);
        h.Locator.Foreground = new ForegroundIdentity(new WindowHandle(0xE1E1), 777, "explorer");
        h.Elements.MakeFindable(new UiElementQuery(UiControlKind.Any, Name: "图片编辑"));

        OperationResult<Unit> invoked = await h.Driver.InvokeKnownElementAsync(
            h.Target, KnownMeituElement.WelcomeOpenEntry, CancellationToken.None);

        invoked.IsFailure.ShouldBeTrue();
        invoked.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        h.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>
    /// The failure record says, in so many words, that nothing was sent.
    /// </summary>
    /// <remarks>
    /// Worth asserting because this is what an operator or an auditor reads after the fact. A
    /// <c>MeituTargetLost</c> that left it ambiguous whether a key had already gone somewhere
    /// would be a much less useful record than one that states it did not.
    /// </remarks>
    [Fact]
    public async Task The_target_lost_failure_records_that_no_input_was_sent()
    {
        Harness h = Build(meituInForeground: false);

        OperationResult<Unit> sent = await h.Driver.SendVerifiedShortcutAsync(
            h.Target, KnownShortcut.OpenFile, CancellationToken.None);

        sent.Failure.Context["inputSent"].ShouldBe("false");
        sent.Failure.Context["actualProcess"].ShouldBe("explorer");
        sent.Failure.IsRetryable.ShouldBeTrue();
    }

    /// <summary>
    /// A handle reused by another process is not the window PrintFlow verified.
    /// </summary>
    /// <remarks>
    /// The subtle version of the same hazard: the foreground handle matches, so a check that
    /// compared handles alone would wave this through — but the window now belongs to a
    /// different process, which means the application behind it is a different application.
    /// </remarks>
    [Fact]
    public async Task No_input_is_sent_when_the_window_handle_now_belongs_to_another_process()
    {
        Harness h = Build(meituInForeground: true);
        h.Locator.Replace(
            h.Target.Process,
            MeituFakes.Window(handle: h.Target.Window.Handle.Value, owningProcessId: 31337, title: "Something else"));

        OperationResult<Unit> sent = await h.Driver.SendVerifiedShortcutAsync(
            h.Target, KnownShortcut.OpenFile, CancellationToken.None);

        sent.IsFailure.ShouldBeTrue();
        sent.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        h.Input.Sends.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_input_is_sent_when_the_Meitu_process_has_exited()
    {
        Harness h = Build(meituInForeground: true);
        h.Locator.DeadProcessIds.Add(h.Target.Process.ProcessId);

        OperationResult<Unit> sent = await h.Driver.SendVerifiedShortcutAsync(
            h.Target, KnownShortcut.OpenFile, CancellationToken.None);

        sent.IsFailure.ShouldBeTrue();
        sent.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        h.Input.Sends.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Activation
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Activation_fails_closed_when_Windows_declines_to_change_the_foreground()
    {
        Harness h = Build(meituInForeground: false);
        h.Locator.RefuseActivation = true;

        OperationResult<MeituTarget> activated =
            await h.Driver.ActivateAsync(h.Target, CancellationToken.None);

        activated.IsFailure.ShouldBeTrue();
        activated.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        h.Locator.ActivationRequests.ShouldBeGreaterThan(0);
        h.Input.Sends.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Opening a file
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Opening_a_working_copy_writes_the_path_into_a_dialog_owned_by_the_verified_process()
    {
        Harness h = Build(meituInForeground: true);
        h.Elements.MakeFindable(new UiElementQuery(UiControlKind.Any, Name: "图片编辑"));
        h.Elements.MakeFindable(new UiElementQuery(UiControlKind.Edit, AutomationId: "1148"));
        h.Elements.MakeFindable(new UiElementQuery(UiControlKind.Button, AutomationId: "1"));

        ExternalWindowRef dialog = MeituFakes.Window(
            handle: 0x2000, owningProcessId: h.Target.Process.ProcessId, title: "打开", className: "#32770");
        h.Locator.Replace(h.Target.Process, h.Target.Window, dialog);

        OperationResult<Unit> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsSuccess.ShouldBeTrue();
        h.Elements.ValueWrites.ShouldHaveSingleItem();
        h.Elements.ValueWrites[0].Value.ShouldBe(@"C:\Temp\printflow\working.png");
    }

    /// <summary>
    /// A dialog belonging to some other process is never written to.
    /// </summary>
    /// <remarks>
    /// This is the file-dialog form of the Explorer regression: a Save As box in another
    /// application looks exactly like the one PrintFlow is waiting for, and typing a path into it
    /// would be the same class of accident. Ownership by the verified Meitu process is what
    /// separates the two, so the test hands the fake a perfectly convincing common dialog owned
    /// by someone else.
    /// </remarks>
    [Fact]
    public async Task A_file_dialog_belonging_to_another_process_is_never_typed_into()
    {
        Harness h = Build(meituInForeground: true);
        h.Elements.MakeFindable(new UiElementQuery(UiControlKind.Any, Name: "图片编辑"));
        h.Elements.MakeFindable(new UiElementQuery(UiControlKind.Edit, AutomationId: "1148"));

        ExternalProcessRef impostor = MeituFakes.Process(id: 5150);
        h.Locator.Register(
            impostor,
            MeituFakes.Window(handle: 0x3000, owningProcessId: impostor.ProcessId, title: "打开", className: "#32770"));

        OperationResult<Unit> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.MeituOpenInputFailed);
        h.Elements.ValueWrites.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Cancellation_exits_without_sending_anything()
    {
        Harness h = Build(meituInForeground: true);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            h.Driver.SendVerifiedShortcutAsync(h.Target, KnownShortcut.OpenFile, cancelled.Token));

        h.Input.Sends.ShouldBeEmpty();
        h.Elements.Invocations.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Element naming
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The driver refuses to look for a control the signed evidence does not name.
    /// </summary>
    /// <remarks>
    /// <c>WelcomeOpenEntryName</c> is configurable, which without this check would be a back door
    /// for pointing the automation at any label at all. Requiring the configured value to appear
    /// in the verified baseline keeps configuration a <i>selection from</i> signed evidence
    /// rather than an addition to it (§2).
    /// </remarks>
    [Fact]
    public void An_element_name_absent_from_the_signed_markers_is_never_searched_for()
    {
        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        MeituTarget target = MeituFakes.Target();
        locator.Register(target.Process, target.Window);
        locator.PutInForeground(target.Window);

        GuardedMeituUiDriver driver = new(
            locator, elements, new RecordingInputSink(), new RecordingEvidenceSink(),
            new StubMeituBaselineProvider(MeituFakes.Baseline()),
            FastOptions with { WelcomeOpenEntryName = "删除全部" },
            TimeProvider.System);

        OperationResult<UiElementRef> found =
            driver.FindKnownElement(target, KnownMeituElement.WelcomeOpenEntry);

        found.IsFailure.ShouldBeTrue();
        found.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
    }
}
