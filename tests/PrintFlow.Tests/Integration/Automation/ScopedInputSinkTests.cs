using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Automation;

// The `Unit` result type, aliased inside the namespace: `PrintFlow.Tests.Unit` is a namespace
// in this assembly, and an enclosing namespace wins over a compilation-unit alias.
using Unit = PrintFlow.Domain.Results.Unit;

/// <summary>
/// The guard inside the keyboard primitive itself (Epic 11300 Part A §3, §19).
/// </summary>
/// <remarks>
/// The class under test is the real <see cref="Win32ScopedInputSink"/> — the only thing in the
/// solution that can call <c>SendInput</c>. Each case scripts a foreground that is not the
/// target, so the guard returns before dispatch. The native boundary is explicitly replaced
/// with a recording delegate, so even a guard regression cannot send real desktop input.
///
/// There is deliberately no "happy path" test here that actually sends a keystroke. Proving the
/// send works means typing into a live window from a test run, and the workstation smoke — run
/// deliberately, against a known Meitu — is the right place for that.
/// </remarks>
public sealed class ScopedInputSinkTests
{
    private static Win32ScopedInputSink SinkWithForeground(ForegroundIdentity foreground)
    {
        FakeWindowLocator locator = new() { Foreground = foreground };
        return new Win32ScopedInputSink(locator, (_, _, _) =>
            throw new InvalidOperationException("A refused synthetic send must never reach native dispatch."));
    }

    [Fact]
    public void A_keystroke_is_refused_when_another_application_holds_the_foreground()
    {
        Win32ScopedInputSink sink = SinkWithForeground(
            new ForegroundIdentity(new WindowHandle(0xE1E1), 777, "explorer"));

        bool dispatched = false;
        OperationResult<Unit> sent = sink.SendShortcut(new WindowHandle(0x1000), KnownShortcut.OpenFile,
            () => dispatched = true);

        sent.IsFailure.ShouldBeTrue();
        sent.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        sent.Failure.Context["inputSent"].ShouldBe("false");
        sent.Failure.Context["actualProcess"].ShouldBe("explorer");
        dispatched.ShouldBeFalse();
    }

    [Fact]
    public void Partial_dispatch_is_observed_without_claiming_success()
    {
        WindowHandle target = new(0x1000);
        FakeWindowLocator locator = new() { Foreground = new(target, 777, "synthetic") };
        bool dispatched = false;
        int calls = 0;
        Win32ScopedInputSink sink = new(locator, (count, _, _) =>
        {
            dispatched.ShouldBeTrue("the milestone is recorded at dispatch, before its result");
            calls++;
            return count - 1;
        });
        OperationResult<Unit> result = sink.SendShortcut(target, KnownShortcut.CloseActiveDocument,
            () => dispatched = true);
        result.IsFailure.ShouldBeTrue();
        calls.ShouldBe(1);
        result.Failure.Code.ShouldBe(FailureCode.MeituOpenInputFailed);
    }

    [Fact]
    public void A_keystroke_is_refused_when_nothing_holds_the_foreground()
    {
        Win32ScopedInputSink sink = SinkWithForeground(new ForegroundIdentity(WindowHandle.None, 0, "(none)"));

        sink.SendShortcut(new WindowHandle(0x1000), KnownShortcut.OpenFile)
            .Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
    }

    /// <summary>Without a target there is nothing to verify against, so nothing is sent.</summary>
    [Fact]
    public void A_keystroke_with_no_target_window_is_refused()
    {
        Win32ScopedInputSink sink = SinkWithForeground(
            new ForegroundIdentity(WindowHandle.None, 0, "(none)"));

        sink.SendShortcut(WindowHandle.None, KnownShortcut.Escape)
            .Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
    }
}
