using PrintFlow.WorkstationEntry;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

public sealed class OwnedWorkTests
{
    [Fact]
    public async Task Open_registration_cannot_claim_quiescence()
    { OwnedWork work = new(); (await work.WaitForQuiescenceAsync(TimeSpan.FromSeconds(1))).ShouldBeFalse(); }
    [Fact]
    public async Task Closed_registry_waits_for_all_owned_tasks()
    {
        TaskCompletionSource done = new(); OwnedWork work = new(); work.Track(done.Task); work.CloseRegistration();
        (await work.WaitForQuiescenceAsync(TimeSpan.FromMilliseconds(20))).ShouldBeFalse();
        done.SetResult(); (await work.WaitForQuiescenceAsync(TimeSpan.FromSeconds(1))).ShouldBeTrue();
    }
    [Fact]
    public async Task Late_registration_remains_incomplete_even_if_task_finishes()
    {
        OwnedWork work = new(); work.CloseRegistration(); work.Track(Task.CompletedTask);
        (await work.WaitForQuiescenceAsync(TimeSpan.FromSeconds(1))).ShouldBeFalse(); work.LateRegistration.ShouldBeTrue();
    }
}
