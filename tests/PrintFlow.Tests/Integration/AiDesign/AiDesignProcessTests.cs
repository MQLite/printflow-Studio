using System.Diagnostics;
using System.IO;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Unit.AiDesign;

namespace PrintFlow.Tests.Integration.AiDesign;

public sealed class AiDesignProcessTests
{
    private static Process Start(params string[] args)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(typeof(PrintFlow.AiDesignProbe.Program).Assembly.Location);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info)!;
    }
    private static async Task Ready(string marker, Process process)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!File.Exists(marker))
        {
            if (process.HasExited) throw new InvalidOperationException($"Probe exit {process.ExitCode}: {await process.StandardError.ReadToEndAsync()}");
            await Task.Delay(20, timeout.Token);
        }
    }
    private static void Kill(Process p) { if (!p.HasExited) p.Kill(true); }
    [Fact]
    public async Task Crash_during_repeated_atomic_replacement_never_exposes_partial_authority()
    {
        using TempWorkspace files = new(); var store = new FileAiDesignRequestStore(files.Root); var d = AiDesignTransitionsTests.Draft();
        (await store.CreateAsync(d.RequestId, new(d.Prompt with { Text = "version:0" }, []), default)).IsSuccess.ShouldBeTrue();
        var ready = Path.Combine(files.Root, "ready"); var go = Path.Combine(files.Root, "go");
        using var p = Start("write-crash", files.Root, d.RequestId.ToString(), ready, go);
        try
        {
            await Ready(ready, p); await File.WriteAllTextAsync(go, "go");
            for (int i = 0; i < 50; i++)
            {
                var observed = await store.ReadAsync(d.RequestId, default); observed.IsSuccess.ShouldBeTrue();
                observed.Value.Prompt.Text.ShouldBe("version:" + observed.Value.StateVersion);
                await Task.Delay(2);
            }
            p.Kill(true); await p.WaitForExitAsync();
            var recovered = (await store.ReadAsync(d.RequestId, default)).Value;
            recovered.StateVersion.ShouldBeGreaterThan(0); recovered.Prompt.Text.ShouldBe("version:" + recovered.StateVersion);
            recovered.State.ShouldBe(AiDesignState.PromptReady);
        }
        finally { Kill(p); }
    }
    [Fact]
    public async Task Two_process_CAS_preserves_one_winner()
    {
        using TempWorkspace files = new(); var store = new FileAiDesignRequestStore(files.Root); var d = AiDesignTransitionsTests.Draft();
        (await store.CreateAsync(d.RequestId, new(d.Prompt, []), default)).IsSuccess.ShouldBeTrue();
        var go = Path.Combine(files.Root, "go"); var a = Path.Combine(files.Root, "a.ready"); var b = Path.Combine(files.Root, "b.ready");
        using var p1 = Start("cas", files.Root, d.RequestId.ToString(), a, go);
        using var p2 = Start("cas", files.Root, d.RequestId.ToString(), b, go);
        try
        {
            await Ready(a, p1); await Ready(b, p2); await File.WriteAllTextAsync(go, "go");
            await p1.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); await p2.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            new[] { p1.ExitCode, p2.ExitCode }.Order().ShouldBe(new[] { 0, 2 });
            var r = (await store.ReadAsync(d.RequestId, default)).Value; r.StateVersion.ShouldBe(1); r.State.ShouldBe(AiDesignState.Queued);
        }
        finally { Kill(p1); Kill(p2); }
    }
    [Fact]
    public async Task Installation_lease_excludes_other_process_and_releases_on_crash()
    {
        using TempWorkspace files = new(); var ready = Path.Combine(files.Root, "ready"); var release = Path.Combine(files.Root, "release");
        using var p = Start("lease", files.Root, ready, release);
        try
        {
            await Ready(ready, p);
            new AiDesignBrowserLease(files.Root).TryAcquire(Guid.NewGuid()).IsFailure.ShouldBeTrue();
            p.Kill(true); await p.WaitForExitAsync();
            var next = new AiDesignBrowserLease(files.Root).TryAcquire(Guid.NewGuid()); next.IsSuccess.ShouldBeTrue(); next.Value.Dispose();
        }
        finally { Kill(p); }
    }
    [Fact]
    public async Task Crash_during_click_keeps_atomic_reservation_and_inputs_without_replay()
    {
        using TempWorkspace files = new(); var store = new FileAiDesignRequestStore(files.Root); var d = AiDesignTransitionsTests.Draft();
        var r = (await store.CreateAsync(d.RequestId, new(d.Prompt, []), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.Queued(Guid.NewGuid(), "token", 1, 1, Guid.NewGuid()), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.UploadStarted("tab", "conversation"), default)).Value;
        var ready = Path.Combine(files.Root, "ready"); var go = Path.Combine(files.Root, "go"); var reserved = Path.Combine(files.Root, "reserved");
        using var p = Start("submit-crash", files.Root, r.RequestId.ToString(), ready, go, reserved);
        try
        {
            await Ready(ready, p); await File.WriteAllTextAsync(go, "go"); await Ready(reserved, p);
            p.Kill(true); await p.WaitForExitAsync();
            var recovered = (await store.ReadAsync(r.RequestId, default)).Value;
            recovered.SubmissionReservedAtUtc.ShouldNotBeNull(); recovered.Prompt.Text.ShouldBe("edited literal prompt");
            (await store.SubmitOnceAsync(recovered.Fence, _ => throw new InvalidOperationException("Replay must not run"), default)).IsFailure.ShouldBeTrue();
            (await store.ChangeAsync(recovered.Fence, new AiDesignEvent.Interrupted(), default)).Value.State.ShouldBe(AiDesignState.Interrupted);
        }
        finally { Kill(p); }
    }
}
