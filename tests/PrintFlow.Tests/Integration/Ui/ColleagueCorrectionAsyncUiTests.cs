using System.IO;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using static PrintFlow.Tests.Fixtures.CorrectionFixtures;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>Deterministic late-answer tests: real temporary database/files, gated responses, no native picker.</summary>
[Collection(SqliteCollection.Name)]
public sealed class ColleagueCorrectionAsyncUiTests
{
    [Theory]
    [InlineData("prepare", false)]
    [InlineData("import", false)]
    [InlineData("repair", false)]
    [InlineData("prepare", true)]
    [InlineData("import", true)]
    [InlineData("repair", true)]
    public async Task A_late_correction_answer_cannot_replace_a_newer_request_on_the_same_session(string action, bool fail)
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        var setup = await SetupAsync(h, action);
        Gate answer = setup.Gated.Hold(action, fail);
        Task pending = Start(setup.Screen, action);
        await answer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        SessionView newer = await NextRequestAsync(h, setup.Service, setup.Initial.Id);
        setup.Screen.Open(newer);
        await setup.Screen.PreviewsLoaded;
        string? identity = setup.Screen.ImportIdentity;
        int focus = setup.Screen.CorrectionFocusToken;
        setup.Screen.Notice = "newer screen notice";

        answer.Release.TrySetResult();
        await pending;

        setup.Screen.ImportIdentity.ShouldBe(identity);
        setup.Screen.CorrectionHandoff.ShouldNotBeNull().RequestId.ShouldBe(newer.Correction!.RequestId);
        setup.Screen.CorrectionFocusToken.ShouldBe(focus, "a retired operation must not move focus");
        setup.Screen.CorrectionMessage.ShouldBeNull();
        setup.Screen.Notice.ShouldBe("newer screen notice");
        setup.Screen.IsBusy.ShouldBeFalse();
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("import")]
    [InlineData("repair")]
    public async Task A_late_error_reload_cannot_replace_a_newer_request_on_the_same_session(string action)
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        var setup = await SetupAsync(h, action);
        setup.Gated.FailNextAction = true; // Simulates an unknown outcome after the real commit.
        Gate reload = setup.Gated.Hold("load");
        Task pending = Start(setup.Screen, action);
        await reload.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        SessionView newer = await NextRequestAsync(h, setup.Service, setup.Initial.Id);
        setup.Screen.Open(newer);
        await setup.Screen.PreviewsLoaded;
        int focus = setup.Screen.CorrectionFocusToken;
        setup.Screen.Notice = "newer screen notice";

        reload.Release.TrySetResult();
        await pending;

        setup.Screen.CorrectionHandoff.ShouldNotBeNull().RequestId.ShouldBe(newer.Correction!.RequestId);
        setup.Screen.CorrectionMessage.ShouldBeNull();
        setup.Screen.CorrectionFocusToken.ShouldBe(focus);
        setup.Screen.Notice.ShouldBe("newer screen notice");
    }

    [Theory]
    [InlineData("prepare", false)]
    [InlineData("import", false)]
    [InlineData("repair", false)]
    [InlineData("prepare", true)]
    [InlineData("import", true)]
    [InlineData("repair", true)]
    public async Task Refreshing_the_same_target_keeps_the_pending_result_and_unknown_outcome_reload(string action, bool fail)
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        var setup = await SetupAsync(h, action);
        Gate answer = setup.Gated.Hold(action, fail);
        Task pending = Start(setup.Screen, action);
        await answer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Language refresh rebuilds the same view. Reference identity must not cancel the action.
        setup.Screen.Open(setup.Initial with { });
        await setup.Screen.PreviewsLoaded;
        if (action == "prepare") setup.Screen.CorrectionNote.ShouldBe("keep my note");

        answer.Release.TrySetResult();
        await pending;

        SessionView actual = await MustAsync(setup.Service.LoadAsync(setup.Initial.Id, default));
        setup.Screen.IsBusy.ShouldBeFalse();
        setup.Screen.CorrectionActivity.ShouldBe(CorrectionActivity.Idle);
        if (action == "import")
        {
            setup.Screen.IsReviewRequired.ShouldBeTrue();
            setup.Screen.ReviewTargetIdentity!.ShouldContain(actual.CurrentArtefact!.RevisionId.ToString(), Case.Insensitive);
            setup.Screen.ShowsCorrectionPanel.ShouldBeFalse();
        }
        else
        {
            setup.Screen.ShowsCorrectionPanel.ShouldBeTrue();
            setup.Screen.CorrectionHandoff!.RequestId.ShouldBe(actual.Correction!.RequestId);
            setup.Screen.ShowsCorrectionMissingFiles.ShouldBeFalse();
            if (action == "prepare") setup.Screen.IsAskingColleague.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Returning_from_a_picker_for_an_old_request_does_not_submit_or_move_focus()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        var setup = await SetupAsync(h, "import");
        SessionView newer = await NextRequestAsync(h, setup.Service, setup.Initial.Id);
        int focus = setup.Screen.CorrectionFocusToken;
        setup.Picker.OnPick = () => setup.Screen.Open(newer);

        await setup.Screen.ImportCorrectedImageCommand.ExecuteAsync(null);

        setup.Gated.ImportCalls.ShouldBe(0, "the selected file belonged to a retired picker interaction");
        setup.Screen.CorrectionHandoff!.RequestId.ShouldBe(newer.Correction!.RequestId);
        setup.Screen.CorrectionFocusToken.ShouldBe(focus);
        setup.Screen.CorrectionMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Finishing_a_retired_prepare_does_not_clear_a_new_imports_busy_state()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        var setup = await SetupAsync(h, "prepare");
        Gate oldAnswer = setup.Gated.Hold("prepare");
        Task oldPending = Start(setup.Screen, "prepare");
        await oldAnswer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        SessionView newer = await NextRequestAsync(h, setup.Service, setup.Initial.Id);
        setup.Screen.Open(newer);
        await setup.Screen.PreviewsLoaded;
        setup.Screen.CanImportCorrectedImage.ShouldBeTrue("the old operation no longer owns this screen");
        Gate newAnswer = setup.Gated.Hold("import");
        Task newPending = Start(setup.Screen, "import");
        await newAnswer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        oldAnswer.Release.TrySetResult();
        await oldPending;

        setup.Screen.IsBusy.ShouldBeTrue();
        setup.Screen.CorrectionActivity.ShouldBe(CorrectionActivity.Checking);
        newAnswer.Release.TrySetResult();
        await newPending;
        setup.Screen.IsBusy.ShouldBeFalse();
    }

    private static Task Start(SessionViewModel screen, string action) => action switch
    {
        "prepare" => screen.PrepareCorrectionFilesCommand.ExecuteAsync(null),
        "import" => screen.ImportCorrectedImageCommand.ExecuteAsync(null),
        _ => screen.RepairCorrectionFilesCommand.ExecuteAsync(null),
    };

    private static async Task<(SessionService Service, GatedService Gated, SessionViewModel Screen, SessionView Initial, Picker Picker)>
        SetupAsync(SessionServiceHarness h, string action)
    {
        SessionService service = Service(h);
        SessionView initial = await AtBackgroundRemovalReviewAsync(h, service);
        if (action != "prepare") initial = await MustAsync(RequestAsync(service, initial));
        if (action == "repair")
        {
            string reference = Path.Combine(initial.Correction!.FolderPath, initial.Correction.ReferenceFileName);
            File.SetAttributes(reference, File.GetAttributes(reference) & ~FileAttributes.ReadOnly);
            File.Delete(reference);
            initial = await MustAsync(service.LoadAsync(initial.Id, default));
            initial.Correction!.MissingFiles.ShouldBeTrue();
        }
        GatedService gated = new(service);
        Picker picker = new() { Selected = CorrectedPng(h) };
        SessionViewModel screen = new(gated, h.Previews, h.TiffReviews, new RecordingNavigation(), filePicker: picker);
        screen.Open(initial);
        await screen.PreviewsLoaded;
        if (action == "prepare")
        {
            screen.BeginAskColleagueCommand.Execute(null);
            screen.CorrectionNote = "keep my note";
        }
        return (service, gated, screen, initial, picker);
    }

    private static async Task<SessionView> NextRequestAsync(SessionServiceHarness h, SessionService service, SessionId id)
    {
        SessionView current = await MustAsync(service.LoadAsync(id, default));
        if (current.Correction is { CanImport: true } handoff)
            current = await MustAsync(service.ImportCorrectedImageAsync(id, handoff.RequestId,
                handoff.HandedOutRevisionId, handoff.HandedOutSha256, CorrectedPng(h, "next.png"), "qa", default));
        return await MustAsync(RequestAsync(service, current));
    }

    private sealed class Picker : IFilePicker
    {
        public string? Selected { get; init; }
        public Action? OnPick { get; set; }
        public string? PickSingleFile(string title, string filter) => PickSingleFile(title, filter, null);
        public string? PickSingleFile(string title, string filter, string? folder)
        {
            OnPick?.Invoke();
            return Selected;
        }
    }

    private sealed class Gate(string action, bool fail)
    {
        public string Action { get; } = action;
        public bool Fail { get; } = fail;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Only delays delivery (or loses the success answer); real calls still commit and load.</summary>
    private sealed class GatedService(ISessionService inner) : ISessionService
    {
        private readonly Queue<Gate> _gates = new();
        public bool FailNextAction { get; set; }
        public int ImportCalls { get; private set; }
        public Gate Hold(string action, bool fail = false)
        {
            Gate gate = new(action, fail);
            _gates.Enqueue(gate);
            return gate;
        }
        private async Task<OperationResult<SessionView>> Deliver(string action, Task<OperationResult<SessionView>> call)
        {
            Gate? gate = _gates.TryPeek(out Gate? next) && next.Action == action ? _gates.Dequeue() : null;
            bool fail = gate?.Fail == true || (action != "load" && FailNextAction);
            if (action != "load") FailNextAction = false;
            OperationResult<SessionView> result = await call;
            if (gate is not null)
            {
                gate.Started.TrySetResult();
                await gate.Release.Task;
            }
            return fail ? OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, "Lost action response.") : result;
        }
        public Task<OperationResult<SessionView>> RequestColleagueCorrectionAsync(SessionId id, Guid request, RevisionId revision,
            Sha256 hash, string? note, CorrectionFileNaming naming, string? op, CancellationToken token) =>
            Deliver("prepare", inner.RequestColleagueCorrectionAsync(id, request, revision, hash, note, naming, op, token));
        public Task<OperationResult<SessionView>> RepairCorrectionFilesAsync(SessionId id, Guid request, CorrectionFileNaming naming, CancellationToken token) =>
            Deliver("repair", inner.RepairCorrectionFilesAsync(id, request, naming, token));
        public Task<OperationResult<SessionView>> ImportCorrectedImageAsync(SessionId id, Guid request, RevisionId? revision,
            Sha256? hash, string path, string? op, CancellationToken token)
        {
            ImportCalls++;
            return Deliver("import", inner.ImportCorrectedImageAsync(id, request, revision, hash, path, op, token));
        }
        public Task<OperationResult<SessionView>> LoadAsync(SessionId id, CancellationToken token) => Deliver("load", inner.LoadAsync(id, token));
        public event EventHandler<AutomationRuntimeView>? AutomationRuntimeChanged
        {
            add => inner.AutomationRuntimeChanged += value;
            remove => inner.AutomationRuntimeChanged -= value;
        }
        public AutomationRuntimeView GetAutomationRuntime(SessionId id) => inner.GetAutomationRuntime(id);
        public OperationResult<PrintFlow.Domain.Results.Unit> RequestStop(SessionId id, AutomationStopMode mode) => inner.RequestStop(id, mode);
        public Task<OperationResult<SessionView>> ImportAsync(WorkflowType type, string path, string? name, string? op, CancellationToken token) => inner.ImportAsync(type, path, name, op, token);
        public Task<OperationResult<SessionView>> ExecuteAsync(SessionId id, WorkflowCommand command, string? op, CancellationToken token) => inner.ExecuteAsync(id, command, op, token);
        public Task<OperationResult<SessionView>> AuthoriseCurrentEnlargementAsync(SessionId id, Guid offer, string? op, CancellationToken token) => inner.AuthoriseCurrentEnlargementAsync(id, offer, op, token);
        public Task<OperationResult<IReadOnlyList<RecoveryItem>>> ListRecoveryAsync(CancellationToken token) => inner.ListRecoveryAsync(token);
        public Task<OperationResult<SessionView>> ResolveRecoveryAsync(SessionId id, RecoveryAction action, string? path, string? op, CancellationToken token) => inner.ResolveRecoveryAsync(id, action, path, op, token);
        public Task<OperationResult<PrintFlow.Workflow.Services.ErrorDetailsView>> LoadErrorDetailsAsync(SessionId id, AttemptId attempt, CancellationToken token) => inner.LoadErrorDetailsAsync(id, attempt, token);
        public Task<OperationResult<SessionView>> ResolveErrorRecoveryAsync(SessionId id, AttemptId attempt, ErrorRecoveryAction action, string? op, CancellationToken token) => inner.ResolveErrorRecoveryAsync(id, attempt, action, op, token);
        public Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(CancellationToken token) => inner.ListRecentAsync(token);
        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> RemoveFromRecentAsync(SessionId id, CancellationToken token) => inner.RemoveFromRecentAsync(id, token);
    }
}
