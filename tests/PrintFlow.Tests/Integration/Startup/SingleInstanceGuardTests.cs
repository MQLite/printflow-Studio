using System.IO;
using PrintFlow.Infrastructure.Startup;

namespace PrintFlow.Tests.Integration.Startup;

/// <summary>
/// The real Windows guard, against a real exclusively-opened lock file
/// (Epic 11100 Part 3C1 §2, §3).
/// </summary>
/// <remarks>
/// This is the part Part 3C1 §10 asks to be provable without racing real processes. It is: the
/// guard's claim is a process-scoped file lock, so a second guard object in this process is
/// refused exactly as a second PrintFlow process would be. Each test uses its own lock path so
/// the cases cannot interfere with each other, or with a PrintFlow running on the workstation.
/// </remarks>
public sealed class SingleInstanceGuardTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PrintFlowTests", Guid.NewGuid().ToString("N"));

    private string UniqueLockPath() => Path.Combine(_directory, $"{Guid.NewGuid():N}.lock");

    [Fact]
    public void The_first_instance_acquires_the_guard()
    {
        using SingleInstanceGuard first = new(UniqueLockPath());

        first.TryAcquire().ShouldBe(SingleInstanceOutcome.Acquired);
        first.IsHeld.ShouldBeTrue();
    }

    [Fact]
    public void A_second_instance_is_refused_while_the_first_holds_the_guard()
    {
        string path = UniqueLockPath();
        using SingleInstanceGuard first = new(path);
        first.TryAcquire().ShouldBe(SingleInstanceOutcome.Acquired);

        using SingleInstanceGuard second = new(path);

        second.TryAcquire().ShouldBe(SingleInstanceOutcome.AlreadyRunning);
        second.IsHeld.ShouldBeFalse();

        // The refusal changed nothing for the owner.
        first.IsHeld.ShouldBeTrue();
    }

    [Fact]
    public void Acquiring_twice_from_the_owner_is_idempotent()
    {
        using SingleInstanceGuard guard = new(UniqueLockPath());

        guard.TryAcquire().ShouldBe(SingleInstanceOutcome.Acquired);
        guard.TryAcquire().ShouldBe(SingleInstanceOutcome.Acquired);
        guard.IsHeld.ShouldBeTrue();
    }

    [Fact]
    public void The_guard_is_released_only_by_disposal_and_a_later_instance_can_then_acquire_it()
    {
        string path = UniqueLockPath();

        SingleInstanceGuard first = new(path);
        first.TryAcquire().ShouldBe(SingleInstanceOutcome.Acquired);

        using (SingleInstanceGuard blocked = new(path))
        {
            blocked.TryAcquire().ShouldBe(SingleInstanceOutcome.AlreadyRunning);
        }

        first.Dispose();
        first.IsHeld.ShouldBeFalse();

        // A leftover lock file is not a claim: the operating system released the handle, so the
        // next launch acquires it cleanly. This is also what happens after a crash.
        File.Exists(path).ShouldBeTrue();

        using SingleInstanceGuard next = new(path);
        next.TryAcquire().ShouldBe(SingleInstanceOutcome.Acquired);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
