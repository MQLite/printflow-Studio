using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// The PSD output settle poll, stated as a timeline instead of a race.
/// </summary>
/// <remarks>
/// <c>PhotoshopPsdBoundaryTests</c> failed intermittently for months — <c>malformed</c> and
/// <c>success</c> both observed returning <c>OutputUnreadable</c> under full-suite load and
/// passing on isolated rerun — because the loop's two inputs, how long an observation costs and
/// how much budget is left, were both owned by the machine. Nothing about that is testable by
/// re-running it.
///
/// Every case here fixes both. The clock advances only when this file says so, and each
/// observation is a scripted step that states its cost and what it did to the file first. The
/// bytes on disk are real and the reader is the real <see cref="WicFileInspector"/>, so a case
/// that reaches validation reaches it over the same bytes production would read; only the
/// <i>timing</i> is written down rather than measured. No test here sleeps, and none would
/// answer differently on a loaded machine.
/// </remarks>
public sealed class PhotoshopPsdSettleDeterminismTests
{
    // The whole budget, and a free poll, so elapsed virtual time is exactly the observation cost.
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(40);

    private static readonly TimeSpan FreePoll = TimeSpan.Zero;

    /// <summary>
    /// The pre-fix reproduction: one slow observation spent the entire budget, and the settle
    /// poll reported the perfectly stable output as unreadable without ever looking at it twice.
    /// </summary>
    /// <remarks>
    /// This is the exact shape of the load-sensitive failure. Under full-suite parallelism the
    /// first read of the prepared raster — a cold WIC decode, a thread-pool continuation waiting
    /// its turn — could cost more than the 40 ms the boundary fixture allowed, and the old loop
    /// checked its deadline <i>after</i> the first observation and gave up holding exactly one
    /// observation. One observation is not evidence of anything: the contract asks for two equal
    /// reads, and the loop had never taken the second.
    ///
    /// Against the old algorithm this case fails with <c>OutputUnreadable</c> and one
    /// observation. It is kept as the standing proof that the budget can no longer truncate the
    /// minimum sequence.
    /// </remarks>
    [Fact]
    public async Task One_slow_observation_no_longer_spends_the_settle_budget_before_the_first_comparison()
    {
        using SettleScenario scenario = new(Budget, FreePoll);
        scenario.Inspector.Observe(cost: Budget);   // the whole budget, in the first read
        scenario.Inspector.Observe(cost: TimeSpan.Zero);

        var result = await scenario.PrepareAsync(SyntheticImages.Png(4, 3));

        result.IsSuccess.ShouldBeTrue(scenario.Explain(result));
        scenario.Inspector.Observations.ShouldBe(2);
        result.Value.PsdInspection!.HasTransparency.ShouldBe(true);
    }

    /// <summary>An output that is final before the first read settles on the minimum pair.</summary>
    [Fact]
    public async Task An_output_that_is_stable_immediately_settles_on_the_minimum_pair()
    {
        using SettleScenario scenario = new(Budget, FreePoll);
        scenario.Inspector.Observe(cost: TimeSpan.FromMilliseconds(5));
        scenario.Inspector.Observe(cost: TimeSpan.FromMilliseconds(5));

        var result = await scenario.PrepareAsync(SyntheticImages.Png(4, 3));

        result.IsSuccess.ShouldBeTrue(scenario.Explain(result));
        scenario.Inspector.Observations.ShouldBe(2);
        scenario.Elapsed.ShouldBe(TimeSpan.FromMilliseconds(10));
    }

    /// <summary>Size A, then size B, then B again: the first equal pair is what settles it.</summary>
    [Fact]
    public async Task An_output_that_changes_once_settles_on_the_first_equal_pair()
    {
        using SettleScenario scenario = new(Budget, FreePoll);
        byte[] finished = SyntheticImages.Png(4, 3);
        scenario.Inspector.Observe(cost: TimeSpan.FromMilliseconds(5));                        // partial bytes
        scenario.Inspector.Observe(cost: TimeSpan.FromMilliseconds(5), writes: finished);      // the finished raster
        scenario.Inspector.Observe(cost: TimeSpan.FromMilliseconds(5));                        // unchanged

        var result = await scenario.PrepareAsync(Partial(11));

        result.IsSuccess.ShouldBeTrue(scenario.Explain(result));
        scenario.Inspector.Observations.ShouldBe(3);
        File.ReadAllBytes(scenario.OutputPath).ShouldBe(finished);
    }

    /// <summary>
    /// An output still being written when the budget runs out fails closed, and says so as an
    /// output problem rather than a validation one.
    /// </summary>
    [Fact]
    public async Task An_output_that_keeps_changing_fails_closed_as_unreadable()
    {
        using SettleScenario scenario = new(Budget, FreePoll);
        for (int i = 0; i < 8; i++)
        {
            scenario.Inspector.Observe(cost: TimeSpan.FromMilliseconds(10), writes: Partial(20 + i));
        }

        var result = await scenario.PrepareAsync(Partial(11));

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputUnreadable);
        scenario.Inspector.Observations.ShouldBe(4);
        scenario.Elapsed.ShouldBe(Budget);
        File.Exists(scenario.OutputPath).ShouldBeTrue();
    }

    /// <summary>
    /// The boundary itself: an observation that completes exactly on the deadline still counts.
    /// </summary>
    /// <remarks>
    /// The deadline ends the <i>waiting</i>, not the comparison the poll has already earned. A
    /// pair that agrees at the last possible instant is a settled file, and calling it unreadable
    /// would be a statement about the clock rather than about the bytes.
    /// </remarks>
    [Fact]
    public async Task An_output_that_becomes_stable_exactly_on_the_deadline_still_settles()
    {
        using SettleScenario scenario = new(Budget, FreePoll);
        byte[] finished = SyntheticImages.Png(4, 3);
        scenario.Inspector.Observe(cost: TimeSpan.FromMilliseconds(10), writes: finished);
        scenario.Inspector.Observe(cost: TimeSpan.FromMilliseconds(30));   // lands on 40 ms exactly

        var result = await scenario.PrepareAsync(Partial(11));

        result.IsSuccess.ShouldBeTrue(scenario.Explain(result));
        scenario.Elapsed.ShouldBe(Budget);
        scenario.Inspector.Observations.ShouldBe(2);
    }

    /// <summary>A raster that never appears is missing, not unreadable.</summary>
    [Fact]
    public async Task An_output_that_never_appears_fails_as_missing()
    {
        using SettleScenario scenario = new(Budget, FreePoll);
        scenario.Inspector.Observe(cost: TimeSpan.FromMilliseconds(20));
        scenario.Inspector.Observe(cost: TimeSpan.FromMilliseconds(20));

        var result = await scenario.PrepareAsync(output: null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputMissing);
        scenario.Inspector.Observations.ShouldBe(2);
        File.Exists(scenario.OutputPath).ShouldBeFalse();
    }

    /// <summary>
    /// A stable but structurally malformed raster settles first and is then refused by
    /// <c>ValidatePsdRaster</c> — the phase order the boundary matrix depends on.
    /// </summary>
    /// <remarks>
    /// This is the case that failed under load, and the distinction it proves is the whole point
    /// of separating the two phases. "Still changing" and "finished and wrong" are different
    /// facts about an output, they carry different failure codes, and a settle poll that can be
    /// starved of observations collapses the second into the first.
    /// </remarks>
    [Fact]
    public async Task A_stable_malformed_raster_settles_first_and_then_fails_psd_validation()
    {
        using SettleScenario scenario = new(Budget, FreePoll);
        scenario.Inspector.Observe(cost: Budget);   // the same starvation as the reproduction above
        scenario.Inspector.Observe(cost: TimeSpan.Zero);

        var result = await scenario.PrepareAsync([1, 2, 3]);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PsdPreparationFailed);
        result.Failure.TechnicalDetail.ShouldContain("RGB/8");
        scenario.Inspector.Observations.ShouldBe(2);
    }

    /// <summary>
    /// A budget too short to hold the observations the rule needs is refused up front, instead of
    /// becoming an unreadable output whenever the machine is slow enough.
    /// </summary>
    [Fact]
    public async Task An_impossible_settle_policy_is_refused_before_photoshop_is_touched()
    {
        using SettleScenario scenario = new(
            timeout: TimeSpan.FromMilliseconds(1), interval: TimeSpan.FromMilliseconds(500));

        var result = await scenario.PrepareAsync(SyntheticImages.Png(4, 3));

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PsdPreparationFailed);
        result.Failure.TechnicalDetail.ShouldContain("impossible to satisfy");
        result.Failure.TechnicalDetail.ShouldContain("500 ms");
        scenario.NativeCalls.ShouldBe(0);
        scenario.OpenCount.ShouldBe(0);
        scenario.Inspector.Observations.ShouldBe(0);
        File.Exists(scenario.OutputPath).ShouldBeFalse();
    }

    /// <summary>The invariant on its own, without an adapter around it.</summary>
    [Theory]
    [InlineData(40, 2, 2, true)]
    [InlineData(500, 500, 2, true)]     // exactly the minimum window is satisfiable
    [InlineData(499, 500, 2, false)]
    [InlineData(60000, 500, 2, true)]   // the production configuration
    [InlineData(900, 500, 3, false)]    // three observations need two intervals
    [InlineData(1000, 500, 3, true)]
    [InlineData(40, 2, 1, false)]       // one read is never evidence of stability
    [InlineData(40, -1, 2, false)]
    public void The_settle_policy_accepts_only_combinations_an_output_could_satisfy(
        int timeoutMs, int intervalMs, int required, bool accepted)
    {
        var policy = PsdOutputSettlePolicy.Create(
            TimeSpan.FromMilliseconds(timeoutMs), TimeSpan.FromMilliseconds(intervalMs), required);

        policy.IsSuccess.ShouldBe(accepted, policy.IsFailure ? policy.Failure.TechnicalDetail : "");
        if (accepted)
        {
            policy.Value.MinimumObservationWindow.ShouldBe(
                TimeSpan.FromMilliseconds(intervalMs * (required - 1)));
        }
    }

    /// <summary>Bytes that are neither a finished raster nor nothing: a write in progress.</summary>
    private static byte[] Partial(int length) => [.. Enumerable.Range(0, length).Select(i => (byte)i)];

    // -----------------------------------------------------------------------------------
    // The controllable seams: a clock that only this file advances, and a scripted reader
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A clock whose reading changes only when a scripted observation says it cost something.
    /// </summary>
    /// <remarks>
    /// Timers are deliberately left to the base implementation. Every case here polls at
    /// <see cref="TimeSpan.Zero"/>, so no timer is ever armed and the poll costs no real time;
    /// what the settle loop measures is entirely the observation costs written above.
    /// </remarks>
    private sealed class VirtualClock : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

        private DateTimeOffset _now = Epoch;

        internal TimeSpan Elapsed => _now - Epoch;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>
    /// The real reader, wrapped in a script that states what each observation costs and what
    /// happened to the file just before it.
    /// </summary>
    private sealed class ScriptedInspector(VirtualClock clock, string outputPath) : IFileInspector
    {
        private readonly WicFileInspector _real = new();
        private readonly Queue<(TimeSpan Cost, byte[]? Writes)> _script = new();

        internal int Observations { get; private set; }

        internal List<string> Timeline { get; } = [];

        internal void Observe(TimeSpan cost, byte[]? writes = null) => _script.Enqueue((cost, writes));

        public async Task<OperationResult<FileFacts>> InspectAsync(
            string absolutePath, CancellationToken cancellationToken)
        {
            (TimeSpan cost, byte[]? writes) = _script.Count > 0 ? _script.Dequeue() : (TimeSpan.Zero, null);
            if (writes is not null)
            {
                File.WriteAllBytes(outputPath, writes);
            }

            clock.Advance(cost);
            Observations++;
            var observed = await _real.InspectAsync(absolutePath, cancellationToken).ConfigureAwait(false);
            Timeline.Add(
                $"#{Observations} at {clock.Elapsed.TotalMilliseconds:0} ms: " +
                (observed.IsSuccess
                    ? $"{observed.Value.ByteLength} bytes, {observed.Value.Format}, {observed.Value.Sha256.ToString()[..8]}"
                    : observed.Failure.Code.ToString()));
            return observed;
        }
    }

    /// <summary>One PSD preparation with every seam under this file's control.</summary>
    private sealed class SettleScenario : IDisposable
    {
        private readonly TempWorkspace _temp = new();
        private readonly VirtualClock _clock = new();
        private readonly PhotoshopAutomationOptions _options;
        private readonly string _input;
        private readonly PhotoshopBaseline _baseline;
        private readonly FakeWindowLocator _locator = new();
        private readonly ScriptedDriver _driver;

        internal SettleScenario(TimeSpan timeout, TimeSpan interval)
        {
            string executable = _temp.CreateSourceFile("Photoshop.exe", [0x4D, 0x5A, 0x90, 0]);
            var version = FileVersionInfo.GetVersionInfo(executable);
            _baseline = PhotoshopFakes.Baseline() with
            {
                ExecutablePath = executable,
                ExecutableSha256 = Sha256.FromBytes(SHA256.HashData(File.ReadAllBytes(executable))),
                AcceptedProductVersion = version.ProductVersion ?? "(unreadable)",
                AcceptedFileVersion = version.FileVersion ?? "(unreadable)",
            };
            _input = _temp.CreateSourceFile("input.psd", PsdInputPreparationTests.RgbCompositePsd());
            _driver = new ScriptedDriver(_input);
            _locator.Register(new ExternalProcessRef(7777, executable, DateTimeOffset.UnixEpoch), PhotoshopFakes.Window());
            _options = new PhotoshopAutomationOptions
            {
                PollInterval = interval,
                TiffSettleTimeout = timeout,
                AttachTimeout = TimeSpan.FromMilliseconds(20),
                OpenConfirmationTimeout = TimeSpan.FromMilliseconds(20),
            };
            OutputPath = Path.Combine(_temp.Root, "prepared.png");
            Inspector = new ScriptedInspector(_clock, OutputPath);
        }

        internal ScriptedInspector Inspector { get; }

        internal string OutputPath { get; }

        internal TimeSpan Elapsed => _clock.Elapsed;

        internal int NativeCalls { get; private set; }

        internal int OpenCount => _driver.OpenCount;

        /// <summary>Runs the whole PSD path with <paramref name="output"/> as what Photoshop wrote.</summary>
        internal async Task<OperationResult<AdapterOutput>> PrepareAsync(byte[]? output)
        {
            ScriptedNative native = new(output, () => NativeCalls++);
            var adapter = new ProductionPhotoshopOutputProcessor(
                new StubPhotoshopBaselineProvider(_baseline), _locator, _driver,
                new StubPhotoshopWorkspace(_temp.Root), _options, _clock)
            {
                PsdNative = native,
                PsdOutputInspector = Inspector,
            };
            return await adapter.PreparePsdAsync(
                new PsdPreparationRequest(
                    WorkspaceFileRef.Create("Sessions/S/Working/A/input.psd", WorkspaceArea.Working),
                    WorkspaceFileRef.Create("Sessions/S/Working/A/prepared.png", WorkspaceArea.Working)),
                CancellationToken.None);
        }

        /// <summary>The observation timeline, for a failure message that explains itself.</summary>
        internal string Explain(OperationResult<AdapterOutput> result) =>
            (result.IsFailure ? result.Failure.ToString() : "") +
            Environment.NewLine + string.Join(Environment.NewLine, Inspector.Timeline);

        public void Dispose() => _temp.Dispose();
    }

    private sealed class ScriptedNative(byte[]? output, Action counted) : IPhotoshopPsdNativeBridge
    {
        public OperationResult<PsdNativeOutcome> ExportOnce(PsdNativeCommand command, PhotoshopBaseline baseline)
        {
            counted();
            if (output is not null)
            {
                File.WriteAllBytes(command.OutputPath, output);
            }

            return OperationResult.Ok(new PsdNativeOutcome(
                true, new PsdInspection(4, 3, "RGB", 8, true, true, [], "native-test-double"), "Prepared."));
        }
    }

    private sealed class ScriptedDriver(string input) : IPhotoshopUiDriver
    {
        private bool _open;

        internal int OpenCount { get; private set; }

        public Task<OperationResult<PhotoshopStateSnapshot>> InspectStateAsync(
            PhotoshopTarget target, string? name, CancellationToken token)
        {
            var state = _open
                ? PhotoshopStartingState.KnownEditorWithOtherDocument
                : PhotoshopStartingState.KnownStartScreen;
            string title = _open ? PhotoshopFakes.TitleFor(Path.GetFileName(input)) : PhotoshopFakes.NoDocumentTitle;
            return Task.FromResult(OperationResult.Ok(
                new PhotoshopStateSnapshot(state, [], new PhotoshopObservation(title, [], [], true, name))));
        }

        public Task<OperationResult<PhotoshopTarget>> ActivateAsync(PhotoshopTarget target, CancellationToken token) =>
            Task.FromResult(OperationResult.Ok(target));

        public Task<OperationResult<PhotoshopTarget>> OpenManagedDocumentAsync(
            PhotoshopTarget target, string path, CancellationToken token)
        {
            _open = true;
            OpenCount++;
            return Task.FromResult(OperationResult.Ok(target));
        }

        public Task<OperationResult<PhotoshopDocumentIdentity>> ProbeDocumentIdentityAsync(
            PhotoshopTarget target, CancellationToken token) =>
            Task.FromResult(OperationResult.Ok(new PhotoshopDocumentIdentity(
                Path.GetFileName(input), Path.GetDirectoryName(input)!, input,
                PhotoshopFakes.TitleFor(Path.GetFileName(input)))));

        public Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
            PhotoshopTarget target, string path, CancellationToken token)
        {
            _open = false;
            return Task.FromResult(OperationResult.Ok(target));
        }

        public OperationResult<EvidenceRef> CaptureEvidence(PhotoshopTarget target, string reason) =>
            OperationResult.Fail<EvidenceRef>(FailureCode.WorkspaceError, "No screenshot in test.");
    }
}
