using System.Diagnostics;
using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Files;

/// <summary>
/// Epic 11300 final-gate blocker fix 2: cancelling an in-flight source import is a structured
/// result, never an exception that leaves the workspace boundary.
/// </summary>
/// <remarks>
/// The R3 closure gate recorded a terminated WPF process whose Windows Application-log entry
/// named <c>FileWorkspace.ImportSourceAsync</c> and
/// <c>System.Threading.Tasks.TaskCanceledException</c>: the copy caught <see cref="IOException"/>
/// and <see cref="UnauthorizedAccessException"/> but not <see cref="OperationCanceledException"/>,
/// so a cancelled import escaped every frame up to the view model and killed the shell. These
/// tests hold that boundary closed from both directions — cancellation must be reported, and a
/// genuine filesystem failure must still be reported as one.
/// </remarks>
public sealed class WorkspaceImportCancellationTests
{
    /// <summary>
    /// A source big enough that a copy of it cannot finish inside the rendezvous below.
    /// </summary>
    /// <remarks>
    /// Created with <see cref="FileStream.SetLength"/>, so making it costs no writes and no
    /// measurable time; only the <em>copy</em> of it is slow, which is exactly the asymmetry
    /// these tests need. 64 MiB copies in roughly 50 ms on the workstation this was measured on,
    /// against a rendezvous that observes the first flushed megabyte after 1–2 ms — so the
    /// cancellation lands with about fifty times the margin it needs, and a lost race fails the
    /// test loudly rather than passing quietly, because every assertion below insists on the
    /// cancelled outcome.
    /// </remarks>
    private const long LargeSourceBytes = 64L * 1024 * 1024;

    /// <summary>How long a rendezvous waits before giving up and letting the assertions speak.</summary>
    private static readonly TimeSpan RendezvousTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Cancelling_a_copy_in_flight_returns_a_structured_cancellation_and_throws_nothing()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        WorkspaceDirRef session = fileWorkspace.CreateSession(
            SessionId.From(Guid.CreateVersion7()), DateTimeOffset.UtcNow).Value;

        string sourcePath = CreateLargeSource(workspace, "in-flight.png");
        string sourceArea = Path.Combine(workspace.Root, session.RelativePath, "Source");

        using CancellationTokenSource cancellation = new();
        Task<OperationResult<WorkspaceFileRef>> importing =
            Task.Run(() => fileWorkspace.ImportSourceAsync(session, sourcePath, cancellation.Token));

        CancelOnceBytesHaveLanded(cancellation, sourceArea, importing)
            .ShouldBeTrue("the copy must have written bytes before the cancellation, or this proves nothing");

        // The whole point: awaiting a cancelled import yields a result. Before the fix this line
        // threw TaskCanceledException, which is what terminated the shell in R3.
        OperationResult<WorkspaceFileRef> imported = await importing;

        imported.IsFailure.ShouldBeTrue();
        imported.Failure.Code.ShouldBe(FailureCode.Cancelled);
        Should.Throw<InvalidOperationException>(() => imported.Value)
            .Message.ShouldContain("Cannot read the value of a failed result");
    }

    /// <summary>
    /// The separate, simpler case: a token that was already cancelled when the method was called.
    /// </summary>
    [Fact]
    public async Task An_import_asked_for_with_an_already_cancelled_token_is_refused_the_same_way()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        WorkspaceDirRef session = fileWorkspace.CreateSession(
            SessionId.From(Guid.CreateVersion7()), DateTimeOffset.UtcNow).Value;

        string sourcePath = workspace.CreateSourceFile("already-cancelled.png", SyntheticImages.Png());

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        OperationResult<WorkspaceFileRef> imported =
            await fileWorkspace.ImportSourceAsync(session, sourcePath, cancelled.Token);

        imported.IsFailure.ShouldBeTrue();
        imported.Failure.Code.ShouldBe(FailureCode.Cancelled);
        Directory.EnumerateFiles(Path.Combine(workspace.Root, session.RelativePath, "Source"))
            .ShouldBeEmpty("a refused import leaves nothing behind that could be read as a snapshot");
    }

    /// <summary>
    /// The partial destination that cancellation leaves behind is quarantined, never kept where a
    /// later reader could mistake it for the snapshot and never hard-deleted.
    /// </summary>
    /// <remarks>
    /// This is the case the fix brief singles out: the destination is created, the copy starts,
    /// and cancellation arrives before it finishes. The bytes on disk at that moment are a
    /// truncated prefix of the operator's file sitting under exactly the name a <em>successful</em>
    /// import would have written — so leaving them in <c>Source\</c> would be leaving a decoy.
    /// They move to <c>Quarantine\</c> instead, which is the route this workspace already owns for
    /// "a file exists with no corresponding metadata", and which moves rather than deletes because
    /// there is deliberately no hard-delete path anywhere in the solution.
    /// </remarks>
    [Fact]
    public async Task A_cancelled_copy_leaves_a_quarantined_partial_file_and_an_empty_Source_area()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        WorkspaceDirRef session = fileWorkspace.CreateSession(
            SessionId.From(Guid.CreateVersion7()), DateTimeOffset.UtcNow).Value;

        string sourcePath = CreateLargeSource(workspace, "half-copied.png");
        string sourceArea = Path.Combine(workspace.Root, session.RelativePath, "Source");

        using CancellationTokenSource cancellation = new();
        Task<OperationResult<WorkspaceFileRef>> importing =
            Task.Run(() => fileWorkspace.ImportSourceAsync(session, sourcePath, cancellation.Token));

        CancelOnceBytesHaveLanded(cancellation, sourceArea, importing)
            .ShouldBeTrue("the copy must have written bytes before the cancellation, or this proves nothing");

        OperationResult<WorkspaceFileRef> imported = await importing;

        imported.Failure.Code.ShouldBe(FailureCode.Cancelled);
        imported.Failure.Context["partialDestination"].ShouldBe("quarantined");
        imported.Failure.Context["sourceFileName"].ShouldBe("half-copied.png");

        Directory.EnumerateFiles(sourceArea)
            .ShouldBeEmpty("the half-written file must not stay under the name a snapshot would have");

        string quarantine = Path.Combine(workspace.Root, "Quarantine");
        string quarantined = Directory.EnumerateFiles(quarantine, "*half-copied.png").ShouldHaveSingleItem();

        // Genuinely partial, and genuinely still there: retained for inspection, not deleted.
        long quarantinedLength = new FileInfo(quarantined).Length;
        quarantinedLength.ShouldBeGreaterThan(0);
        quarantinedLength.ShouldBeLessThan(LargeSourceBytes);

        File.ReadAllText(quarantined + ".reason.txt")
            .ShouldContain("Cancelled part-way through importing");

        // The operator's own file is untouched by any of this.
        new FileInfo(sourcePath).Length.ShouldBe(LargeSourceBytes);
    }

    /// <summary>
    /// A genuine filesystem failure is still a genuine filesystem failure — cancellation handling
    /// must not have collapsed the two together.
    /// </summary>
    [Fact]
    public async Task A_destination_that_already_exists_is_still_a_WorkspaceError_and_not_a_cancellation()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        WorkspaceDirRef session = fileWorkspace.CreateSession(
            SessionId.From(Guid.CreateVersion7()), DateTimeOffset.UtcNow).Value;

        string sourcePath = workspace.CreateSourceFile("occupied.png", SyntheticImages.Png());
        string sourceArea = Path.Combine(workspace.Root, session.RelativePath, "Source");
        File.WriteAllText(Path.Combine(sourceArea, "occupied.png"), "already here");

        OperationResult<WorkspaceFileRef> imported =
            await fileWorkspace.ImportSourceAsync(session, sourcePath, CancellationToken.None);

        imported.IsFailure.ShouldBeTrue();
        imported.Failure.Code.ShouldBe(FailureCode.WorkspaceError);
        Directory.Exists(Path.Combine(workspace.Root, "Quarantine"))
            .ShouldBeFalse("an ordinary failure is not a cancellation and quarantines nothing");
    }

    /// <summary>
    /// An ordinary import driven with a live, cancellable token still behaves exactly as before:
    /// the new catch must be invisible to the path that is not cancelled.
    /// </summary>
    [Fact]
    public async Task An_ordinary_import_under_a_live_token_still_produces_the_read_only_snapshot()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        WorkspaceDirRef session = fileWorkspace.CreateSession(
            SessionId.From(Guid.CreateVersion7()), DateTimeOffset.UtcNow).Value;

        byte[] content = SyntheticImages.Png();
        string sourcePath = workspace.CreateSourceFile("ordinary.png", content);

        using CancellationTokenSource live = new();
        OperationResult<WorkspaceFileRef> imported =
            await fileWorkspace.ImportSourceAsync(session, sourcePath, live.Token);

        imported.IsSuccess.ShouldBeTrue();
        imported.Value.RelativePath.ShouldBe($"{session.RelativePath}/Source/ordinary.png");
        imported.Value.Area.ShouldBe(WorkspaceArea.Source);

        string snapshot = fileWorkspace.ResolveAbsolute(imported.Value);
        File.ReadAllBytes(snapshot).ShouldBe(content);
        File.GetAttributes(snapshot).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();

        File.ReadAllBytes(sourcePath).ShouldBe(content, "the customer's own file is never modified");
        Directory.Exists(Path.Combine(workspace.Root, "Quarantine")).ShouldBeFalse();
    }

    /// <summary>
    /// Creates a source whose copy takes real time but whose creation takes none.
    /// </summary>
    private static string CreateLargeSource(TempWorkspace workspace, string fileName)
    {
        string path = Path.Combine(workspace.Root, fileName);
        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.SetLength(LargeSourceBytes);
        return path;
    }

    /// <summary>
    /// Cancels only once the copy has actually put bytes on disk, and reports whether it did.
    /// </summary>
    /// <remarks>
    /// A rendezvous rather than a sleep: the loop watches for the observable effect of the copy
    /// itself — a flushed chunk in the destination area — and cancels the instant it appears, so
    /// "cancelled during the copy" is a sequence the test established rather than one it hoped
    /// for. It runs on the test's own thread, never a pool thread, so a busy thread pool can
    /// delay the copy but never the observer. It gives up if the import completes first, and the
    /// caller asserts on the answer instead of quietly continuing.
    /// </remarks>
    private static bool CancelOnceBytesHaveLanded(
        CancellationTokenSource cancellation, string destinationDirectory, Task pending)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        bool landed = false;

        while (elapsed.Elapsed < RendezvousTimeout)
        {
            if (BytesLandedIn(destinationDirectory) > 0)
            {
                landed = true;
                break;
            }

            if (pending.IsCompleted)
            {
                break;
            }
        }

        cancellation.Cancel();
        return landed;
    }

    private static long BytesLandedIn(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // The file is being written; its length will be readable on the next pass.
            }
        }

        return total;
    }
}
