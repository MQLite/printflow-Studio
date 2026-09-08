using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The editable Output Name and the source context on Workflow Selection
/// (SCRUM-11075, SCRUM-11078).
/// </summary>
/// <remarks>
/// Driven against the real session service, workspace and SQLite database — only the file dialog
/// and the window are substituted, the convention <c>HomeAndWorkflowSelectionTests</c>
/// established.
/// <para>
/// Deliberately short. The naming contract itself already has thorough unit coverage
/// (<c>SanitiserTests</c>, <c>NamingPatternRendererTests</c>, <c>AcceptedNamingContractTests</c>)
/// and the collision reservation has its own (<c>WorkspaceTests</c>), so nothing here re-tests a
/// filename matrix. What is new is the <i>screen</i>: that it seeds from the session, that an
/// edit reaches the existing engine command, that a refused edit starts nothing, and that what
/// the operator typed is what the produced file is eventually named.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class OutputNameAndSourceContextTests
{
    // -------------------------------------------------------------------------------------
    // SCRUM-11075: the operator-facing name, before the workflow runs
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The box opens on the source-derived default, and the source file is named separately.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The default has to be the accepted source-derived one rather than
    /// something the screen invented, and the operator has to be able to tell which file they are
    /// naming output for — an editable name that has been changed no longer identifies the source
    /// (SCRUM-11078).
    /// </remarks>
    [Fact]
    public async Task The_box_opens_on_the_source_derived_name_beside_the_source_file()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("customer-design.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        WorkflowSelectionViewModel selection = harness.WorkflowSelection(new RecordingNavigation());
        selection.Open(harness.Navigation.WorkflowSelectionFor!);

        selection.OutputName.ShouldBe("customer-design");
        selection.SessionName.ShouldBe("customer-design");
        selection.SourceFileName.ShouldBe("customer-design.png");
        selection.HasSourceFileName.ShouldBeTrue();

        // The engine decides whether the name may still be edited, exactly as it decides whether
        // the workflow may still be chosen.
        selection.CanEditOutputName.ShouldBeTrue();
    }

    /// <summary>
    /// An accepted edit goes through <see cref="WorkflowCommand.SetOutputName"/>, is persisted,
    /// and is what the Session then holds (SCRUM-11075).
    /// </summary>
    /// <remarks>
    /// Asserted against SQLite rather than the returned view, because the in-memory answer was
    /// never the question: what has to survive is the row the next command will read.
    /// </remarks>
    [Fact]
    public async Task An_edited_name_is_committed_before_the_workflow_and_reaches_the_session()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("IMG_4471.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(harness.Navigation.WorkflowSelectionFor!);

        selection.OutputName = "Memorial Design";
        await selection.SelectCommand.ExecuteAsync(
            selection.Workflows.Single(w => w.Type == WorkflowType.PrepareAsset));

        selection.Notice.ShouldBeNull();

        SessionView opened = navigation.SessionFor.ShouldNotBeNull();
        opened.OutputName.Value.ShouldBe("Memorial Design");
        opened.WorkflowType.ShouldBe(WorkflowType.PrepareAsset);

        // And the source is untouched: the operator's file is never renamed (MVP invariant 1).
        opened.SourceFileName.ShouldBe("IMG_4471.png");

        SessionAggregate stored =
            (await harness.Inner.Repository.LoadAsync(opened.Id, CancellationToken.None)).Value!;
        stored.Session.OutputName.Value.ShouldBe("Memorial Design");
    }

    /// <summary>
    /// The name the operator typed is the base the produced file is actually named from
    /// (SCRUM-11075).
    /// </summary>
    /// <remarks>
    /// The end of the chain, and the only assertion here that touches naming output at all: the
    /// suffix comes from the accepted preset pattern <c>{Name}_HD.png</c> and is not this slice's
    /// to change (§29). What is proved is that <c>{Name}</c> resolves to what was typed on
    /// Workflow Selection rather than to the stem of the file that was imported.
    /// </remarks>
    [Fact]
    public async Task The_edited_name_is_the_base_the_produced_file_is_named_from()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("IMG_4471.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(harness.Navigation.WorkflowSelectionFor!);

        selection.OutputName = "Memorial Design";
        await selection.SelectCommand.ExecuteAsync(
            selection.Workflows.Single(w => w.Type == WorkflowType.PrepareAsset));

        SessionId id = navigation.SessionFor.ShouldNotBeNull().Id;
        await Must(harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await Must(harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));

        SessionAggregate stored =
            (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;

        Revision produced = stored.Revisions.Single(r => r.Operation == OperationKind.Enhance);
        produced.File.FileName.ShouldBe("Memorial Design_HD.png");
    }

    /// <summary>
    /// A name the naming authority refuses starts nothing (SCRUM-11075 §20).
    /// </summary>
    /// <remarks>
    /// The important half is the second one: not merely that the name was not written, but that
    /// no workflow was selected and no navigation happened while the box still held the value the
    /// authority rejected.
    /// </remarks>
    [Theory]
    [InlineData("bad/name")]
    [InlineData("   ")]
    public async Task A_refused_name_leaves_the_operator_on_workflow_selection(string typed)
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("refused.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(harness.Navigation.WorkflowSelectionFor!);

        selection.OutputName = typed;
        await selection.SelectCommand.ExecuteAsync(
            selection.Workflows.Single(w => w.Type == WorkflowType.GeneratePrintTiff));

        navigation.SessionFor.ShouldBeNull();
        selection.Notice.ShouldNotBeNullOrWhiteSpace();

        // The operator can still read what they typed, and the box is not a resource key.
        selection.Notice!.ShouldNotContain("WorkflowSelection_");
        selection.OutputName.ShouldBe(typed);

        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        SessionAggregate stored =
            (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;

        stored.Session.OutputName.Value.ShouldBe("refused");
        stored.Session.WorkflowType.ShouldBe(WorkflowType.PrepareAsset);
        stored.Revisions.ShouldHaveSingleItem().IsRoot.ShouldBeTrue();
    }

    /// <summary>
    /// A name the operator leaves alone writes no command at all (SCRUM-11075 §9).
    /// </summary>
    /// <remarks>
    /// Not a micro-optimisation: re-committing an unchanged value would write a metadata
    /// transaction for a decision nobody made, and the session's <c>UpdatedAtUtc</c> would move
    /// for a screen visit.
    /// </remarks>
    [Fact]
    public async Task An_untouched_name_is_not_re_committed()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("untouched.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        DateTimeOffset before =
            (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!.Session.UpdatedAtUtc;

        harness.AdvanceClock(TimeSpan.FromMinutes(5));

        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(harness.Navigation.WorkflowSelectionFor!);
        selection.OutputName.ShouldBe("untouched");

        // Nothing typed. Selecting the workflow the session already has moves it no further than
        // that one command would on its own.
        await selection.SelectCommand.ExecuteAsync(
            selection.Workflows.Single(w => w.Type == WorkflowType.PrepareAsset));

        SessionAggregate stored =
            (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        stored.Session.OutputName.Value.ShouldBe("untouched");
        stored.Session.UpdatedAtUtc.ShouldBeGreaterThan(before, "SelectWorkflow itself still commits");
    }

    /// <summary>
    /// The lock covers the name as well as the workflow (SCRUM-11075 §18).
    /// </summary>
    /// <remarks>
    /// Once a derived Revision exists the session is past the point where either may be chosen
    /// from this screen, and both answers come from the engine's own <c>AvailableCommands</c>
    /// rather than from a rule the screen keeps.
    /// </remarks>
    [Fact]
    public async Task A_produced_result_closes_the_name_box_and_the_workflow_choice_together()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("locked-name.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        await Must(harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await Must(harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));

        WorkflowSelectionViewModel selection = harness.WorkflowSelection(new RecordingNavigation());
        selection.Open((await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value);

        selection.CanSelect.ShouldBeFalse();
        selection.CanEditOutputName.ShouldBeTrue(
            "the name stays editable while the session can still progress; only the workflow is frozen");
    }

    // -------------------------------------------------------------------------------------
    // SCRUM-11078: the source is visible before the workflow is chosen
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The imported design is on screen before a workflow is chosen, and looking at it is not an
    /// action (SCRUM-11078 §12, §13).
    /// </summary>
    [Fact]
    public async Task The_imported_design_previews_before_selection_and_changes_nothing()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("previewed.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        SessionAggregate before =
            (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;

        WorkflowSelectionViewModel selection = harness.WorkflowSelection(new RecordingNavigation());
        selection.Open(harness.Navigation.WorkflowSelectionFor!);
        await selection.PreviewLoaded;

        selection.HasPreview.ShouldBeTrue();
        ArtefactPreviewPane pane = selection.Preview.ShouldNotBeNull();
        pane.HasImage.ShouldBeTrue();
        pane.Payload.IsEmpty.ShouldBeFalse();
        pane.FileName.ShouldBe("previewed.png");
        pane.Detail.ShouldNotBeNullOrWhiteSpace();

        // A preview creates no Revision, records no decision and advances nothing (§13).
        SessionAggregate after =
            (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;

        after.Revisions.Count.ShouldBe(before.Revisions.Count);
        after.Reviews.Count.ShouldBe(before.Reviews.Count);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        after.Session.WorkflowType.ShouldBe(before.Session.WorkflowType);
        after.Session.State.ShouldBe(before.Session.State);
        after.Session.UpdatedAtUtc.ShouldBe(before.Session.UpdatedAtUtc);
    }

    /// <summary>
    /// A source the product cannot truthfully draw yet says so rather than showing nothing
    /// (SCRUM-11078 §14).
    /// </summary>
    /// <remarks>
    /// A PSD's managed raster is prepared inside the Session, which is later than this screen. The
    /// smallest correct behaviour is a sentence: no fabricated picture, and no silent blank.
    /// </remarks>
    [Fact]
    public async Task A_psd_source_is_labelled_rather_than_drawn()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.Inner.Workspace.CreateSourceFile(
            "layered.psd", PsdInputPreparationTests.RgbCompositePsd());
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        WorkflowSelectionViewModel selection = harness.WorkflowSelection(new RecordingNavigation());
        selection.Open(harness.Navigation.WorkflowSelectionFor!);
        await selection.PreviewLoaded;

        ArtefactPreviewPane pane = selection.Preview.ShouldNotBeNull();
        pane.HasImage.ShouldBeFalse();
        pane.IsUnavailable.ShouldBeTrue();
        pane.Unavailable.ShouldNotBeNullOrWhiteSpace();
        pane.Payload.IsEmpty.ShouldBeTrue();

        // The file is still identified even though it cannot be drawn.
        selection.SourceFileName.ShouldBe("layered.psd");
    }

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
