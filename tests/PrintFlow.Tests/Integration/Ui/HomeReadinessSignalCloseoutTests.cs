using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using PrintFlow.App.Resources;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Domain.Results;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class HomeReadinessSignalCloseoutTests
{
    [Fact]
    public async Task Page_and_Settings_each_publish_one_logical_gate_observation()
    {
        using WorkstationVerificationFixture fixture = new();
        using SettingsScreenHarness settings = new();
        List<ReadinessObservationState> states = [];
        settings.Observations.Changed += (_, _) => states.Add(settings.Observations.Current.State);
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier(), settings.Observations);
        await new EnvironmentReadinessViewModel(gate, settings.Navigation, settings.Observations).OpenAsync(CancellationToken.None);
        states.ShouldBe([ReadinessObservationState.InProgress, ReadinessObservationState.Observed]);
        states.Clear();
        await settings.OpenAsync(diagnostics: gate);
        states.ShouldBe([ReadinessObservationState.InProgress, ReadinessObservationState.Observed]);
    }

    [Fact]
    public async Task Old_success_cannot_supersede_new_failure_or_new_unfinished_check_and_later_success_restores()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationResult pass = fixture.CreateVerifier().Verify();
        ReadinessObservationAccessor observations = new();
        ScriptedVerifier slow = new(pass) { Hold = true };
        VerifiedEnvironmentGate olderGate = new(slow, observations);
        Task<EnvironmentReadinessReport> older = Task.Run(olderGate.Read);
        slow.Entered.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        try
        {
            fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
            VerifiedEnvironmentGate newer = new(fixture.CreateVerifier(), observations);
            newer.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
            observations.Current.Report!.Verified.ShouldBeFalse();
            slow.Release.Set();
            (await older).Verified.ShouldBeTrue();
            observations.Current.Report!.Verified.ShouldBeFalse();
            fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 1 };
            newer.Read().Verified.ShouldBeTrue();
            observations.Current.Report!.Verified.ShouldBeTrue();

            slow.Release.Reset(); slow.Entered.Reset();
            older = Task.Run(olderGate.Read);
            slow.Entered.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
            ScriptedVerifier throwing = new(pass) { Throw = true };
            Should.Throw<InvalidOperationException>(() => new VerifiedEnvironmentGate(throwing, observations).Read());
            slow.Release.Set(); await older;
            observations.Current.State.ShouldBe(ReadinessObservationState.Unfinished);
        }
        finally { slow.Release.Set(); await older; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Full_live_operation_never_leaks_intermediate_pass_and_cancel_is_unfinished(bool returnCancelled)
    {
        using WorkstationVerificationFixture fixture = new();
        ReadinessObservationAccessor observations = new();
        ScriptedVerifier verifier = new(fixture.CreateVerifier().Verify()) { Hold = true, ReturnCancelled = returnCancelled };
        VerifiedEnvironmentGate gate = new(verifier, observations);
        List<ReadinessObservationState> states = [];
        observations.Changed += (_, _) => states.Add(observations.Current.State);
        using CancellationTokenSource cancellation = new();
        Task<EnvironmentReadinessReport> running = gate.RunLiveChecksAsync(cancellation.Token);
        observations.Current.State.ShouldBe(ReadinessObservationState.InProgress);
        observations.Current.Report.ShouldBeNull();
        cancellation.Cancel();
        verifier.Release.Set();
        if (returnCancelled) (await running).Verified.ShouldBeFalse();
        else await Should.ThrowAsync<OperationCanceledException>(() => running);
        states.ShouldBe([ReadinessObservationState.InProgress, ReadinessObservationState.Unfinished]);
    }

    [Fact]
    public async Task Cancelled_result_without_caller_cancellation_is_not_a_discovered_fault()
    {
        using WorkstationVerificationFixture fixture = new();
        ReadinessObservationAccessor observations = new();
        VerifiedEnvironmentGate gate = new(new ScriptedVerifier(fixture.CreateVerifier().Verify())
            { ReturnCancelled = true }, observations);
        (await gate.RunLiveChecksAsync(CancellationToken.None)).Verified.ShouldBeFalse();
        observations.Current.State.ShouldBe(ReadinessObservationState.Unfinished);
        new HomeReadinessSummary(observations.Current).HasTechnicalDetail.ShouldBeFalse();
    }

    [Fact]
    public async Task Preworker_cancellation_is_atomic_and_a_started_synchronous_read_retains_its_report()
    {
        using WorkstationVerificationFixture fixture = new();
        ReadinessObservationAccessor observations = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier(), observations);
        gate.Read();
        EnvironmentReadinessViewModel screen = new(gate, new RecordingNavigation(), observations);
        await Should.ThrowAsync<OperationCanceledException>(() => screen.OpenAsync(new CancellationToken(true)));
        observations.Current.State.ShouldBe(ReadinessObservationState.Unfinished);
        ReadinessObservation old = observations.Current;
        gate.Read();
        ReadinessObservation passed = observations.Current;
        observations.AbandonIfUnchanged(old);
        observations.Current.ShouldBeSameAs(passed);
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
        gate.Read();
        ReadinessObservation failed = observations.Current;
        observations.AbandonIfUnchanged(passed);
        observations.Current.ShouldBeSameAs(failed);
        long current = observations.Begin();
        ReadinessObservation inProgress = observations.Current;
        observations.AbandonIfUnchanged(failed);
        observations.Current.ShouldBeSameAs(inProgress);
        observations.Abandon(current);

        using CancellationTokenSource token = new();
        ScriptedVerifier held = new(fixture.CreateVerifier().Verify()) { Hold = true };
        screen = new(new VerifiedEnvironmentGate(held, observations), new RecordingNavigation(), observations);
        Task read = screen.OpenAsync(token.Token);
        held.Entered.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        token.Cancel(); held.Release.Set(); await read;
        observations.Current.State.ShouldBe(ReadinessObservationState.Observed);
        observations.Current.Report!.Verified.ShouldBeFalse();
    }

    [Fact]
    public async Task Subscriber_and_optional_sink_faults_never_change_gate_decisions_exceptions_or_lease()
    {
        using WorkstationVerificationFixture fixture = new();
        using SessionServiceHarness storage = new();
        IWorkstationAutomationLease lease = (await storage.AutomationLeases.TryAcquireAsync(null, CancellationToken.None)).Value;
        try
        {
            ReadinessObservationAccessor observations = new();
            int notifications = 0;
            observations.Changed += (_, _) => throw new ObjectDisposedException("old view");
            observations.Changed += (_, _) => notifications++;
            VerifiedEnvironmentGate gate = new(fixture.CreateVerifier(), observations);
            gate.Verify(AdapterExecutionMode.Production, lease).IsSuccess.ShouldBeTrue();
            fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
            gate.Verify(AdapterExecutionMode.Production, lease).IsFailure.ShouldBeTrue();
            observations.Current.Report!.Verified.ShouldBeFalse();
            notifications.ShouldBe(4);
            lease.IsActive.ShouldBeTrue();
            foreach (bool beginThrows in new[] { false, true })
            {
                gate = new(fixture.CreateVerifier(), new ThrowingSink(beginThrows));
                gate.Verify(AdapterExecutionMode.Production, lease).IsFailure.ShouldBeTrue();
                fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 1 };
                gate.Verify(AdapterExecutionMode.Production, lease).IsSuccess.ShouldBeTrue();
                gate.Read().Verified.ShouldBeTrue();
                ScriptedVerifier throwing = new(fixture.CreateVerifier().Verify()) { Throw = true };
                Should.Throw<InvalidOperationException>(() => new VerifiedEnvironmentGate(throwing, new ThrowingSink(beginThrows)).Read())
                    .Message.ShouldBe("original verifier error");
                lease.IsActive.ShouldBeTrue();
                fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
            }
        }
        finally { (await lease.ReleaseAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue(); }
    }

    [Fact]
    public void Internal_subset_success_cannot_restore_full_ready_and_shared_failure_code_is_not_an_observation()
    {
        using WorkstationVerificationFixture fixture = new();
        ReadinessObservationAccessor observations = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier(), observations);
        gate.Read();
        ReadinessObservation original = observations.Current;
        gate.Verify((AdapterExecutionMode)999).Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        observations.Current.ShouldBeSameAs(original);
        gate.VerifyForInternalWork(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();
        observations.Current.State.ShouldBe(ReadinessObservationState.Unfinished);
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
        gate.VerifyForInternalWork(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
        observations.Current.Report!.BlockingFailures.First().CheckKey.ShouldBe("DisplayConfiguration");
    }

    [Fact]
    public async Task Production_refusal_replaces_previous_pass_in_reopened_Home_with_actual_first_reason_and_time()
    {
        using WorkstationVerificationFixture fixture = new();
        using HomeScreenHarness h = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier(), h.Readiness);
        h.Readiness.Complete(h.Readiness.Begin(), gate.Read());
        await h.Home.RefreshCommand.ExecuteAsync(null);
        h.Home.Readiness.State.ShouldBe(HomeReadinessState.Ready);
        fixture.Clock.Advance(TimeSpan.FromMinutes(7));
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };

        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
        HomeViewModel reopened = new(h.Sessions, h.Previews, h.Navigation, h.FilePicker, h.StartupStatus, h.Readiness);

        reopened.Readiness.State.ShouldBe(HomeReadinessState.Blocked);
        reopened.Readiness.TechnicalDetailText.ShouldContain("DisplayConfiguration");
        reopened.Readiness.CheckedAtText.ShouldContain(fixture.Clock.GetUtcNow().ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
    }

    [Fact]
    public async Task Loaded_Home_updates_on_its_dispatcher_without_refreshing_lists_or_clearing_notice()
    {
        using WorkstationVerificationFixture fixture = new();
        using HomeScreenHarness h = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier(), h.Readiness);
        h.Readiness.Complete(h.Readiness.Begin(), gate.Read());
        await h.Home.RefreshCommand.ExecuteAsync(null);
        h.Home.Notice = "Keep this notice";
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = h.Home }, new Size(1000, 700), tree =>
        {
            tree.Root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            int changes = 0;
            int listChanges = 0;
            h.Home.RecentSessions.CollectionChanged += (_, _) => listChanges++;
            h.Home.RecoverySessions.CollectionChanged += (_, _) => listChanges++;
            bool wrongThread = false;
            h.Home.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(HomeViewModel.Readiness)) return;
                changes++;
                wrongThread |= !tree.Root.Dispatcher.CheckAccess();
            };
            Task.Run(() => gate.Verify(AdapterExecutionMode.Production)).GetAwaiter().GetResult();
            tree.Root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            h.Home.Readiness.State.ShouldBe(HomeReadinessState.Blocked);
            wrongThread.ShouldBeFalse();
            changes.ShouldBeGreaterThan(0);
            h.Home.Notice.ShouldBe("Keep this notice");
            listChanges.ShouldBe(0);
            // A queued notification must also be discarded if the view unloads before dispatch.
            fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 1 };
            gate.Read();
            tree.Root.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            HomeReadinessSummary detached = h.Home.Readiness;
            gate.Read();
            tree.Root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            h.Home.Readiness.ShouldBeSameAs(detached);
            return 0;
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public void Home_cleanup_notice_cannot_imply_processing_is_allowed(string language)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        h.Home.RetentionWarningText.ShouldNotBe(Strings.Startup_DiagnosticRetentionWarning);
        h.Home.RetentionWarningText.ShouldContain(language == "en" ? "does not mean" : "不表示");
    }

    private sealed class ThrowingSink(bool beginThrows) : IEnvironmentReadinessObservations
    {
        public long Begin() => beginThrows ? throw new InvalidOperationException("observer") : 1;
        public void Complete(long ticket, EnvironmentReadinessReport report) => throw new InvalidOperationException("observer");
        public void Abandon(long ticket) => throw new InvalidOperationException("observer");
    }

    private sealed class ScriptedVerifier(WorkstationVerificationResult result) : IProductionWorkstationVerifier
    {
        public bool Hold { get; init; }
        public bool Throw { get; init; }
        public bool ReturnCancelled { get; init; }
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public WorkstationVerificationResult Verify()
        {
            if (Throw) throw new InvalidOperationException("original verifier error");
            Entered.Set();
            if (Hold && !Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return result;
        }
        public async Task<WorkstationVerificationResult> RunLiveChecksAsync(CancellationToken token)
        {
            // Real full-live shape: automatic success exists internally, but is never a final report.
            Entered.Set();
            if (Hold) await Task.Run(() => Release.Wait(TimeSpan.FromSeconds(10)));
            if (ReturnCancelled)
                return result with { Verified = false, Checks = [WorkstationCheckResult.Failed(
                    WorkstationVerificationCheck.MeituLaunchability, WorkstationCheckKind.Live,
                    FailureCode.Cancelled, null, null, "cancelled")] };
            token.ThrowIfCancellationRequested();
            return result;
        }
    }
}
