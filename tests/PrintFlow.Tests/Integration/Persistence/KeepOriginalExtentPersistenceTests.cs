using System.IO;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class KeepOriginalExtentPersistenceTests
{
    [Fact]
    public async Task Keeping_original_uses_approved_derived_upstream_instead_of_import_or_last_trim()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await AtTrim(h, service, WorkflowType.PrepareCustomerDesign);
        await Execute(service, id, new WorkflowCommand.ReturnToStep(StepKind.Enhancement));
        SessionView enhanced = await Execute(service, id, new WorkflowCommand.StartStep(StepKind.Enhancement));
        RevisionId approved = enhanced.CurrentArtefact!.RevisionId;
        await Execute(service, id, new WorkflowCommand.Approve(StepKind.Enhancement, enhanced.CurrentArtefact.Sha256));
        await Execute(service, id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval));
        await RunTrim(service, id, manual: false);
        await Execute(service, id, new WorkflowCommand.KeepOriginalExtent());
        SessionAggregate after = await Load(h, id);
        after.ToSnapshot().UpstreamRevisionOf(StepKind.PrintDimensions).ShouldBe(approved);
        approved.ShouldNotBe(after.Revisions.Single(r => r.IsRoot).Id);
        approved.ShouldNotBe(after.Revisions.Single(r => r.Operation == OperationKind.Trim).Id);
    }

    [Fact]
    public async Task Changed_upstream_bytes_cannot_be_retained_as_if_still_approved()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await AtTrim(h, service, WorkflowType.PrepareCustomerDesign);
        SessionAggregate before = await Load(h, id);
        Revision upstream = before.Revisions.Single(r => r.IsRoot);
        string path = h.FileWorkspace.ResolveAbsolute(upstream.File);
        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        (await service.ExecuteAsync(id, new WorkflowCommand.KeepOriginalExtent(), "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        SessionAggregate after = await Load(h, id);
        after.Steps.ShouldBe(before.Steps);
        after.Attempts.ShouldBe(before.Attempts);
        after.Reviews.ShouldBe(before.Reviews);
        after.Revisions.Single(r => r.Id == upstream.Id).IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task Keeping_original_does_not_acquire_or_release_another_sessions_automation_lock()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await AtTrim(h, service, WorkflowType.PrepareCustomerDesign);
        SessionId other = (await Must(service.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourcePng("other.png"),
            "other", "tester", CancellationToken.None))).Id;
        SessionAggregate before = await Load(h, id);
        AutomationLockChange acquire = new(AutomationLockAction.Acquire, other, DateTimeOffset.UtcNow, 99999, "OTHER-MACHINE");
        (await h.Repository.CommitAsync(new SessionMutation(before.Session, [], [], [], [], [], [], null, acquire), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var held = (await h.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        held.IsHeld.ShouldBeTrue();
        await Execute(service, id, new WorkflowCommand.KeepOriginalExtent());
        (await h.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.ShouldBe(held);
    }

    [Theory]
    [InlineData(WorkflowType.PrepareCustomerDesign, "waiting")]
    [InlineData(WorkflowType.PrepareCustomerDesign, "review")]
    [InlineData(WorkflowType.PrepareCustomerDesign, "manual")]
    [InlineData(WorkflowType.PrepareAsset, "waiting")]
    [InlineData(WorkflowType.PrepareAsset, "review")]
    [InlineData(WorkflowType.PrepareAsset, "manual")]
    public async Task Decision_survives_restart_and_downstream_consumes_original(WorkflowType workflow, string phase)
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await AtTrim(h, service, workflow, phase == "manual");
        SessionView resumedBefore = await Must(h.CreateService().LoadAsync(id, CancellationToken.None));
        resumedBefore.CurrentStep!.Step.ShouldBe(StepKind.Trim);
        resumedBefore.OriginalExtentRetained.ShouldBeFalse();
        resumedBefore.CanKeepOriginalExtent.ShouldBeTrue();
        resumedBefore.AvailableCommands.ShouldContain(CommandKind.StartStep);
        if (phase != "waiting")
        {
            SessionView offered = await RunTrim(service, id, phase == "manual");
            offered.CurrentStep!.State.ShouldBe(phase == "review" ? StepState.ReviewRequired : StepState.Failed);
            if (phase == "manual") offered.CurrentStepFailure.ShouldBe(FailureCode.ManualCropRequired);
        }
        SessionAggregate before = await Load(h, id);
        Revision upstream = before.Revisions.Single(r => r.Id == before.ToSnapshot().UpstreamRevisionOf(StepKind.Trim));
        byte[] bytes = await File.ReadAllBytesAsync(h.FileWorkspace.ResolveAbsolute(upstream.File));
        string[] files = Directory.GetFiles(h.Workspace.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var lockBefore = (await h.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        await Execute(service, id, new WorkflowCommand.KeepOriginalExtent());
        service = h.CreateService();
        SessionView resumed = await Must(service.LoadAsync(id, CancellationToken.None));
        resumed.OriginalExtentRetained.ShouldBeTrue();
        resumed.CanKeepOriginalExtent.ShouldBeFalse();
        SessionAggregate after = await Load(h, id);
        after.Attempts.ShouldBe(before.Attempts);
        after.Revisions.ShouldBe(before.Revisions);
        after.Reviews.ShouldBe(before.Reviews);
        after.Steps.Single(s => s.Step == StepKind.Trim).SkipReason.ShouldBe(WorkflowCommand.KeepOriginalExtent.Reason);
        after.Steps.Single(s => s.Step == StepKind.Trim).CurrentRevisionId.ShouldBeNull();
        after.Steps.Single(s => s.Step == StepKind.Trim).AttemptCount.ShouldBe(phase == "waiting" ? 0 : 1);
        after.Revisions.Count(r => r.Operation == OperationKind.Trim).ShouldBe(phase == "review" ? 1 : 0);
        if (phase != "review") after.Attempts.ShouldAllBe(a => a.TrimGeometry == null);
        Directory.GetFiles(h.Workspace.Root, "*", SearchOption.AllDirectories).Order().ShouldBe(files);
        (await File.ReadAllBytesAsync(h.FileWorkspace.ResolveAbsolute(upstream.File))).ShouldBe(bytes);
        (await h.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.ShouldBe(lockBefore);
        StepKind next = workflow == WorkflowType.PrepareAsset ? StepKind.ApprovedPngExport : StepKind.PrintDimensions;
        resumed.CurrentStep!.Step.ShouldBe(next);
        after.ToSnapshot().UpstreamRevisionOf(next).ShouldBe(upstream.Id);
        if (workflow == WorkflowType.PrepareAsset)
        {
            await Execute(service, id, new WorkflowCommand.StartStep(next));
            after = await Load(h, id);
            Revision exported = after.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved);
            exported.SourceRevisionId.ShouldBe(upstream.Id);
            exported.Facts.PixelWidth.ShouldBe(upstream.Facts.PixelWidth);
            exported.Facts.PixelHeight.ShouldBe(upstream.Facts.PixelHeight);
            (await File.ReadAllBytesAsync(h.FileWorkspace.ResolveAbsolute(exported.File))).ShouldBe(bytes);
            after.Attempts.Single(a => a.Step == next).InputRevisionId.ShouldBe(upstream.Id);
        }
        else
        {
            resumed.CurrentArtefact!.RevisionId.ShouldBe(upstream.Id);
            resumed.CurrentArtefact.Facts.PixelWidth.ShouldBe(upstream.Facts.PixelWidth);
            resumed.CurrentArtefact.Facts.PixelHeight.ShouldBe(upstream.Facts.PixelHeight);
            await Execute(service, id, new WorkflowCommand.SetPrintDimensions(WorkflowScenario.CustomBox));
            after = await Load(h, id);
            after.Session.PrintPreparationPlan!.SourceRevisionId.ShouldBe(upstream.Id);
            after.ToSnapshot().UpstreamRevisionOf(StepKind.PhotoshopOutput).ShouldBe(upstream.Id);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Returning_to_trim_allows_real_adjusted_attempt_and_preserves_history(bool approveFirst)
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await AtTrim(h, service, WorkflowType.PrepareCustomerDesign);
        SessionView offered = await Execute(service, id, new WorkflowCommand.StartStep(StepKind.Trim));
        await Execute(service, id, approveFirst
            ? new WorkflowCommand.Approve(StepKind.Trim, offered.CurrentArtefact!.Sha256)
            : new WorkflowCommand.KeepOriginalExtent());
        SessionAggregate before = await Load(h, id);
        await Execute(service, id, new WorkflowCommand.SetPrintDimensions(WorkflowScenario.CustomBox));
        await Execute(service, id, new WorkflowCommand.ReturnToStep(StepKind.Trim));
        SessionAggregate reset = await Load(h, id);
        reset.Steps.Single(s => s.Step == StepKind.Trim).SkipReason.ShouldBeNull();
        reset.Session.Dimensions.ShouldBeNull();
        await Execute(service, id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(1)));
        offered = await Execute(service, id, new WorkflowCommand.StartStep(StepKind.Trim));
        offered.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        SessionAggregate after = await Load(h, id);
        foreach (Revision old in before.Revisions)
        {
            Revision retained = after.Revisions.Single(r => r.Id == old.Id);
            retained.File.ShouldBe(old.File);
            retained.Facts.ShouldBe(old.Facts);
            retained.SourceRevisionId.ShouldBe(old.SourceRevisionId);
        }
        after.Reviews.ShouldBe(before.Reviews);
        after.Attempts.Count(a => a.Step == StepKind.Trim).ShouldBe(2);
        after.Revisions.Count(r => r.Operation == OperationKind.Trim).ShouldBe(2);
    }

    [Fact]
    public async Task Returning_upstream_clears_no_trim_choice_and_invalidates_downstream_export()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await AtTrim(h, service, WorkflowType.PrepareAsset);
        await Execute(service, id, new WorkflowCommand.KeepOriginalExtent());
        await Execute(service, id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport));
        SessionAggregate before = await Load(h, id);
        Revision exported = before.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved);
        await Execute(service, id, new WorkflowCommand.ReturnToStep(StepKind.Enhancement));
        SessionAggregate reset = await Load(h, id);
        reset.Revisions.Single(r => r.Id == exported.Id).IsValid.ShouldBeFalse();
        reset.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Waiting);
        reset.Steps.Single(s => s.Step == StepKind.Trim).SkipReason.ShouldBeNull();
        SessionView enhanced = await Execute(service, id, new WorkflowCommand.StartStep(StepKind.Enhancement));
        await Execute(service, id, new WorkflowCommand.Approve(StepKind.Enhancement, enhanced.CurrentArtefact!.Sha256));
        await Execute(service, id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval));
        SessionView trimmed = await Execute(service, id, new WorkflowCommand.StartStep(StepKind.Trim));
        trimmed.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        SessionAggregate after = await Load(h, id);
        after.Revisions.Single(r => r.Operation == OperationKind.Trim).SourceRevisionId.ShouldBe(enhanced.CurrentArtefact.RevisionId);
    }

    internal static async Task<SessionId> AtTrim(SessionServiceHarness h, ISessionService service, WorkflowType type, bool opaque = false)
    {
        SessionView imported = await Must(service.ImportAsync(type,
            opaque ? h.WriteOpaqueSourcePng() : h.WriteBorderedSourcePng(), "keep-extent", "tester", CancellationToken.None));
        await Execute(service, imported.Id, new WorkflowCommand.ConfirmOriginal());
        await Execute(service, imported.Id, new WorkflowCommand.Skip(StepKind.Enhancement));
        await Execute(service, imported.Id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval));
        return imported.Id;
    }

    internal static Task<SessionView> Execute(ISessionService service, SessionId id, WorkflowCommand command) =>
        Must(service.ExecuteAsync(id, command, "tester", CancellationToken.None));

    internal static async Task<SessionView> RunTrim(ISessionService service, SessionId id, bool manual)
    {
        OperationResult<SessionView> result = await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        if (manual) { result.IsFailure.ShouldBeTrue(); result.Failure.Code.ShouldBe(FailureCode.ManualCropRequired); }
        else result.IsSuccess.ShouldBeTrue();
        return await Must(service.LoadAsync(id, CancellationToken.None));
    }

    internal static async Task<SessionAggregate> Load(SessionServiceHarness h, SessionId id) =>
        (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    private static async Task<SessionView> Must(Task<OperationResult<SessionView>> pending)
    {
        OperationResult<SessionView> result = await pending;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
        return result.Value;
    }
}
