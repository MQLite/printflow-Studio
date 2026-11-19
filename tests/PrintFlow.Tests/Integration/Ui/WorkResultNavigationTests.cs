using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.App.ViewModels.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class WorkResultNavigationTests
{
    [Fact]
    public async Task Continue_opens_imported_original_confirmation_with_no_workflow_selection_or_new_session()
    {
        using var app = new TempApplication();
        var query = new WorkResultCardTests.TestQuery();
        var leave = new ControlledLeavePrompt { Choice = AiDraftLeaveChoice.Cancel };
        using var services = AuthoringGraphTests.Compose(app, leave, configure: s => s.AddSingleton<IWorkTaskQuery>(query));
        var navigation = services.GetRequiredService<INavigationService>();
        var work = services.GetRequiredService<IAiDesignNavigation>();
        var sessions = services.GetRequiredService<ISessionService>();
        var row = WorkResultCardTests.TaskFor(null, WorkTaskStage.Imported);
        var saved = await ImportAsync(app, services, row.RequestId);
        saved.CurrentStep!.Step.ShouldBe(StepKind.OriginalConfirmation);
        query.Items = [row with { ImportedSessionId = saved.Id }];
        await navigation.GoHomeAsync(default);
        await work.GoToPromptWorkbenchAsync(null, default);
        var bench = navigation.Current.ShouldBeOfType<AiPromptWorkbenchViewModel>();

        bench.Document = new([new AiTextPart(Guid.NewGuid(), "Unsaved sample")]);
        await work.OpenWorkResultAsync(row.RequestId, default);
        navigation.Current.ShouldBeSameAs(bench);
        leave.Choice = AiDraftLeaveChoice.Discard;
        var missing = row with { ImportedSessionId = PrintFlow.Domain.Ids.SessionId.From(Guid.NewGuid()) };
        query.Items = [missing];
        (await work.OpenWorkResultAsync(row.RequestId, default)).IsFailure.ShouldBeTrue();
        navigation.Current.ShouldBeSameAs(bench);
        query.Items = [row with { ImportedSessionId = saved.Id }];
        var navigations = 0;
        navigation.CurrentChanged += (_, _) => navigations++;
        await Task.WhenAll(work.OpenWorkResultAsync(row.RequestId, default), work.OpenWorkResultAsync(row.RequestId, default));
        var opened = navigation.Current.ShouldBeOfType<SessionViewModel>();
        opened.Id.ShouldBe(saved.Id);
        opened.ShowsGuidedSourceDetails.ShouldBeTrue();
        navigations.ShouldBe(1);
        (await sessions.ListRecentAsync(default)).Value.ShouldHaveSingleItem().Id.ShouldBe(saved.Id);
    }

    [Fact]
    public async Task Work_result_lookup_does_not_override_navigation_while_loading()
    {
        using var app = new TempApplication();
        var query = new WorkResultCardTests.TestQuery();
        using var services = AuthoringGraphTests.Compose(app, new(), configure: s => s.AddSingleton<IWorkTaskQuery>(query));
        var navigation = services.GetRequiredService<INavigationService>();
        var work = services.GetRequiredService<IAiDesignNavigation>();
        var row = WorkResultCardTests.TaskFor(null, WorkTaskStage.Imported);
        var saved = await ImportAsync(app, services, row.RequestId);
        await navigation.GoHomeAsync(default);
        await work.GoToPromptWorkbenchAsync(null, default);
        var bench = navigation.Current.ShouldBeOfType<AiPromptWorkbenchViewModel>();
        var pending = new TaskCompletionSource<OperationResult<WorkTaskSnapshot?>>(TaskCreationOptions.RunContinuationsAsynchronously);
        query.FindOverride = _ => pending.Task;
        var opening = work.OpenWorkResultAsync(row.RequestId, default);
        await bench.OpenAsync(null, default);
        pending.SetResult(OperationResult.Ok<WorkTaskSnapshot?>(row with { ImportedSessionId = saved.Id }));
        await opening;
        navigation.Current.ShouldBeSameAs(bench);
    }

    [Fact]
    public async Task Visible_home_refreshes_once_for_the_same_work_import()
    {
        using var app = new TempApplication();
        using var services = AuthoringGraphTests.Compose(app, new());
        var navigation = services.GetRequiredService<INavigationService>();
        var tracker = services.GetRequiredService<WorkTaskTracker>();
        var existing = await ImportAsync(app, services, Guid.NewGuid());
        await navigation.GoHomeAsync(default);
        var home = navigation.Current.ShouldBeOfType<HomeViewModel>();
        var originalSection = home.ContinueSessions;
        var existingRow = originalSection.ShouldHaveSingleItem();
        await home.AbandonCommand.ExecuteAsync(existingRow);
        var confirmation = home.PendingAbandon;
        confirmation.ShouldNotBeNull();

        var imported = await ImportAsync(app, services, Guid.NewGuid());
        var observation = new WorkResultObservation(imported.Id.Value, Guid.NewGuid(), 3, WorkResultStage.Imported, imported.Id);
        tracker.Report(observation);
        tracker.Report(observation);
        home.ContinueSessions.ShouldHaveSingleItem().Id.ShouldBe(existing.Id);
        home.PendingAbandon.ShouldBeSameAs(confirmation);

        home.KeepJobCommand.Execute(null);
        await home.WorkResultRefresh;
        home.ContinueSessions.Count(row => row.Id == imported.Id).ShouldBe(1);
        home.ContinueSessions.ShouldBeSameAs(originalSection);
        home.ContinueSessions.Single(row => row.Id == existing.Id).ShouldBeSameAs(existingRow);
        navigation.Current.ShouldBeSameAs(home);
        home.PendingAbandon.ShouldBeNull();
    }

    internal static async Task<SessionView> ImportAsync(TempApplication app, IServiceProvider services, Guid request)
    {
        var source = WorkResultCardTests.WriteSyntheticReturn(app.WorkspaceRoot, request);
        var inspector = services.GetRequiredService<IFileInspector>();
        var facts = (await inspector.InspectAsync(source, default)).Value;
        var result = await services.GetRequiredService<ISessionService>().ImportVerifiedWorkResultAsync(
            request, source, "Synthetic return", facts.Sha256, default);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : null);
        return result.Value;
    }
}
