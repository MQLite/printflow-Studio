using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Navigation;
using PrintFlow.App.Settings;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using System.Reflection;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedUtilityReturnTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_late_reload_cannot_restore_old_task_ownership(bool navigateAway)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        using SettingsScreenHarness preferences = new();
        var sessions = DispatchProxy.Create<ISessionService, ReloadService>();
        var proxy = (ReloadService)(object)sessions; proxy.Inner = h.Sessions;
        var services = new ServiceCollection();
        services.AddSingleton(sessions);
        services.AddSingleton<NavigationService>();
        services.AddTransient(p => new SessionViewModel(sessions, h.Previews, h.TiffReviews, p.GetRequiredService<NavigationService>()));
        services.AddTransient(p => new EnvironmentReadinessViewModel(preferences.Diagnostics, p.GetRequiredService<NavigationService>(), h.Readiness));
        using var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<NavigationService>();
        var imported = (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourceFile("late-utility.png"), null, "tester", default)).Value;
        var before = (await h.Inner.Repository.LoadAsync(imported.Id, default)).Value!;
        nav.GoToSession(imported);
        var source = (SessionViewModel)nav.Current!;
        await source.OpenEnvironmentCommand.ExecuteAsync(null);
        var environment = (EnvironmentReadinessViewModel)nav.Current!;
        proxy.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var back = environment.BackToHomeCommand.ExecuteAsync(null);
        bool diagnosticsAvailableDuringReturn = environment.RunLiveChecksCommand.CanExecute(null);
        if (navigateAway) nav.GoToSession(imported);
        var expected = nav.Current;
        proxy.Pending.SetResult(navigateAway ? OperationResult.Ok(imported)
            : OperationResult.Fail<SessionView>(FailureCode.PersistenceError, "Synthetic task reload failure"));
        await back;
        nav.Current.ShouldBeSameAs(expected);
        source.CanInteractWithLivePage.ShouldBeFalse();
        if (!navigateAway)
        {
            environment.ReturnNotice.ShouldNotBeNull();
            environment.ReturnNotice!.ShouldContain("尚未恢复");
            environment.NoticeErrorCode.ShouldBe(nameof(FailureCode.PersistenceError));
            proxy.Pending = null;
            await environment.BackToHomeCommand.ExecuteAsync(null);
            nav.Current.ShouldBeSameAs(source);
            source.CanInteractWithLivePage.ShouldBeTrue();
        }
        var after = (await h.Inner.Repository.LoadAsync(source.Id, default)).Value!;
        after.Attempts.ShouldBe(before.Attempts); after.Reviews.ShouldBe(before.Reviews);
        after.Revisions.ShouldBe(before.Revisions); after.Steps.ShouldBe(before.Steps);
        diagnosticsAvailableDuringReturn.ShouldBeFalse("task reload owns the utility until it completes");
    }

    public class ReloadService : DispatchProxy
    {
        public ISessionService Inner { get; set; } = null!;
        public TaskCompletionSource<OperationResult<SessionView>>? Pending { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == nameof(ISessionService.LoadAsync) && Pending is { } pending
                ? pending.Task : method.Invoke(Inner, args);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Utilities_reload_the_source_task_and_retain_only_matching_drafts(bool changed)
    {
        using HomeScreenHarness h = new();
        using SettingsScreenHarness preferences = new();
        var services = new ServiceCollection();
        services.AddSingleton(h.Sessions);
        services.AddSingleton<NavigationService>();
        services.AddTransient(p => new SessionViewModel(h.Sessions, h.Previews, h.TiffReviews, p.GetRequiredService<NavigationService>()));
        var diagnostics = new GuidedCompletionUtilityTests.FixedDiagnostics(GuidedCompletionUtilityTests.Report("checked"));
        services.AddTransient(p => new SettingsViewModel(preferences.Settings, preferences.Localisation, diagnostics,
            p.GetRequiredService<NavigationService>(), preferences.Defaults,
            new LocalDiagnosticLocations(h.Inner.Database.Path, h.Inner.Workspace.Root), h.Readiness));
        services.AddTransient(p => new EnvironmentReadinessViewModel(diagnostics, p.GetRequiredService<NavigationService>(), h.Readiness));
        using var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<NavigationService>();
        var imported = (await h.Sessions.ImportAsync(WorkflowType.GeneratePrintTiff, h.WriteSourceFile("utility.png"), null, "tester", default)).Value;
        nav.GoToSession(imported);
        var source = (SessionViewModel)nav.Current!;
        await source.ConfirmOriginalCommand.ExecuteAsync(null);
        source.WidthMmText = "317"; source.HeightMmText = "211";
        await nav.GoToSettingsAsync(default);
        source.CanInteractWithLivePage.ShouldBeFalse("a retained task must be suspended while utilities own the screen");
        await source.SetMaximumBoundsCommand.ExecuteAsync(null);
        (await h.Sessions.LoadAsync(source.Id, default)).Value.Dimensions.ShouldBeNull();
        var settings = (SettingsViewModel)nav.Current!;
        await settings.OpenEnvironmentCommand.ExecuteAsync(null);
        var environment = (EnvironmentReadinessViewModel)nav.Current!;
        await settings.BackToHomeCommand.ExecuteAsync(null);
        nav.Current.ShouldBeSameAs(environment, "an old utility instance cannot take ownership back");
        if (changed)
        {
            var driver = h.Session(new RecordingNavigation());
            driver.Open((await h.Sessions.LoadAsync(source.Id, default)).Value);
            driver.WidthMmText = "200"; driver.HeightMmText = "150";
            await driver.SetMaximumBoundsCommand.ExecuteAsync(null);
        }
        var before = (await h.Inner.Repository.LoadAsync(source.Id, default)).Value!;
        await environment.BackToHomeCommand.ExecuteAsync(null);
        nav.Current.ShouldBeSameAs(source);
        source.CanInteractWithLivePage.ShouldBeTrue();
        source.WidthMmText.ShouldBe(changed ? null : "317");
        source.HeightMmText.ShouldBe(changed ? null : "211");
        source.GuidedPage.ShouldBe(changed ? GuidedSessionPage.WhiteInk : GuidedSessionPage.Dimensions);
        var after = (await h.Inner.Repository.LoadAsync(source.Id, default)).Value!;
        after.Steps.ShouldBe(before.Steps); after.Reviews.ShouldBe(before.Reviews); after.Revisions.ShouldBe(before.Revisions);
        await settings.OpenEnvironmentCommand.ExecuteAsync(null);
        await environment.BackToHomeCommand.ExecuteAsync(null);
        nav.Current.ShouldBeSameAs(source);
        await environment.RunLiveChecksCommand.ExecuteAsync(null);
        diagnostics.LiveCalls.ShouldBe(0, "a stale utility action cannot start diagnostics after returning to the task");
        settings.TrimSafetyMarginPixels = "9";
        await settings.ApplyCommand.ExecuteAsync(null);
        (await preferences.Settings.ReadAllAsync(default)).Value.ShouldBeEmpty();
    }
}
