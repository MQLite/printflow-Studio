using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class DeliveryCoordinationTests
{
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
