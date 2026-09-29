using System.Globalization;
using System.IO;
using PrintFlow.App.Localisation;
using PrintFlow.App.Resources;
using PrintFlow.App.Settings;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using static PrintFlow.Tests.Fixtures.CorrectionFixtures;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// SCRUM-11151: a failure's main sentence carries no bare code, the exact code of the failure it
/// describes stays reachable under Error details, and the only next step named is one the screen
/// offers. Headless view models over the real session service; scripted failures never reach the
/// database, so a test can prove that showing, re-wording or refreshing a notice changed nothing.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class FailureGuidanceUiTests
{
    [Theory]
    [InlineData("en", "Failure_MeituInterrupted", FailureCode.Cancelled)]
    [InlineData("zh-CN", "Failure_MeituInterrupted", FailureCode.Cancelled)]
    [InlineData("en", "Failure_OperationFaulted", FailureCode.AdapterUnavailable)]
    [InlineData("zh-CN", "Failure_OperationFaulted", FailureCode.AdapterUnavailable)]
    public async Task A_specialized_processing_failure_does_not_advise_retry_when_its_reload_fails(
        string language, string key, FailureCode code)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        var (service, screen, _) = await AtOriginalConfirmationAsync(h);
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        screen.CanRunStep.ShouldBeTrue();
        service.NextExecuteFailure = OperationFailure.Create(code, "Scripted processor failure.", isRetryable: true, messageKey: key);
        service.FailLoadsAfterScriptedFailure = true;

        await screen.RunStepCommand.ExecuteAsync(null);

        screen.CanRetry.ShouldBeFalse();
        screen.NoticeErrorCode.ShouldBe(code.ToString());
        screen.Notice!.ShouldContain(Strings.Session_ActionFailedNextStale);
        screen.Notice!.ShouldNotContain(language == "en" ? "retry" : "重试");
        if (key == "Failure_MeituInterrupted") screen.Notice!.ShouldContain(language == "en" ? "Meitu" : "美图");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task Stop_take_over_and_continue_size_refusals_keep_their_own_diagnostics(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        SessionView imported = await ImportAsync(h, "alternate-actions");
        ScriptedFailureSessionService service = new(h.CreateService())
        {
            Runtime = new(imported.Id, null, StepKind.Enhancement, AutomationRuntimeState.Running,
                ExternalOperationPhase.NotStarted, true),
        };
        SessionViewModel screen = new(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        screen.Open(imported);
        await screen.PreviewsLoaded;
        SessionAggregate before = await RecoverySurfaceTests.Load(h, imported.Id);
        screen.CanStopAutomation.ShouldBeTrue();
        screen.CanTakeOverAutomation.ShouldBeTrue();
        service.NextStopFailure = OperationFailure.Create(FailureCode.PreconditionNotMet, "The run already ended.");
        screen.StopAutomationCommand.Execute(null);
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.PreconditionNotMet));
        screen.Notice.ShouldBe(Composed(Strings.Session_ActionFailed, Strings.Failure_PreconditionNotMet, Strings.Session_ActionFailedNext));

        screen.BeginTakeOverCommand.Execute(null);
        service.NextStopFailure = OperationFailure.Create(FailureCode.Cancelled, "Scripted takeover refusal.");
        screen.ConfirmTakeOverCommand.Execute(null);
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.Cancelled));
        screen.Notice!.ShouldNotContain(nameof(FailureCode.Cancelled));
        service.StopCalls.ShouldBe(2);
        screen.IsConfirmingTakeOver.ShouldBeFalse();

        // A read model with an offered enlargement; the intercepted refusal never reaches a write.
        service.Runtime = AutomationRuntimeView.Idle;
        screen.Open(imported with { Sizing = imported.Sizing with { CanAuthoriseEnlargement = true, EnlargementOfferId = Guid.NewGuid() } });
        await screen.PreviewsLoaded;
        screen.CanAuthoriseEnlargement.ShouldBeTrue();
        service.NextEnlargementFailure = OperationFailure.Create(FailureCode.PersistenceError, "Scripted authorization failure.");
        await screen.ContinueWithSizeCommand.ExecuteAsync(null);
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.PersistenceError));
        screen.Notice.ShouldBe(Composed(Strings.Session_ActionUnconfirmed, Strings.Failure_PersistenceError, Strings.Session_ActionFailedNext));
        service.ExecuteCalls.ShouldBe(1);
        (await RecoverySurfaceTests.Load(h, imported.Id)).Session.ShouldBe(before.Session);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task Recovery_home_open_and_error_details_back_failures_are_read_only_and_keep_their_own_code(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        SessionId recovering = await RecoverySurfaceTests.Seed(h, "home-open");
        ScriptedFailureSessionService service = new(h.CreateService());
        RecordingNavigation navigation = new();
        HomeViewModel home = Home(service, h, navigation);
        await home.RefreshCommand.ExecuteAsync(null);
        RecoverySessionRow row = home.RecoverySessions.Single(r => r.Id == recovering);
        service.LoadFailure = OperationFailure.Create(FailureCode.PersistenceError, "Read failed.");
        await home.OpenRecoveryCommand.ExecuteAsync(row);
        home.NoticeErrorCode.ShouldBe(nameof(FailureCode.PersistenceError));
        home.Notice.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.Home_ResumeFailed, row.OpenLabel));
        navigation.SessionFor.ShouldBeNull();
        service.MutatingCalls.ShouldBe(0);

        SessionId failed = await FailedEnhancementAsync(h);
        service.LoadFailure = null;
        AttemptId attempt = (await service.LoadAsync(failed, default)).Value.CurrentFailureAttemptId!.Value;
        ErrorDetailsViewModel details = new(service, navigation, new LocalisationService(h.Settings));
        await details.OpenAsync(failed, attempt, default);
        service.LoadFailure = OperationFailure.Create(FailureCode.WorkspaceError, "Back could not read the job.");
        await details.BackCommand.ExecuteAsync(null);
        details.NoticeErrorCode.ShouldBe(nameof(FailureCode.WorkspaceError));
        details.Notice.ShouldBe(Strings.Failure_WorkspaceError);
        details.Code.ShouldBe(nameof(FailureCode.Timeout));
        navigation.SessionFor.ShouldBeNull();
        service.MutatingCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("en", "zh-CN")]
    [InlineData("zh-CN", "en")]
    public async Task Correction_diagnostics_follow_the_message_and_language_without_mutating_the_job(string from, string to)
    {
        using OperatorCultureScope culture = new(from);
        using SessionServiceHarness h = new();
        SessionService real = Service(h);
        SessionView initial = await AtBackgroundRemovalReviewAsync(h, real);
        ScriptedFailureSessionService service = new(real);
        LocalisationService localisation = new(h.Settings);
        localisation.Use(Language(from));
        SessionViewModel screen = new(service, h.Previews, h.TiffReviews, new RecordingNavigation(), localisation: localisation);
        screen.Open(initial);
        await screen.PreviewsLoaded;
        screen.BeginAskColleagueCommand.Execute(null);
        service.NextPrepareFailure = OperationFailure.Create(FailureCode.WorkspaceError, "Scripted preparation failure.");
        await screen.PrepareCorrectionFilesCommand.ExecuteAsync(null);
        screen.CorrectionErrorCode.ShouldBe(nameof(FailureCode.WorkspaceError));
        int writes = service.MutatingCalls, loads = service.LoadCalls;
        SessionAggregate before = await RecoverySurfaceTests.Load(h, initial.Id);
        localisation.Use(Language(to));
        screen.CorrectionMessage.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.Session_CorrectionPrepareFailed, Strings.Failure_WorkspaceError));
        screen.CorrectionErrorCode.ShouldBe(nameof(FailureCode.WorkspaceError));
        service.MutatingCalls.ShouldBe(writes);
        service.LoadCalls.ShouldBe(loads);
        (await RecoverySurfaceTests.Load(h, initial.Id)).Session.ShouldBe(before.Session);
        screen.CancelAskColleagueCommand.Execute(null);
        screen.CorrectionErrorCode.ShouldBeNull();
    }

    // --- Session: the Session_ActionFailed wrapper -------------------------------------------

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task A_failed_session_action_keeps_the_code_out_of_the_main_sentence(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        (ScriptedFailureSessionService service, SessionViewModel screen, SessionId id) = await AtOriginalConfirmationAsync(h);
        SessionAggregate before = await RecoverySurfaceTests.Load(h, id);
        var commandsBefore = Commands(screen);
        service.NextExecuteFailure = OperationFailure.Create(FailureCode.PreconditionNotMet, "Scripted refusal.");

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);

        screen.Notice.ShouldBe(Composed(Strings.Session_ActionFailed, Strings.Failure_PreconditionNotMet, Strings.Session_ActionFailedNext));
        screen.Notice!.ShouldNotContain(nameof(FailureCode.PreconditionNotMet));
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.PreconditionNotMet));
        screen.HasNoticeErrorCode.ShouldBeTrue();

        // Retry is not legal here, and nothing on screen suggests it.
        screen.CanRetry.ShouldBeFalse();
        screen.Notice!.ShouldNotContain(screen.RetryLabel);
        screen.NextStepText.ShouldNotContain(screen.RetryLabel);

        // Presentation only: commands and the persisted job are exactly as before.
        Commands(screen).ShouldBe(commandsBefore);
        (await RecoverySurfaceTests.Load(h, id)).Session.ShouldBe(before.Session);
        service.ExecuteCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task A_persistence_failure_is_worded_as_unconfirmed_rather_than_as_not_done(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        (ScriptedFailureSessionService service, SessionViewModel screen, _) = await AtOriginalConfirmationAsync(h);
        service.NextExecuteFailure = OperationFailure.Create(FailureCode.PersistenceError, "Scripted commit failure.");

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);

        screen.Notice.ShouldBe(Composed(Strings.Session_ActionUnconfirmed, Strings.Failure_PersistenceError, Strings.Session_ActionFailedNext));
        screen.Notice.ShouldNotStartWith(string.Format(CultureInfo.CurrentCulture, Strings.Session_ActionFailed, string.Empty).Trim());
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.PersistenceError));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task When_the_job_cannot_be_read_again_the_notice_does_not_point_at_a_possibly_stale_status(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        (ScriptedFailureSessionService service, SessionViewModel screen, _) = await AtOriginalConfirmationAsync(h);
        service.FailLoadsAfterScriptedFailure = true;
        service.NextExecuteFailure = OperationFailure.Create(FailureCode.WorkspaceError, "Scripted file failure.");

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);

        screen.Notice.ShouldBe(Composed(Strings.Session_ActionFailed, Strings.Failure_WorkspaceError, Strings.Session_ActionFailedNextStale));
        screen.Notice!.ShouldNotContain(Strings.Session_ActionFailedNext);
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.WorkspaceError));
    }

    /// <summary>
    /// A real Timeout from the fake Meitu adapter: Retry is legal, and it is named only by the status
    /// line, which is built from the legal commands. The stored step failure's code is on the existing
    /// Error Details screen; the notice's own code is under its in-place Error details. Both are the
    /// same failure here, and both are exact.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task A_real_timeout_names_retry_only_through_the_status_line_and_its_code_is_reachable_twice(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        RecordingNavigation navigation = new();
        (ScriptedFailureSessionService service, SessionViewModel screen, SessionId id) = await AtOriginalConfirmationAsync(h, navigation);
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        screen.CanRunStep.ShouldBeTrue();
        h.FakeMeitu.SetScenario(FakeAdapterScenario.Timeout);

        await screen.RunStepCommand.ExecuteAsync(null);

        screen.Notice.ShouldBe(Composed(Strings.Session_ActionFailed, Strings.Failure_Timeout, Strings.Session_ActionFailedNext));
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.Timeout));
        screen.Notice!.ShouldNotContain(screen.RetryLabel);
        screen.CanRetry.ShouldBeTrue();
        screen.NextStepText.ShouldContain(screen.RetryLabel);
        screen.StatusReason.ShouldBe(Strings.Failure_Timeout);
        screen.StatusReason.ShouldNotContain(nameof(FailureCode.Timeout));

        SessionView stored = (await service.LoadAsync(id, CancellationToken.None)).Value;
        AttemptId attempt = stored.CurrentFailureAttemptId.ShouldNotBeNull();
        ErrorDetailsView details = (await service.LoadErrorDetailsAsync(id, attempt, CancellationToken.None)).Value;
        details.StableCode.ShouldBe(nameof(FailureCode.Timeout));
        screen.CanOpenErrorDetails.ShouldBeTrue();
        await screen.OpenErrorDetailsCommand.ExecuteAsync(null);
        navigation.ErrorDetailsFor.ShouldBe((id, attempt));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task A_correction_handoff_failure_never_suggests_retry_or_run_step(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        SessionService real = Service(h);
        SessionView initial = await MustAsync(RequestAsync(real, await AtBackgroundRemovalReviewAsync(h, real)));
        string reference = Path.Combine(initial.Correction!.FolderPath, initial.Correction.ReferenceFileName);
        File.SetAttributes(reference, File.GetAttributes(reference) & ~FileAttributes.ReadOnly);
        File.Delete(reference);
        initial = await MustAsync(real.LoadAsync(initial.Id, default));
        ScriptedFailureSessionService service = new(real);
        SessionViewModel screen = new(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        screen.Open(initial);
        await screen.PreviewsLoaded;
        SessionAggregate before = await RecoverySurfaceTests.Load(h, initial.Id);
        service.NextRepairFailure = OperationFailure.Create(FailureCode.WorkspaceError, "Scripted repair failure.");

        await screen.RepairCorrectionFilesCommand.ExecuteAsync(null);

        screen.Notice.ShouldBe(Composed(Strings.Session_ActionFailed, Strings.Failure_WorkspaceError, Strings.Session_ActionFailedNext));
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.WorkspaceError));
        screen.NextStepText.ShouldContain(screen.CorrectionImportLabel);
        foreach (string label in new[] { screen.RetryLabel, screen.RunStepLabel })
        {
            screen.Notice!.ShouldNotContain(label);
            screen.NextStepText.ShouldNotContain(label);
        }

        screen.CanRetry.ShouldBeFalse();
        screen.CanRunStep.ShouldBeFalse();
        screen.CanImportCorrectedImage.ShouldBeTrue("the colleague-correction return is unchanged");
        (await RecoverySurfaceTests.Load(h, initial.Id)).CorrectionRequests.ShouldBe(before.CorrectionRequests);
    }

    [Fact]
    public async Task A_later_notice_or_failure_never_keeps_an_earlier_code()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        (ScriptedFailureSessionService service, SessionViewModel screen, _) = await AtOriginalConfirmationAsync(h);
        service.NextExecuteFailure = OperationFailure.Create(FailureCode.PreconditionNotMet, "First.");
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.PreconditionNotMet));

        service.NextExecuteFailure = OperationFailure.Create(FailureCode.OutputMissing, "Second.");
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.OutputMissing));
        screen.Notice!.ShouldContain(Strings.Failure_OutputMissing);

        screen.Notice = Strings.Session_TargetSizeInvalid;
        screen.NoticeErrorCode.ShouldBeNull();
        screen.HasNoticeErrorCode.ShouldBeFalse();
    }

    [Theory]
    [InlineData("en", "zh-CN")]
    [InlineData("zh-CN", "en")]
    public async Task Changing_language_rewords_the_notice_and_touches_nothing_else(string from, string to)
    {
        using OperatorCultureScope culture = new(from);
        using SessionServiceHarness h = new();
        LocalisationService localisation = new(h.Settings);
        localisation.Use(Language(from));
        (ScriptedFailureSessionService service, SessionViewModel screen, SessionId id) =
            await AtOriginalConfirmationAsync(h, localisation: localisation);
        service.NextExecuteFailure = OperationFailure.Create(FailureCode.Timeout, "Scripted timeout.", isRetryable: true);
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        string first = screen.Notice!;
        var commands = Commands(screen);
        SessionAggregate before = await RecoverySurfaceTests.Load(h, id);
        int mutating = service.MutatingCalls, loads = service.LoadCalls;

        localisation.Use(Language(to));

        screen.Notice.ShouldBe(Composed(Strings.Session_ActionFailed, Strings.Failure_Timeout, Strings.Session_ActionFailedNext));
        screen.Notice.ShouldNotBe(first);
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.Timeout));
        service.MutatingCalls.ShouldBe(mutating);
        service.LoadCalls.ShouldBe(loads, "re-wording reads nothing");
        Commands(screen).ShouldBe(commands);
        (await RecoverySurfaceTests.Load(h, id)).Session.ShouldBe(before.Session);
    }

    // --- Home: no session is open, and the code is the failure's own ------------------------

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task A_job_that_cannot_be_opened_says_opening_changed_nothing_and_names_the_same_button(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        await ImportAsync(h, "resume");
        ScriptedFailureSessionService service = new(h.CreateService());
        RecordingNavigation navigation = new();
        HomeViewModel home = Home(service, h, navigation);
        await home.RefreshCommand.ExecuteAsync(null);
        RecentSessionRow row = home.RecentSessions.Single();
        service.LoadFailure = OperationFailure.Create(FailureCode.PersistenceError, "Scripted read failure.");

        await home.ResumeCommand.ExecuteAsync(row);

        home.Notice.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.Home_ResumeFailed, row.OpenActionLabel));
        home.Notice!.ShouldNotContain(nameof(FailureCode.PersistenceError));
        home.NoticeErrorCode.ShouldBe(nameof(FailureCode.PersistenceError));
        service.MutatingCalls.ShouldBe(0);
        navigation.SessionFor.ShouldBeNull();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task An_abandon_failure_shows_its_own_code_and_not_the_jobs_earlier_failure(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        SessionId id = await FailedEnhancementAsync(h);
        ScriptedFailureSessionService service = new(h.CreateService());
        HomeViewModel home = Home(service, h, new RecordingNavigation());
        await home.RefreshCommand.ExecuteAsync(null);
        RecentSessionRow row = home.RecentSessions.Single(r => r.Id == id);
        row.CanAbandon.ShouldBeTrue();
        service.NextExecuteFailure = OperationFailure.Create(FailureCode.PreconditionNotMet, "Scripted refusal.");

        await home.AbandonCommand.ExecuteAsync(row);

        home.Notice.ShouldBe(Strings.Home_AbandonFailed);
        home.NoticeErrorCode.ShouldBe(nameof(FailureCode.PreconditionNotMet), "the job's stored Timeout is a different failure");
        SessionAggregate after = await RecoverySurfaceTests.Load(h, id);
        after.Session.State.ShouldBe(SessionState.Active);
        after.Attempts.Single(a => a.Status == AttemptStatus.Failed).Failure!.Code.ShouldBe(FailureCode.Timeout);
        after.Snapshot.ShouldNotBeNull();
        File.Exists(after.Snapshot!.OriginalSourcePath).ShouldBeTrue("the customer's original is untouched");
        File.Exists(h.FileWorkspace.ResolveAbsolute(after.Revisions.Single(r => r.Id == after.Snapshot.RootRevisionId).File))
            .ShouldBeTrue("abandoning never deletes files");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task A_recovery_failure_warns_against_repeating_it_and_keeps_its_own_code(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        SessionId id = await RecoverySurfaceTests.Seed(h, "recover");
        ScriptedFailureSessionService service = new(h.CreateService());
        HomeViewModel home = Home(service, h, new RecordingNavigation());
        await home.RefreshCommand.ExecuteAsync(null);
        service.NextRecoveryFailure = OperationFailure.Create(FailureCode.PersistenceError, "Scripted commit failure.");

        await home.RestartRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());

        home.Notice.ShouldBe(Strings.Home_RecoveryFailed);
        home.NoticeErrorCode.ShouldBe(nameof(FailureCode.PersistenceError));
        home.RecoverySessions.Single().Id.ShouldBe(id);
        home.Notice!.ShouldNotContain(Strings.Home_RecoveryRestart);
    }

    [Fact]
    public async Task A_home_notice_that_is_not_a_failure_has_no_error_details()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        await ImportAsync(h, "drop");
        ScriptedFailureSessionService service = new(h.CreateService());
        HomeViewModel home = Home(service, h, new RecordingNavigation());
        await home.RefreshCommand.ExecuteAsync(null);
        service.LoadFailure = OperationFailure.Create(FailureCode.PersistenceError, "Scripted read failure.");
        await home.ResumeCommand.ExecuteAsync(home.RecentSessions.Single());
        home.HasNoticeErrorCode.ShouldBeTrue();

        await home.DropFilesCommand.ExecuteAsync(Array.Empty<string>());

        home.Notice.ShouldBe(Strings.Home_DropNothing);
        home.NoticeErrorCode.ShouldBeNull();
    }

    // --- Workflow Selection and Settings --------------------------------------------------

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task A_refused_workflow_says_nothing_started_and_keeps_the_name(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        SessionView imported = await ImportAsync(h, "select");
        ScriptedFailureSessionService service = new(h.CreateService());
        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = new(service, h.Previews, navigation);
        selection.Open(imported);
        string name = selection.OutputName!;
        service.NextExecuteFailure = OperationFailure.Create(FailureCode.PreconditionNotMet, "Scripted refusal.");

        await selection.SelectCommand.ExecuteAsync(selection.Workflows.First());

        selection.Notice.ShouldBe(Strings.WorkflowSelection_Refused);
        selection.NoticeErrorCode.ShouldBe(nameof(FailureCode.PreconditionNotMet));
        selection.OutputName.ShouldBe(name);
        navigation.SessionFor.ShouldBeNull();
        service.ExecuteCalls.ShouldBe(1, "only the refused selection; an unchanged name sends nothing");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task A_settings_save_failure_keeps_the_entries_and_changes_nothing(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SettingsScreenHarness harness = new();
        harness.Localisation.Use(Language(language));
        FailingSettings failing = new(harness.Settings);
        SettingsViewModel screen = await harness.OpenAsync(failing);
        IReadOnlyList<SettingEntry> stored = (await harness.Settings.ReadAllAsync(default)).Value;
        OperatorLanguage languageBefore = harness.Localisation.Current;
        screen.TrimSafetyMarginPixels = "7";

        await screen.ApplyCommand.ExecuteAsync(null);

        screen.Notice.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.Settings_SaveFailed, screen.ApplyLabel));
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.PersistenceError));
        screen.TrimSafetyMarginPixels.ShouldBe("7", "the entries are still shown");
        (await harness.Settings.ReadAllAsync(default)).Value.ShouldBe(stored, "nothing was changed");
        harness.Localisation.Current.ShouldBe(languageBefore);
        failing.Upserts.ShouldBe(1);
    }

    // --- Error Details: its own notice is a different failure from the opened attempt -------

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task An_error_details_action_failure_shows_its_own_code_beside_the_attempts_code(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        SessionId id = await FailedEnhancementAsync(h);
        ScriptedFailureSessionService service = new(h.CreateService());
        AttemptId attempt = (await service.LoadAsync(id, default)).Value.CurrentFailureAttemptId!.Value;
        ErrorDetailsViewModel details = new(service, new RecordingNavigation(), new LocalisationService(h.Settings));
        await details.OpenAsync(id, attempt, default);
        details.Code.ShouldBe(nameof(FailureCode.Timeout));
        details.CanRetry.ShouldBeTrue();
        details.HasNoticeErrorCode.ShouldBeFalse();
        service.NextErrorRecoveryFailure = OperationFailure.Create(
            FailureCode.PreconditionNotMet, "Scripted: no longer available for the error that was opened.");

        await details.RetryCommand.ExecuteAsync(null);

        details.Notice.ShouldBe(Strings.Failure_PreconditionNotMet);
        details.NoticeErrorCode.ShouldBe(nameof(FailureCode.PreconditionNotMet));
        details.Code.ShouldBe(nameof(FailureCode.Timeout), "the Code row stays the opened attempt's");
    }

    [Theory]
    [InlineData("en", "back")]
    [InlineData("zh-CN", "back")]
    [InlineData("en", "recovery")]
    [InlineData("zh-CN", "recovery")]
    [InlineData("en", "refresh")]
    [InlineData("zh-CN", "refresh")]
    public async Task A_failed_details_action_replaces_an_earlier_package_preview_failure(string language, string action)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        SessionId id = await FailedEnhancementAsync(h);
        ScriptedFailureSessionService service = new(h.CreateService());
        AttemptId attempt = (await service.LoadAsync(id, default)).Value.CurrentFailureAttemptId!.Value;
        RecordingNavigation navigation = new();
        RefusingPackagePreview packages = new();
        StubDiagnosticPackageDestinationPicker destination = new();
        ErrorDetailsViewModel details = new(service, navigation, new LocalisationService(h.Settings), packages, destination);
        await details.OpenAsync(id, attempt, default);
        string description = details.Description;
        SessionAggregate before = await RecoverySurfaceTests.Load(h, id);

        await details.OpenDiagnosticPackageCommand.ExecuteAsync(null);

        details.IsPackagePreview.ShouldBeFalse();
        details.Notice.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.DiagnosticPackage_PreviewFailed, FailureCode.OutputMissing));
        details.HasNoticeErrorCode.ShouldBeFalse("the existing package failure presentation is unchanged");
        details.CanRetry.ShouldBeTrue();

        OperationFailure latest = OperationFailure.Create(FailureCode.WorkspaceError, "The later action failed.");
        if (action != "back")
        {
            service.NextErrorRecoveryFailure = latest;
            if (action == "refresh")
                service.DetailsLoadFailure = OperationFailure.Create(FailureCode.PersistenceError, "The subsequent details refresh also failed.");
            await details.RetryCommand.ExecuteAsync(null);
        }
        else
        {
            service.LoadFailure = latest;
            await details.BackCommand.ExecuteAsync(null);
        }

        details.Notice.ShouldBe(action == "refresh" ? Strings.Failure_PersistenceError : Strings.Failure_WorkspaceError);
        details.NoticeErrorCode.ShouldBe(action == "refresh" ? nameof(FailureCode.PersistenceError) : nameof(FailureCode.WorkspaceError));
        details.HasNoticeErrorCode.ShouldBeTrue();
        if (action == "refresh")
        {
            details.HasDetails.ShouldBeFalse("the existing unavailable-details behavior is preserved");
            details.Code.ShouldBe(Strings.ErrorDetails_NotRecorded);
        }
        else
        {
            details.Code.ShouldBe(nameof(FailureCode.Timeout), "the opened attempt retains its own code");
            details.Description.ShouldBe(description);
        }
        details.IsPackagePreview.ShouldBeFalse();
        navigation.SessionFor.ShouldBeNull();
        packages.Plans.ShouldBe(1);
        packages.Exports.ShouldBe(0);
        destination.CallCount.ShouldBe(0);
        service.MutatingCalls.ShouldBe(action != "back" ? 1 : 0, "only the scripted recovery request; no presentation side effects");
        SessionAggregate after = await RecoverySurfaceTests.Load(h, id);
        after.Session.ShouldBe(before.Session);
        after.Attempts.ShouldBe(before.Attempts);
    }

    // --- helpers ------------------------------------------------------------------------------

    /// <summary>A failed read-only package preview; no files, dialogs or exports are involved.</summary>
    private sealed class RefusingPackagePreview : IDiagnosticPackageService
    {
        public int Plans { get; private set; }
        public int Exports { get; private set; }

        public Task<OperationResult<DiagnosticPackagePlan>> BuildPlanAsync(SessionId id, AttemptId attempt, CancellationToken token)
        {
            Plans++;
            return Task.FromResult(OperationResult.Fail<DiagnosticPackagePlan>(FailureCode.OutputMissing, "Preview evidence is unavailable."));
        }

        public Task<OperationResult<DiagnosticPackageExportResult>> ExportAsync(DiagnosticPackagePlan plan, string destination, CancellationToken token)
        {
            Exports++;
            throw new InvalidOperationException("A refused preview must never export a package.");
        }
    }

    internal static async Task<(ScriptedFailureSessionService Service, SessionViewModel Screen, SessionId Id)> AtOriginalConfirmationAsync(
        SessionServiceHarness h, RecordingNavigation? navigation = null, ILocalisationService? localisation = null)
    {
        SessionView imported = await ImportAsync(h, "failure");
        ScriptedFailureSessionService service = new(h.CreateService());
        SessionViewModel screen = new(service, h.Previews, h.TiffReviews, navigation ?? new RecordingNavigation(), null, localisation);
        screen.Open(imported);
        await screen.PreviewsLoaded;
        screen.CanConfirmOriginal.ShouldBeTrue();
        return (service, screen, imported.Id);
    }

    internal static async Task<SessionView> ImportAsync(SessionServiceHarness h, string name) =>
        (await h.CreateService().ImportAsync(WorkflowType.PrepareAsset, h.WriteSourcePng(name + ".png"), name, "tester",
            CancellationToken.None)).Value;

    /// <summary>A job whose Enhancement really failed with Timeout through the fake Meitu adapter.</summary>
    internal static async Task<SessionId> FailedEnhancementAsync(SessionServiceHarness h)
    {
        ISessionService real = h.CreateService();
        SessionId id = (await ImportAsync(h, "timeout")).Id;
        await MustAsync(real.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", default));
        h.FakeMeitu.SetScenario(FakeAdapterScenario.Timeout);
        (await real.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", default)).IsFailure.ShouldBeTrue();
        h.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        return id;
    }

    internal static string Composed(string wrapper, string sentence, string next)
    {
        string first = string.Format(CultureInfo.CurrentCulture, wrapper, sentence);
        return first.EndsWith('。') ? first + next : first + " " + next;
    }

    private static HomeViewModel Home(ISessionService service, SessionServiceHarness h, RecordingNavigation navigation) =>
        new(service, h.Previews, navigation, new StubFilePicker(), new StartupStatusAccessor(), new ReadinessObservationAccessor());

    private static OperatorLanguage Language(string culture) =>
        culture == "en" ? OperatorLanguage.English : OperatorLanguage.SimplifiedChinese;

    private static (bool, bool, bool, bool, bool, bool) Commands(SessionViewModel screen) =>
        (screen.CanConfirmOriginal, screen.CanRunStep, screen.CanRetry, screen.CanApprove, screen.CanReject, screen.CanSkip);

    /// <summary>The real store for reads; every write fails exactly as a rolled-back SQLite write reports.</summary>
    private sealed class FailingSettings(ISettingsRepository inner) : ISettingsRepository
    {
        public int Upserts { get; private set; }

        public Task<OperationResult<SettingEntry?>> ReadAsync(SettingKey key, CancellationToken cancellationToken) =>
            inner.ReadAsync(key, cancellationToken);

        public Task<OperationResult<IReadOnlyList<SettingEntry>>> ReadAllAsync(CancellationToken cancellationToken) =>
            inner.ReadAllAsync(cancellationToken);

        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> UpsertAsync(
            IReadOnlyList<SettingEntry> entries, CancellationToken cancellationToken)
        {
            Upserts++;
            return Task.FromResult(OperationResult.Fail<PrintFlow.Domain.Results.Unit>(
                FailureCode.PersistenceError, "Scripted: the settings write failed and was rolled back."));
        }
    }
}
