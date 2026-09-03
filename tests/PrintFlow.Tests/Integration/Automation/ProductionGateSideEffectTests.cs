using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using FileWorkspace = PrintFlow.Infrastructure.Workspace.FileWorkspace;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// What a refused gate costs when the Production adapters are genuinely composed
/// (Epic 11500 Part D §6, §12).
/// </summary>
/// <remarks>
/// Part B proved the gate refuses. This proves the refusal happens <i>before</i> anything
/// external can occur — which is a different claim, and one that only becomes checkable now that
/// <c>Adapters.Mode = Production</c> composes adapters that could really launch Photoshop and
/// Meitu. The fact that they exist in the container must never imply they may run.
/// <para>
/// Two levels of proof, because they answer different objections. The first drives the real
/// composed application and observes the world: no process appeared, no file was written, no
/// attempt row exists, the automation lock was never taken. The second replaces the operating
/// system underneath the very same production adapters with recording seams, so "no keystroke
/// was sent" and "the Action was invoked zero times" are counted rather than inferred.
/// </para>
/// <para>
/// The live workstation is never intentionally broken to prove a refusal. Every failing
/// workstation below is <see cref="WorkstationVerificationFixture"/>'s synthetic one — a machine
/// that exists nowhere (§12).
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ProductionGateSideEffectTests
{
    // ---------------------------------------------------------------- §6

    /// <summary>
    /// Production configured, gate refusing: the workflow stops and the world is unchanged
    /// (§6).
    /// </summary>
    /// <remarks>
    /// The real <see cref="ServiceRegistration"/>, the real
    /// <see cref="VerifiedEnvironmentGate"/>, the real production Meitu adapter, and a synthetic
    /// layout that cannot pass verification. Everything asserted afterwards is something that
    /// would exist if the adapter had been reached: an attempt row, a held automation lock, a
    /// revision, an evidence capture, a running application.
    /// </remarks>
    [Fact]
    public async Task A_refused_gate_stops_the_meitu_step_before_anything_external_happens()
    {
        using TempApplication application = new("Production");
        using ServiceProvider services = Compose(application);

        services.GetRequiredService<IMeituProcessor>().ShouldBeOfType<ProductionMeituProcessor>();

        ISessionService service = services.GetRequiredService<ISessionService>();
        ISessionRepository repository = services.GetRequiredService<ISessionRepository>();
        ExternalWorld before = ExternalWorld.Observe(application.WorkspaceRoot);

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, WriteSource(application), "art", "tester",
            CancellationToken.None)).Value.Id;
        await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue();
        started.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);

        SessionAggregate aggregate = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        aggregate.Attempts.ShouldNotContain(a => a.Step == StepKind.Enhancement);
        aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Enhance);
        (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();

        before.AssertUnchanged(application.WorkspaceRoot);
    }

    /// <summary>
    /// The same, for the step that would drive Photoshop and its Action (§6).
    /// </summary>
    /// <remarks>
    /// Worth its own case rather than folding into the Meitu one: the Photoshop step is the
    /// expensive half of Production — it opens a document, resizes it, runs the canonical Action
    /// and writes a TIFF — and it is reached through a different workflow with its own
    /// preconditions. A gate that happened to be consulted for Meitu and not here would be a
    /// hole the Meitu test could not see.
    /// </remarks>
    [Fact]
    public async Task A_refused_gate_stops_the_photoshop_step_before_anything_external_happens()
    {
        using TempApplication application = new("Production");
        using ServiceProvider services = Compose(application);

        services.GetRequiredService<IPhotoshopOutputProcessor>()
            .ShouldBeOfType<ProductionPhotoshopOutputProcessor>();

        ISessionService service = services.GetRequiredService<ISessionService>();
        ISessionRepository repository = services.GetRequiredService<ISessionRepository>();
        ExternalWorld before = ExternalWorld.Observe(application.WorkspaceRoot);

        SessionId id = (await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, WriteSource(application), "tiff-out", "tester",
            CancellationToken.None)).Value.Id;
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("finished design"), "tester", CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SetPrintDimensions(
                PrintDimensions.FromMillimetres(200, 150, SizePreset.Custom)),
            "tester",
            CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "tester",
            CancellationToken.None));

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue();
        started.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);

        SessionAggregate aggregate = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        aggregate.Attempts.ShouldNotContain(a => a.Step == StepKind.PhotoshopOutput);
        aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.PhotoshopOutput);
        aggregate.Outputs.ShouldBeEmpty();
        (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();

        before.AssertUnchanged(application.WorkspaceRoot);
    }

    /// <summary>
    /// Counted at the operating-system seams: a refused Meitu step sends nothing (§6).
    /// </summary>
    /// <remarks>
    /// The production adapter is the real one; only the four seams through which it can reach the
    /// machine are recorders. So every claim §6 makes is a number here rather than an inference:
    /// launches, keystrokes, UI invocations and evidence captures are all zero, and the adapter
    /// itself was never even asked.
    /// </remarks>
    [Fact]
    public async Task A_refused_gate_sends_no_input_and_launches_nothing_through_the_meitu_seams()
    {
        using SessionServiceHarness harness = new();
        using TempWorkspace adapterWorkspace = new();

        MeituSpy spy = MeituSpy.Create(adapterWorkspace);
        CountingMeitu counted = new(spy.Adapter);

        ISessionService service = ServiceWith(harness, counted, RefusingGate());
        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(), "art", "tester",
            CancellationToken.None)).Value.Id;
        await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue();
        started.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);

        counted.Calls.ShouldBe(0, "the adapter was never asked.");
        spy.AssertUntouched();
    }

    /// <summary>
    /// Counted at the operating-system seams: a refused Photoshop step invokes no Action (§6).
    /// </summary>
    /// <remarks>
    /// The Action bridge is the seam that matters most in this test. It is the only path by which
    /// PrintFlow can make Photoshop change a document, and a count of zero on it is the literal
    /// form of "no Photoshop Action is invoked" — asserted through the same closed bridge the
    /// accepted adapter uses, rather than through a stand-in that could never have invoked one.
    /// </remarks>
    [Fact]
    public async Task A_refused_gate_invokes_no_photoshop_action_through_the_photoshop_seams()
    {
        using SessionServiceHarness harness = new();
        using TempWorkspace adapterWorkspace = new();

        PhotoshopSpy spy = PhotoshopSpy.Create(adapterWorkspace);
        CountingPhotoshop counted = new(spy.Adapter);

        ISessionService service = harness.CreateServiceWithPhotoshop(counted, RefusingGate());
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue();
        started.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);

        counted.Calls.ShouldBe(0, "the adapter was never asked.");
        spy.AssertUntouched();
    }

    // ---------------------------------------------------------------- §4.6, §16 Gate

    /// <summary>
    /// A verified workstation lets the workflow reach the Production adapter (§4.6).
    /// </summary>
    /// <remarks>
    /// The other half of the boundary, and the half that would otherwise be proved only by a
    /// smoke nobody can run twice: on a workstation that passes every blocking check the gate
    /// gets out of the way, and the adapter is genuinely entered. It then fails, because the
    /// synthetic seams are not a real Meitu — and the failure code is the point. Anything but
    /// <see cref="FailureCode.EnvironmentNotVerified"/> means the refusal came from the adapter
    /// doing its own work, not from the gate standing in front of it.
    /// </remarks>
    [Fact]
    public async Task A_verified_workstation_lets_the_workflow_reach_the_production_adapter()
    {
        using SessionServiceHarness harness = new();
        using TempWorkspace adapterWorkspace = new();
        using WorkstationVerificationFixture workstation = new();
        workstation.MarkEvidenceReadOnly();

        VerifiedEnvironmentGate gate = new(workstation.CreateVerifier());
        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue(
            "the synthetic workstation is arranged to pass every blocking check.");

        CountingMeitu counted = new(MeituSpy.Create(adapterWorkspace).Adapter);

        ISessionService service = ServiceWith(harness, counted, gate);
        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(), "art", "tester",
            CancellationToken.None)).Value.Id;
        await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        counted.Calls.ShouldBe(1, "a verified workstation must let the adapter be asked.");
        started.Failure.Code.ShouldNotBe(FailureCode.EnvironmentNotVerified,
            "whatever went wrong, it was not the gate.");
    }

    /// <summary>
    /// A dynamic fact that breaks and is restored reopens Production without a restart (§16 Gate).
    /// </summary>
    /// <remarks>
    /// Part B established this at the gate. Repeated here through the workflow, because that is
    /// where it matters to an operator: lock the screen mid-job and the next step refuses; unlock
    /// it and the next step is asked again. No restart, and no cached yes in between.
    /// </remarks>
    [Fact]
    public async Task A_dynamic_failure_that_is_repaired_reopens_production_without_a_restart()
    {
        using SessionServiceHarness harness = new();
        using TempWorkspace adapterWorkspace = new();
        using WorkstationVerificationFixture workstation = new();
        workstation.MarkEvidenceReadOnly();

        VerifiedEnvironmentGate gate = new(workstation.CreateVerifier());
        CountingMeitu counted = new(MeituSpy.Create(adapterWorkspace).Adapter);
        ISessionService service = ServiceWith(harness, counted, gate);

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(), "art", "tester",
            CancellationToken.None)).Value.Id;
        await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);

        InteractiveSessionFacts unlocked = workstation.Facts.Session;
        workstation.Facts.Session = unlocked with { InputDesktopName = "Winlogon" };

        OperationResult<SessionView> locked = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        locked.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        counted.Calls.ShouldBe(0);

        workstation.Facts.Session = unlocked;

        await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        counted.Calls.ShouldBe(1, "the same process must recover without being restarted.");
    }

    // ---------------------------------------------------------------- §12

    /// <summary>
    /// The refusal matrix: every named condition closes Production with no side effect (§12).
    /// </summary>
    /// <remarks>
    /// One case per condition §12 names, each breaking exactly one thing on an otherwise accepted
    /// synthetic workstation, and each asserting the same three facts: the workflow refuses, the
    /// adapter is never asked, and every external seam count is zero. What it rules out is a
    /// condition that refuses <i>after</i> the adapter has already put something on screen.
    /// </remarks>
    [Theory]
    [InlineData(RefusalCase.DisplayMismatch)]
    [InlineData(RefusalCase.NonInteractiveSession)]
    [InlineData(RefusalCase.UnsupportedRemoteSession)]
    [InlineData(RefusalCase.SecureDesktop)]
    [InlineData(RefusalCase.WrongMeituExecutableDigest)]
    [InlineData(RefusalCase.WrongPhotoshopExecutableDigest)]
    [InlineData(RefusalCase.WrongActionDigest)]
    [InlineData(RefusalCase.WrongWorkspaceRoot)]
    [InlineData(RefusalCase.InvalidPreset)]
    [InlineData(RefusalCase.InvalidEvidenceChain)]
    public async Task Every_refusal_condition_closes_production_with_no_external_side_effect(
        RefusalCase scenario)
    {
        using SessionServiceHarness harness = new();
        using TempWorkspace adapterWorkspace = new();
        using WorkstationVerificationFixture workstation = new();
        workstation.MarkEvidenceReadOnly();

        VerifiedEnvironmentGate gate = new(
            workstation.CreateVerifier(WorkspaceRootFor(scenario, workstation)));
        Break(scenario, workstation);

        MeituSpy spy = MeituSpy.Create(adapterWorkspace);
        CountingMeitu counted = new(spy.Adapter);
        ISessionService service = ServiceWith(harness, counted, gate);

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(), "art", "tester",
            CancellationToken.None)).Value.Id;
        await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue($"{scenario} must close Production.");
        started.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        counted.Calls.ShouldBe(0, $"{scenario} must refuse before the adapter is asked.");
        spy.AssertUntouched();
    }

    /// <summary>The conditions §12 requires proving synthetically.</summary>
    public enum RefusalCase
    {
        DisplayMismatch,
        NonInteractiveSession,
        UnsupportedRemoteSession,
        SecureDesktop,
        WrongMeituExecutableDigest,
        WrongPhotoshopExecutableDigest,
        WrongActionDigest,
        WrongWorkspaceRoot,
        InvalidPreset,
        InvalidEvidenceChain,
    }

    // -----------------------------------------------------------------------------------
    // The synthetic workstation
    // -----------------------------------------------------------------------------------

    private static void Break(RefusalCase scenario, WorkstationVerificationFixture fixture)
    {
        switch (scenario)
        {
            case RefusalCase.DisplayMismatch:
                fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
                break;

            case RefusalCase.NonInteractiveSession:
                fixture.Facts.Session = new InteractiveSessionFacts(false, 0, "Services", false, null);
                break;

            case RefusalCase.UnsupportedRemoteSession:
                fixture.Facts.Session = fixture.Facts.Session with
                {
                    SessionId = 2,
                    SessionName = "RDP-Tcp#7",
                    IsRemoteSession = true,
                };
                break;

            case RefusalCase.SecureDesktop:
                fixture.Facts.Session = fixture.Facts.Session with { InputDesktopName = "Winlogon" };
                break;

            case RefusalCase.WrongMeituExecutableDigest:
                WorkstationVerificationFixture.Corrupt(fixture.MeituPath);
                break;

            case RefusalCase.WrongPhotoshopExecutableDigest:
                WorkstationVerificationFixture.Corrupt(fixture.PhotoshopPath);
                break;

            case RefusalCase.WrongActionDigest:
                WorkstationVerificationFixture.Corrupt(fixture.ActionPath);
                break;

            case RefusalCase.WrongWorkspaceRoot:
                // Already broken by the root the verifier above was built with.
                break;

            case RefusalCase.InvalidPreset:
                WorkstationVerificationFixture.Corrupt(fixture.ManifestPath);
                break;

            case RefusalCase.InvalidEvidenceChain:
                // The attribute is cleared first because the fixture applied it: the immutability
                // policy is an advisory, and what this case is about is the digest that is not.
                WorkstationVerificationFixture.ClearReadOnlyAttribute(fixture.EvidencePaths[0]);
                WorkstationVerificationFixture.Corrupt(fixture.EvidencePaths[0]);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unhandled case.");
        }
    }

    private static string? WorkspaceRootFor(
        RefusalCase scenario, WorkstationVerificationFixture fixture) =>
        scenario == RefusalCase.WrongWorkspaceRoot
            ? Path.Combine(fixture.WorkspaceRoot, "not-the-accepted-root")
            : null;

    // -----------------------------------------------------------------------------------
    // Spies over the external seams
    // -----------------------------------------------------------------------------------

    /// <summary>The real production Meitu adapter over an operating system that only records.</summary>
    private sealed record MeituSpy(
        ProductionMeituProcessor Adapter,
        FakeWindowLocator Locator,
        RecordingUiElementProvider Elements,
        RecordingInputSink Input,
        RecordingEvidenceSink Evidence)
    {
        public static MeituSpy Create(TempWorkspace workspace)
        {
            StubMeituBaselineProvider baselines = new(MeituFakes.Baseline());
            FakeWindowLocator locator = new();
            RecordingUiElementProvider elements = new();
            RecordingInputSink input = new();
            RecordingEvidenceSink evidence = new();
            MeituAutomationOptions options = new() { PollInterval = TimeSpan.FromMilliseconds(5) };

            GuardedMeituUiDriver driver = new(
                locator, elements, input, evidence, baselines, options, TimeProvider.System);

            ProductionMeituProcessor adapter = new(
                baselines, locator, driver, new FileWorkspace(workspace.Root), new WicFileInspector(),
                new WicMeituTransparencyInspector(), new FileSystemMeituOutputProbe(),
                options, TimeProvider.System);

            return new MeituSpy(adapter, locator, elements, input, evidence);
        }

        public void AssertUntouched()
        {
            Locator.LaunchCount.ShouldBe(0, "no application may be launched.");
            Input.Sends.ShouldBeEmpty("no keyboard or mouse input may be sent.");
            Elements.Invocations.ShouldBeEmpty("no UI element may be invoked.");
            Elements.ValueWrites.ShouldBeEmpty("nothing may be typed into a control.");
            Evidence.Captures.ShouldBeEmpty("no screen may be captured.");
        }
    }

    /// <summary>The real production Photoshop adapter over an operating system that only records.</summary>
    private sealed record PhotoshopSpy(
        ProductionPhotoshopOutputProcessor Adapter,
        FakeWindowLocator Locator,
        FakeVerifiedControlSink Controls,
        RecordingInputSink Input,
        RecordingEvidenceSink Evidence,
        SpyPreparationBridge Preparation,
        SpyW1Bridge W1,
        SpyTiffBridge Tiff)
    {
        public static PhotoshopSpy Create(TempWorkspace workspace)
        {
            StubPhotoshopBaselineProvider baselines = new(PhotoshopFakes.Baseline());
            FakeWindowLocator locator = new();
            FakeVerifiedControlSink controls = new();
            RecordingInputSink input = new();
            RecordingEvidenceSink evidence = new();
            PhotoshopAutomationOptions options = new() { PollInterval = TimeSpan.Zero };

            GuardedPhotoshopUiDriver driver = new(
                locator, controls, input, evidence, baselines, options, TimeProvider.System);

            SpyPreparationBridge preparation = new();
            SpyW1Bridge w1 = new();
            SpyTiffBridge tiff = new();

            ProductionPhotoshopOutputProcessor adapter = new(
                baselines, locator, driver, new FileWorkspace(workspace.Root), options,
                TimeProvider.System, preparation, w1, tiff,
                new ProductionTiffInspector(), new FileSystemPhotoshopTiffFileProbe());

            return new PhotoshopSpy(
                adapter, locator, controls, input, evidence, preparation, w1, tiff);
        }

        public void AssertUntouched()
        {
            Locator.LaunchCount.ShouldBe(0, "no application may be launched.");
            Input.Sends.ShouldBeEmpty("no keyboard or mouse input may be sent.");
            Controls.Presses.ShouldBeEmpty("no control may be pressed.");
            Controls.Writes.ShouldBeEmpty("nothing may be typed into a control.");
            Evidence.Captures.ShouldBeEmpty("no screen may be captured.");
            Preparation.Calls.ShouldBe(0, "no document may be resized.");
            W1.Calls.ShouldBe(0, "the canonical Action may not be invoked.");
            Tiff.Calls.ShouldBe(0, "no production output file may be saved.");
        }
    }

    private sealed class SpyPreparationBridge : IPhotoshopPreparationNativeBridge
    {
        public int Calls { get; private set; }

        public OperationResult<PhotoshopNativePreparationOutcome> ApplyOnce(
            PhotoshopNativePreparationCommand command, PhotoshopBaseline baseline)
        {
            Calls++;
            throw new InvalidOperationException("A refused gate must never reach Photoshop.");
        }
    }

    private sealed class SpyW1Bridge : IPhotoshopW1NativeBridge
    {
        public int Calls { get; private set; }

        public OperationResult<PhotoshopNativeW1Outcome> ExecuteOnce(
            PhotoshopNativeW1Command command, PhotoshopBaseline baseline)
        {
            Calls++;
            throw new InvalidOperationException("A refused gate must never invoke the Action.");
        }
    }

    private sealed class SpyTiffBridge : IPhotoshopTiffNativeBridge
    {
        public int Calls { get; private set; }

        public OperationResult<PhotoshopNativeTiffOutcome> SaveOnce(
            PhotoshopNativeTiffCommand command, PhotoshopBaseline baseline)
        {
            Calls++;
            throw new InvalidOperationException("A refused gate must never save a production file.");
        }
    }

    /// <summary>Counts calls that reach the adapter, so "never asked" is a checkable fact.</summary>
    private sealed class CountingMeitu(IMeituProcessor inner) : IMeituProcessor
    {
        public int Calls { get; private set; }

        public string AdapterId => inner.AdapterId;

        public AdapterExecutionMode Mode => inner.Mode;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return inner.ProcessAsync(request, cancellationToken);
        }
    }

    private sealed class CountingPhotoshop(IPhotoshopOutputProcessor inner) : IPhotoshopOutputProcessor
    {
        public int Calls { get; private set; }

        public string AdapterId => inner.AdapterId;

        public AdapterExecutionMode Mode => inner.Mode;

        public Task<OperationResult<AdapterOutput>> GenerateAsync(
            PhotoshopRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return inner.GenerateAsync(request, cancellationToken);
        }
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    /// <summary>What the world looked like before the refused step, and what it must still be.</summary>
    private sealed record ExternalWorld(int Processes, bool EvidenceDirectoryExists)
    {
        public static ExternalWorld Observe(string workspaceRoot) =>
            new(ExternalApplicationProbe.RunningCount(),
                Directory.Exists(Path.Combine(workspaceRoot, "Evidence")));

        public void AssertUnchanged(string workspaceRoot)
        {
            ExternalApplicationProbe.RunningCount().ShouldBe(Processes,
                "a refused step must launch no external application.");
            Directory.Exists(Path.Combine(workspaceRoot, "Evidence"))
                .ShouldBe(EvidenceDirectoryExists, "a refused step captures no evidence.");
            Directory.EnumerateFiles(workspaceRoot, "*.tif", SearchOption.AllDirectories)
                .ShouldBeEmpty("a refused step produces no production output file.");
        }
    }

    private static IEnvironmentGate RefusingGate() => new UnverifiedEnvironmentGate();

    private static ISessionService ServiceWith(
        SessionServiceHarness harness, IMeituProcessor meitu, IEnvironmentGate gate) =>
        harness.CreateServiceWithMeitu(meitu, environmentGate: gate);

    private static async Task<SessionId> ReadyForPhotoshopAsync(
        SessionServiceHarness harness, ISessionService service)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, harness.WriteBorderedSourcePng(), "tiff-out", "tester",
            CancellationToken.None)).Value.Id;

        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("finished design"), "tester", CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SetPrintDimensions(
                PrintDimensions.FromMillimetres(200, 150, SizePreset.Custom)),
            "tester",
            CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "tester",
            CancellationToken.None));

        return id;
    }

    private static string WriteSource(TempApplication application)
    {
        string path = Path.Combine(application.WorkspaceRoot, "incoming.png");
        File.WriteAllBytes(path, SyntheticImages.PngWithAlpha(
            12, 10, (x, y) => x is >= 3 and <= 7 && y is >= 2 and <= 6 ? (byte)255 : (byte)0));
        return path;
    }

    private static SessionView Accept(OperationResult<SessionView> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private static ServiceProvider Compose(TempApplication application)
    {
        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(application.ConfigurationFilePath);

        SqliteConnectionFactory factory = new(application.DatabasePath);
        using (SqliteConnection connection = factory.Open())
        {
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        }

        return ServiceRegistration.BuildServiceProvider(
            configuration, application.WorkspaceRoot, factory);
    }
}
