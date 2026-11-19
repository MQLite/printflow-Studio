using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class DeliveryCoordinationTests
{
    [Fact]
    public async Task A_verified_Work_result_import_is_idempotent_and_rejects_changed_bytes()
    {
        using SessionServiceHarness harness = new();
        var service = harness.CreateService();
        string path = harness.WriteSourcePng();
        var facts = (await harness.FileInspector.InspectAsync(path, default)).Value;
        var requestId = Guid.NewGuid();

        var incorrect = await service.ImportVerifiedWorkResultAsync(requestId, path, "Work result",
            Sha256.Parse(new string('A', 64)), default);
        incorrect.IsFailure.ShouldBeTrue();
        (await harness.Repository.LoadAsync(SessionId.From(requestId), default)).Value.ShouldBeNull();

        var first = await service.ImportVerifiedWorkResultAsync(requestId, path, "Work result", facts.Sha256, default);
        var second = await harness.CreateService().ImportVerifiedWorkResultAsync(requestId, path, "Work result", facts.Sha256, default);
        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        first.Value.Id.ShouldBe(second.Value.Id);
        var saved = (await harness.Repository.LoadAsync(first.Value.Id, default)).Value!;
        saved.Revisions.ShouldHaveSingleItem().Sha256.ShouldBe(facts.Sha256);
        saved.Attempts.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Ordinary_approval_command_waits_for_shared_session_delivery_gate()
    {
        using SessionServiceHarness harness = new();
        var service = harness.CreateService();
        SessionId id = (await service.ImportAsync(WorkflowType.PrepareAsset,
            harness.WriteSourcePng(), "art", "tester", CancellationToken.None)).Value.Id;
        Task<PrintFlow.Domain.Results.OperationResult<SessionView>> command;
        using (await SessionCompletionGate.EnterAsync(id, CancellationToken.None))
        {
            command = service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);
            (await Task.WhenAny(command, Task.Delay(150))).ShouldNotBe(command);
        }
        (await command).IsSuccess.ShouldBeTrue();
    }
}
