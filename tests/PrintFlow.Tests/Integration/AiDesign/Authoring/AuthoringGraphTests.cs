using System.IO;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.App.ViewModels.AiDesign;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Services;
using Shouldly;
using Xunit;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Infrastructure.AiDesign.Authoring;

namespace PrintFlow.Tests.Integration.AiDesign.Authoring;

public sealed class AuthoringGraphTests
{
    [Fact]
    public async Task Refused_new_draft_publication_still_opens_a_readable_saved_draft_from_home()
    {
        using var app = new TempApplication(); Guid id;
        using (var first = Compose(app, new()))
        {
            var nav = first.GetRequiredService<INavigationService>(); await nav.GoHomeAsync(default);
            await first.GetRequiredService<IAiDesignNavigation>().GoToPromptWorkbenchAsync(null, default);
            var vm = nav.Current.ShouldBeOfType<AiPromptWorkbenchViewModel>();
            vm.Document = new([new AiTextPart(Guid.NewGuid(), "existing disk content")]); await vm.FlushAsync(default); id = vm.Draft!.Id;
        }
        using var services = Compose(app, new(), true); var restarted = services.GetRequiredService<INavigationService>();
        await restarted.GoHomeAsync(default); await services.GetRequiredService<IAiDesignNavigation>().GoToPromptWorkbenchAsync(null, default);
        var workbench = restarted.Current.ShouldBeOfType<AiPromptWorkbenchViewModel>();
        workbench.Draft.ShouldBeNull(); workbench.IsSavedDraftsVisible.ShouldBeTrue();
        await workbench.OpenSavedDraftCommand.ExecuteAsync(workbench.SavedDrafts.Single(x => x.Id == id));
        workbench.Draft!.Id.ShouldBe(id);
        string.Concat(workbench.Document.Parts.OfType<AiTextPart>().Select(x => x.Text)).ShouldBe("existing disk content");
    }
    [Fact]
    public async Task Uncommitted_draft_directories_do_not_hide_saved_drafts()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        var savedId = f.Vm.Draft!.Id;
        string orphan = Path.Combine(f.Root.Root, "AiDesign", "Authoring", "Drafts", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(orphan); File.WriteAllText(Path.Combine(orphan, "revision-orphan.json"), "{}");
        var result = await f.Module.ListAsync(default);
        result.IsSuccess.ShouldBeTrue(); result.Value.Drafts.Select(x => x.Draft.Id).ShouldBe([savedId]);
    }

    [Fact]
    public async Task Home_entry_refuses_busy_and_retired_home()
    {
        using var app = new TempApplication(); using var services = Compose(app, new());
        var nav = services.GetRequiredService<INavigationService>(); var ai = services.GetRequiredService<IAiDesignNavigation>();
        await nav.GoHomeAsync(default); var home = nav.Current.ShouldBeOfType<HomeViewModel>();
        home.IsBusy = true; home.OpenPromptWorkbenchCommand.CanExecute(null).ShouldBeFalse();
        await ai.GoToPromptWorkbenchAsync(null, default); nav.Current.ShouldBeSameAs(home);
        home.IsBusy = false; home.OpenPromptWorkbenchCommand.CanExecute(null).ShouldBeTrue();
        home.Deactivate(); await ai.GoToPromptWorkbenchAsync(null, default); nav.Current.ShouldBeSameAs(home);
        Directory.Exists(Path.Combine(app.WorkspaceRoot, "AiDesign", "Authoring")).ShouldBeFalse();
    }

    [Fact]
    public async Task Home_opens_prompt_only_workbench_without_starting_workflow_or_browser()
    {
        using var app = new TempApplication(); using var services = Compose(app, new());
        var nav = services.GetRequiredService<INavigationService>();
        services.GetRequiredService<IAiDesignNavigation>().ShouldBeSameAs(nav);
        await nav.GoHomeAsync(default); var home = nav.Current.ShouldBeOfType<HomeViewModel>();
        home.OpenPromptWorkbenchCommand.CanExecute(null).ShouldBeTrue();
        await home.OpenPromptWorkbenchCommand.ExecuteAsync(null);
        var workbench = nav.Current.ShouldBeOfType<AiPromptWorkbenchViewModel>();
        workbench.Draft.ShouldNotBeNull(); workbench.Document.Parts.ShouldNotBeEmpty();
        home.OpenPromptWorkbenchCommand.CanExecute(null).ShouldBeFalse();
        (await services.GetRequiredService<ISessionService>().ListRecentAsync(default)).Value.ShouldBeEmpty();
        Directory.Exists(Path.Combine(app.WorkspaceRoot, "AiDesign", "Requests")).ShouldBeFalse();
    }

    [Fact]
    public async Task Cancel_or_failed_save_prevents_return_and_successful_return_reopens_exact_text()
    {
        using var app = new TempApplication(); var prompt = new ControlledLeavePrompt { Choice = AiDraftLeaveChoice.Cancel };
        using var services = Compose(app, prompt); var nav = services.GetRequiredService<INavigationService>();
        await nav.GoHomeAsync(default); await services.GetRequiredService<IAiDesignNavigation>().GoToPromptWorkbenchAsync(null, default);
        var vm = nav.Current.ShouldBeOfType<AiPromptWorkbenchViewModel>(); var id = vm.Draft!.Id;
        vm.Document = new([new PrintFlow.Domain.AiDesign.Authoring.AiTextPart(Guid.NewGuid(), "  Āna\r\n陈  ")]);
        await vm.ReturnHomeCommand.ExecuteAsync(null); nav.Current.ShouldBeSameAs(vm);
        prompt.Choice = AiDraftLeaveChoice.Save;
        using (var block = new FileStream(Path.Combine(app.WorkspaceRoot, "AiDesign", "Authoring", "Drafts", id.ToString("D"), "current.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        { await nav.GoHomeAsync(default); nav.Current.ShouldBeSameAs(vm); }
        await vm.ReturnHomeCommand.ExecuteAsync(null); nav.Current.ShouldBeOfType<HomeViewModel>();
        await services.GetRequiredService<IAiDesignNavigation>().GoToPromptWorkbenchAsync(id, default);
        string.Concat(nav.Current.ShouldBeOfType<AiPromptWorkbenchViewModel>().Document.Parts.OfType<PrintFlow.Domain.AiDesign.Authoring.AiTextPart>().Select(x => x.Text)).ShouldBe("  Āna\r\n陈  ");
    }

    internal static ServiceProvider Compose(TempApplication app, ControlledLeavePrompt prompt, bool denyNewDrafts = false)
    {
        var factory = new SqliteConnectionFactory(app.DatabasePath);
        using (var connection = factory.Open()) MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        return ServiceRegistration.BuildServiceProvider(PrintFlowConfiguration.LoadFromFile(app.ConfigurationFilePath), app.WorkspaceRoot, factory,
            services =>
            {
                services.AddSingleton<IAiDraftLeavePrompt>(prompt);
                if (denyNewDrafts) services.AddSingleton<IAiAuthoringDraftStore>(new DenyNewDraftStore(new FileAiAuthoringDraftStore(app.WorkspaceRoot)));
            });
    }

    private sealed class DenyNewDraftStore(IAiAuthoringDraftStore inner) : IAiAuthoringDraftStore
    {
        public Task<OperationResult<AiAuthoringDraft>> ReadAsync(Guid id, CancellationToken ct) => inner.ReadAsync(id, ct);
        public Task<OperationResult<IReadOnlyList<AiAuthoringDraft>>> ReadHistoryAsync(Guid id, CancellationToken ct) => inner.ReadHistoryAsync(id, ct);
        public Task<OperationResult<AiAuthoringDraftListing>> ListAsync(CancellationToken ct) => inner.ListAsync(ct);
        public Task<OperationResult<T>> WithCurrentRevisionAsync<T>(Guid id, long expectedRevision,
            Func<AiAuthoringDraft, Task<OperationResult<T>>> publish, CancellationToken ct) =>
            inner.WithCurrentRevisionAsync(id, expectedRevision, publish, ct);
        public Task<OperationResult<AiAuthoringDraft>> SaveAsync(AiAuthoringDraft value, long? expectedRevision, CancellationToken ct) =>
            expectedRevision is null ? Task.FromResult(OperationResult.Fail<AiAuthoringDraft>(FailureCode.PersistenceError, "New publication refused.")) : inner.SaveAsync(value, expectedRevision, ct);
    }
}
