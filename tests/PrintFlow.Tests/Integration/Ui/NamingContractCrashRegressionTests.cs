using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The R2 Final Gate failure, as a test (naming-contract fix §11).
/// </summary>
/// <remarks>
/// What happened at the gate: in Fake mode, an operator prepared Background Removal, authorised
/// the Revision and hash on screen, and pressed Run. <c>SessionService</c> asked the naming
/// authority for the cutout name, the authority handed the preset's <c>{Name}_CUTOUT.png</c> to
/// <c>string.Format</c>, and the <see cref="FormatException"/> that composite formatting throws
/// for a non-numeric argument index travelled out of the await in the view model's Run command
/// and terminated the WPF process. No CUTOUT review was ever reached.
/// <para>
/// The tests below drive that exact sequence through the real <see cref="SessionViewModel"/>,
/// the real <see cref="SessionService"/> and a real database, and assert both halves of the
/// fix: the accepted pattern now renders and the step reaches ReviewRequired, and a pattern that
/// genuinely cannot render leaves the screen with a failure notice rather than an exception.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class NamingContractCrashRegressionTests
{
    /// <summary>
    /// Fake mode, prepare Background Removal, authorise, Run — the command completes and CUTOUT
    /// review is reached (§11).
    /// </summary>
    /// <remarks>
    /// The Run is awaited directly rather than through any exception-swallowing helper, so a
    /// <see cref="FormatException"/> escaping the command would fail this test as an unhandled
    /// exception — which is what it did to the process.
    /// </remarks>
    [Fact]
    public async Task Running_background_removal_from_the_screen_reaches_cutout_review()
    {
        using HomeScreenHarness harness = new();
        (SessionViewModel screen, SessionId id) = await AtBackgroundRemovalAsync(harness, "r2-cutout.png");

        screen.IsFakeProcessing.ShouldBeTrue("this regression must never touch a real Meitu.");

        screen.BeginAutomaticSelectionCommand.Execute(null);
        await screen.ConfirmAutomaticSelectionCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.IsAutomaticSelectionAuthorised.ShouldBeTrue();

        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;

        screen.Notice.ShouldBeNull();
        screen.IsReviewRequired.ShouldBeTrue();

        SessionAggregate persisted = await ReloadAsync(harness, id);
        persisted.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State
            .ShouldBe(StepState.ReviewRequired);

        Revision cutout = persisted.Revisions.Single(r => r.Operation == OperationKind.RemoveBackground);
        cutout.File.FileName.ShouldBe("r2-cutout_CUTOUT.png");
    }

    /// <summary>
    /// Enhancement reaches HD review through the same screen, for the same reason (§10).
    /// </summary>
    [Fact]
    public async Task Running_enhancement_from_the_screen_reaches_HD_review()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.Inner.WriteBorderedSourcePng("r2-hd.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionView imported = harness.Navigation.WorkflowSelectionFor!;
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(imported);

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;

        screen.Notice.ShouldBeNull();
        screen.IsReviewRequired.ShouldBeTrue();

        SessionAggregate persisted = await ReloadAsync(harness, imported.Id);
        persisted.Revisions.Single(r => r.Operation == OperationKind.Enhance)
            .File.FileName.ShouldBe("r2-hd_HD.png");
    }

    /// <summary>
    /// A pattern the renderer cannot honour becomes an operator notice, not a process fault
    /// (§6, §11).
    /// </summary>
    /// <remarks>
    /// The other half of the fix, and the half that makes the first half durable. Rendering the
    /// accepted syntax correctly is not by itself a guarantee that a future manifest cannot
    /// crash the shell — refusing an unrenderable one structurally is. The preset here is
    /// hash-verified and supplies <c>{Foo}_CUTOUT.png</c>: the step fails, the screen says so,
    /// and no exception leaves the command.
    /// </remarks>
    [Fact]
    public async Task An_unrenderable_cutout_pattern_becomes_a_notice_rather_than_a_crash()
    {
        using HomeScreenHarness harness = new(StubNamingPresetProvider.WithUnrenderableCutoutPattern());
        (SessionViewModel screen, SessionId id) = await AtBackgroundRemovalAsync(harness, "r2-broken.png");

        screen.BeginAutomaticSelectionCommand.Execute(null);
        await screen.ConfirmAutomaticSelectionCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;

        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;

        screen.Notice.ShouldNotBeNull();
        screen.IsReviewRequired.ShouldBeFalse();

        SessionAggregate persisted = await ReloadAsync(harness, id);
        persisted.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State
            .ShouldNotBe(StepState.ReviewRequired);
        persisted.Revisions.ShouldNotContain(r => r.Operation == OperationKind.RemoveBackground);
    }

    /// <summary>
    /// Imports, confirms, runs and approves Enhancement, and leaves the screen on Background
    /// Removal with nothing authorised.
    /// </summary>
    private static async Task<(SessionViewModel Screen, SessionId Id)> AtBackgroundRemovalAsync(
        HomeScreenHarness harness, string fileName)
    {
        harness.FilePicker.Path = harness.Inner.WriteBorderedSourcePng(fileName);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionView imported = harness.Navigation.WorkflowSelectionFor!;
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(imported);

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.ApproveCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.Notice.ShouldBeNull();

        (await ReloadAsync(harness, imported.Id)).ToSnapshot()
            .CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);

        return (screen, imported.Id);
    }

    private static async Task<SessionAggregate> ReloadAsync(HomeScreenHarness harness, SessionId id) =>
        (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
}
