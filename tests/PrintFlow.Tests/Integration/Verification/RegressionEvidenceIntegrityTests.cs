using System.Collections.Immutable;
using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Regression;

namespace PrintFlow.Tests.Integration.Verification;

/// <summary>
/// The regression evidence chain, driven at the boundaries an operator actually uses
/// (PF-AUDIT-R1, audit findings F3 and F4).
/// </summary>
/// <remarks>
/// <b>What these tests are about.</b> One question, asked at each of the three places the answer
/// used to be assumed: <i>is this evidence about this thing?</i> The regression wrapper asks it of
/// a result.json before reporting a pass; the revalidation writer asks it of a run before
/// publishing an attestation; the Product evaluator asks it of a record before opening Production.
/// Before this slice, none of the three asked.
/// <para>
/// <b>Why they run the real scripts.</b> Both defects live in operator tooling — a shell exit-code
/// decision and a writer's choice of which fields to read. A test of a helper the script calls
/// would pass while the script still did the wrong thing, so these start
/// <c>powershell.exe</c> against the scripts in <c>tools\</c> and assert on their exit codes and on
/// what they did or did not write.
/// </para>
/// <para>
/// <b>What they do not do.</b> No Meitu, Photoshop or Maintop, no live readiness, no real
/// regression run, and nothing outside one temporary directory per test. The stand-in test host is
/// installed through the script's own <c>dotnet</c> resolution order rather than through a switch
/// added for testing, and it cannot manufacture a pass: a wrapper verdict still requires this
/// invocation's own bound result. A synthetic run of this protocol is not, and never becomes, a
/// standard-set acceptance.
/// </para>
/// </remarks>
[Collection(EnvironmentVariableCollection.Name)]
public sealed class RegressionEvidenceIntegrityTests : IDisposable
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string Wrapper =
        Path.Combine(RepositoryRoot, "tools", "regression", "Invoke-PrintFlowStandardRegressionSet.ps1");

    private static readonly string Writer =
        Path.Combine(RepositoryRoot, "tools", "installer", "Set-PrintFlowProductionRevalidation.ps1");

    private readonly WorkstationVerificationFixture _workstation = new();
    private readonly SyntheticRegressionWorkstation _synthetic;

    public RegressionEvidenceIntegrityTests() =>
        _synthetic = new SyntheticRegressionWorkstation(_workstation);

    // ==========================================================================================
    // F4 — an execution's verdict is its own
    // ==========================================================================================

    /// <summary>
    /// F4 as it stood: the audit commit's wrapper reports a previous run's <c>Passed</c> as the
    /// success of an invocation whose host failed.
    /// </summary>
    /// <remarks>
    /// The defect demonstrated at the public boundary rather than described, which is why it is kept
    /// as a test and not only as prose in the report. It can only run where the audit commit is
    /// reachable; a shallow clone, an export or a rewritten history fails it with a stated reason rather
    /// than weakening silently. The repaired behaviour is asserted separately by
    /// <see cref="A_previous_runs_pass_is_not_this_invocations_success"/>, which always runs.
    /// </remarks>
    [Fact]
    public void The_audited_wrapper_reported_a_previous_runs_pass_as_this_invocations_success()
    {
        const string runId = "20260910-090000";
        _synthetic.WriteResult(_synthetic.PassingRun(runId, Guid.NewGuid().ToString()));

        string audited = AuditedScript("tools/regression/Invoke-PrintFlowStandardRegressionSet.ps1")
            .ShouldNotBeNull(
                "The audit commit 3f83863 must be reachable from this checkout for the historical half " +
                "of this counterexample to run. A shallow clone, an export or a rewritten history " +
                "cannot run it; the repaired behaviour is asserted separately and does not need git.");

        ScriptOutcome before = _synthetic.Run(
            audited,
            $"-SetRoot \"{_synthetic.SetRoot}\" -RunId {runId}",
            SyntheticRegressionWorkstation.HostThatFails(7));

        before.ExitCode.ShouldBe(0,
            "The audited wrapper reports the previous run's Passed as this invocation's success. If " +
            "this no longer reproduces, the counterexample has changed and the repair needs " +
            "re-deriving, not re-baselining.");
    }

    /// <summary>A previous run's pass is not this invocation's success.</summary>
    /// <remarks>
    /// The same state the audited wrapper exited 0 on: a run folder holding a genuine passing result,
    /// and a test host that exits nonzero without writing anything.
    /// </remarks>
    [Fact]
    public void A_previous_runs_pass_is_not_this_invocations_success()
    {
        const string runId = "20260910-090000";
        _synthetic.WriteResult(_synthetic.PassingRun(runId, Guid.NewGuid().ToString()));

        ScriptOutcome after = _synthetic.Run(
            Wrapper,
            $"-SetRoot \"{_synthetic.SetRoot}\" -RunId {runId}",
            SyntheticRegressionWorkstation.HostThatFails(7));

        after.ExitCode.ShouldBe(4, after.Output);
        after.Says("already has a destination").ShouldBeTrue();

        // Refused, and nothing cleared to make room: the evidence that this identity was used is
        // the whole content of the refusal.
        File.Exists(Path.Combine(_synthetic.RunFolder(runId), "result.json")).ShouldBeTrue();
    }

    /// <summary>
    /// A host failure is not overruled by output, however complete and however recent that output
    /// looks.
    /// </summary>
    /// <remarks>
    /// The destination is fresh, so the reused-identity rule is not what stops this: the host
    /// writes a full, passing, correctly bound result for this very invocation and then exits
    /// nonzero. A run whose host did not complete produced no evidence, and the file it left says
    /// nothing about that.
    /// </remarks>
    [Fact]
    public void A_result_cannot_overrule_a_host_that_failed()
    {
        const string runId = "20260910-091500";

        ScriptOutcome outcome = _synthetic.Run(
            Wrapper,
            $"-SetRoot \"{_synthetic.SetRoot}\" -RunId {runId} -DiagnosticUnbound",
            SyntheticRegressionWorkstation.HostThatWrites(
                _synthetic.PassingRun(runId, "@@INVOCATION@@").ToJson(), exitCode: 7));

        outcome.ExitCode.ShouldBe(7);
        outcome.Says("THE RUN DID NOT COMPLETE").ShouldBeTrue();
    }

    /// <summary>A result produced by some other invocation is not this one's outcome.</summary>
    [Fact]
    public void A_result_from_another_invocation_is_not_this_ones_outcome()
    {
        const string runId = "20260910-093000";

        ScriptOutcome outcome = _synthetic.Run(
            Wrapper,
            $"-SetRoot \"{_synthetic.SetRoot}\" -RunId {runId} -DiagnosticUnbound",
            SyntheticRegressionWorkstation.HostThatWrites(
                _synthetic.PassingRun(runId, "11111111-2222-3333-4444-555555555555").ToJson()));

        outcome.ExitCode.ShouldNotBe(0);
        outcome.Says("PRODUCED NO RESULT OF ITS OWN").ShouldBeTrue();
    }

    /// <summary>
    /// The legitimate path still works: a host that completes and writes this invocation's own
    /// complete result is reported as the pass it is.
    /// </summary>
    /// <remarks>
    /// The counterweight to every refusal above. A protocol that only ever refuses is not a fix,
    /// and the wrapper's own instruction to record the revalidation is part of what has to keep
    /// working.
    /// </remarks>
    [Fact]
    public void A_matching_execution_reports_the_pass_it_produced()
    {
        const string runId = "20260910-094500";

        ScriptOutcome outcome = _synthetic.Run(
            Wrapper,
            $"-SetRoot \"{_synthetic.SetRoot}\" -RunId {runId} " +
            $"-CandidateInstallFolder \"{_synthetic.InstallFolder}\" " +
            $"-BuildPairReceipt \"{_synthetic.BuildPairReceiptPath}\"",
            SyntheticRegressionWorkstation.HostThatWrites(
                _synthetic.PassingRun(runId, "@@INVOCATION@@").ToJson()));

        outcome.ExitCode.ShouldBe(0, outcome.Output);

        // The publish instruction, not just "The set passed" - the wrapper has two exit-0 branches
        // containing that phrase, and the other one is the unbound-candidate warning. Asserting the
        // phrase alone would keep passing if candidate binding silently broke.
        outcome.Says("Record the revalidation with").ShouldBeTrue(outcome.Output);
    }

    /// <summary>Two invocations cannot hold one run identity, sequentially or at the same moment.</summary>
    /// <remarks>
    /// The claim is a single <c>CreateNew</c>, so the file system decides the race rather than a
    /// check-then-create both claimants could pass. The concurrent half is the case that rationale
    /// exists for, so it is the case asserted: many claimants start together and exactly one leaves
    /// with the claim.
    /// </remarks>
    [Fact]
    public void One_run_identity_admits_one_invocation()
    {
        string sequential = _synthetic.RunFolder("20260910-095959");

        RegressionExecutionClaim first = RegressionExecutionClaim.Stake(
            sequential, "20260910-095959", "first-invocation");

        Should.Throw<RegressionExecutionClaimRefusedException>(() =>
                RegressionExecutionClaim.Stake(sequential, "20260910-095959", "second-invocation"))
            .Message.ShouldContain("first-invocation");

        RegressionExecutionClaim.Read(sequential)!.InvocationId.ShouldBe(first.InvocationId);

        // And concurrently. Eight claimants released together on one identity; the file system
        // arbitrates, so seven of them must be refused and the survivor must be the one whose id the
        // claim on disk names.
        const string contested = "20260910-100000";
        string folder = _synthetic.RunFolder(contested);
        using Barrier gate = new(8);

        RegressionExecutionClaim?[] outcomes = [.. Enumerable.Range(0, 8)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(i =>
            {
                gate.SignalAndWait();
                try
                {
                    return RegressionExecutionClaim.Stake(folder, contested, $"invocation-{i}");
                }
                catch (RegressionExecutionClaimRefusedException)
                {
                    return null;
                }
            })];

        RegressionExecutionClaim winner = outcomes.Where(c => c is not null).ShouldHaveSingleItem()!;
        RegressionExecutionClaim.Read(folder)!.InvocationId.ShouldBe(winner.InvocationId);
    }

    /// <summary>
    /// A candidate built from different source than the run exercised is not the run's candidate,
    /// even when every digest is readable and the version string agrees.
    /// </summary>
    /// <remarks>
    /// The other half of the identity contract, and the half byte comparison cannot express: an
    /// installation carries a RID self-contained publish and a test host does not, so the two are
    /// bound by build identity. Asserted directly on the comparison the run uses, because the run's
    /// own use of it needs a real installation to exercise.
    /// </remarks>
    [Fact]
    public void A_candidate_from_different_source_is_not_the_tested_candidate()
    {
        ImmutableArray<ProductAssemblyIdentity> harness = ProductBuildIdentity.Running();

        ProductBuildIdentity.CompareBuildIdentity(harness, harness).ShouldBeEmpty();

        // Same version, same shape, a different source revision — which is what installing a payload
        // built from another commit looks like.
        ImmutableArray<ProductAssemblyIdentity> elsewhere =
        [
            .. harness.Select(a => a.Name == "PrintFlow.Workflow.dll"
                ? a with { BuildIdentity = "0.1.0+0000000000000000000000000000000000000000" }
                : a),
        ];

        ProductBuildIdentity.CompareBuildIdentity(elsewhere, harness)
            .ShouldHaveSingleItem().ShouldContain("PrintFlow.Workflow.dll");

        // An identity that could not be established is not a matching identity, in either direction.
        ProductBuildIdentity.CompareBuildIdentity([], harness).Length.ShouldBe(4);
        ProductBuildIdentity.CompareBuildIdentity(harness, []).Length.ShouldBe(4);
        ProductBuildIdentity.CompareBytes(default, harness).Length.ShouldBe(4);
    }

    /// <summary>
    /// A run that could not bind a candidate says so, and neither the wrapper nor the writer treats
    /// it as publishable.
    /// </summary>
    /// <remarks>
    /// The consumers of <c>CandidateProblems</c>, which is the field a run uses to say "my evidence
    /// stands but no revalidation may follow from it". The wrapper still reports the pass, because
    /// the set did pass; what it does not do is send the operator to a command that would refuse.
    /// </remarks>
    [Fact]
    public void A_run_that_bound_no_candidate_is_not_publishable()
    {
        const string runId = "20260910-101500";

        StandardRegressionSetRunResult unbound = _synthetic.PassingRun(runId, "@@INVOCATION@@");
        unbound = unbound with
        {
            Binding = unbound.Binding! with
            {
                CandidateProblems = ["No installation at 'D:\\nowhere', so this run attests no candidate."],
            },
        };

        ScriptOutcome wrapper = _synthetic.Run(
            Wrapper,
            $"-SetRoot \"{_synthetic.SetRoot}\" -RunId {runId} -DiagnosticUnbound",
            SyntheticRegressionWorkstation.HostThatWrites(unbound.ToJson()));

        wrapper.ExitCode.ShouldBe(0, wrapper.Output);
        wrapper.Says("attests no installed candidate").ShouldBeTrue();
        wrapper.Says("Record the revalidation with").ShouldBeFalse(
            "A run that bound no candidate must not send the operator to a command that will refuse.");

        ScriptOutcome publication = Publish(Path.Combine(_synthetic.RunFolder(runId), "result.json"));
        publication.ExitCode.ShouldBe(3, publication.Output);
        publication.Says("could not bind an installed candidate").ShouldBeTrue();
        File.Exists(_synthetic.RecordPath).ShouldBeFalse();
    }

    /// <summary>
    /// The runner obeys the claim before it does anything else — the entry point, not the helper.
    /// </summary>
    /// <remarks>
    /// Proof that the refusal is wired in and not merely available. The run is enabled, pointed at
    /// a destination another invocation already claimed, and stops there: no configuration is read,
    /// no service graph is composed and no external application is approached, which is exactly why
    /// this test is safe to run anywhere.
    /// </remarks>
    [Fact]
    public void The_runner_refuses_to_start_on_a_claimed_identity()
    {
        const string runId = "20260910-101010";
        RegressionExecutionClaim.Stake(_synthetic.RunFolder(runId), runId, "someone-elses-invocation");

        using EnvironmentVariables environment = new()
        {
            ["PRINTFLOW_STANDARD_REGRESSION_SET"] = "1",
            ["PRINTFLOW_REGRESSION_SET_ROOT"] = _synthetic.SetRoot,
            ["PRINTFLOW_REGRESSION_RUN_ID"] = runId,
            ["PRINTFLOW_REGRESSION_INVOCATION_ID"] = "this-invocation",
        };

        Should.Throw<RegressionExecutionClaimRefusedException>(() =>
                new Smoke.StandardRegressionSetWorkstationSmoke(new NullOutput())
                    .Run_the_standard_local_regression_set_on_the_fixed_workstation())
            .Message.ShouldContain("someone-elses-invocation");
    }

    /// <summary>
    /// Matching informational labels cannot bind a loaded harness to different recorded bytes.
    /// </summary>
    [Fact]
    public void The_runner_refuses_same_label_different_harness_bytes_before_configuration()
    {
        const string runId = "20260910-101011";
        ImmutableArray<ProductAssemblyIdentity> loaded = ProductBuildIdentity.Running();
        (RegressionBuildOrigin origin, ImmutableArray<ProductAssemblyIdentity> differentBytesSameLabel) =
            _synthetic.WriteDifferentSourceHarnessReceiptWithSameLabels();

        ProductAssemblyIdentity loadedInfrastructure = loaded.Single(
            identity => identity.Name == "PrintFlow.Infrastructure.dll");
        ProductAssemblyIdentity recordedInfrastructure = differentBytesSameLabel.Single(
            identity => identity.Name == "PrintFlow.Infrastructure.dll");
        recordedInfrastructure.BuildIdentity.ShouldBe(loadedInfrastructure.BuildIdentity,
            "the counterexample keeps the informational build label unchanged");
        recordedInfrastructure.Sha256.ShouldNotBe(loadedInfrastructure.Sha256,
            "the isolated source build has genuinely different bytes under that same label");

        using EnvironmentVariables environment = new()
        {
            ["PRINTFLOW_STANDARD_REGRESSION_SET"] = "1",
            ["PRINTFLOW_REGRESSION_SET_ROOT"] = _synthetic.SetRoot,
            ["PRINTFLOW_REGRESSION_RUN_ID"] = runId,
            ["PRINTFLOW_REGRESSION_INVOCATION_ID"] = "same-label-different-bytes",
            ["PRINTFLOW_REGRESSION_CANDIDATE_INSTALL_FOLDER"] = _synthetic.InstallFolder,
            ["PRINTFLOW_REGRESSION_BUILD_PAIR_RECEIPT"] = origin.ReceiptPath,
            ["PRINTFLOW_REGRESSION_DIAGNOSTIC_UNBOUND"] = null,
        };

        InvalidDataException refused = Should.Throw<InvalidDataException>(() =>
            new Smoke.StandardRegressionSetWorkstationSmoke(new NullOutput())
                .Run_the_standard_local_regression_set_on_the_fixed_workstation());

        refused.Message.ShouldContain("harness");
        refused.Message.ShouldContain("bytes");
        RegressionExecutionClaim.Read(_synthetic.RunFolder(runId))!.InvocationId
            .ShouldBe("same-label-different-bytes",
                "the permanent claim remains first, while origin refusal precedes configuration and composition");
    }

    // ==========================================================================================
    // Recording a visual review
    // ==========================================================================================

    /// <summary>
    /// A review updates the decisions of the run it reviews and nothing else about it.
    /// </summary>
    /// <remarks>
    /// The execution's identity, its start and — the one this repairs — its completion time are
    /// carried across unchanged, so a run reviewed a week later does not read as one that executed
    /// a week later. The reviewer's own time is recorded as the review's, the decision is bound to
    /// the artefact digest the run recorded, and a decision from a test stays marked synthetic.
    /// No external runner is involved: this path re-derives from evidence already on disk.
    /// </remarks>
    [Fact]
    public void A_review_preserves_the_execution_it_reviews()
    {
        const string runId = "20260910-110000";
        string folder = _synthetic.RunFolder(runId);
        Directory.CreateDirectory(folder);

        string artefact = Path.Combine(folder, "portrait-preview.bin");
        File.WriteAllText(artefact, "synthetic preview bytes");
        string artefactDigest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(artefact)));

        StandardRegressionSetRunResult pending = PendingRun(runId, artefact, artefactDigest);
        _synthetic.WriteResult(pending);

        string decisions = Path.Combine(folder, "decisions.json");
        File.WriteAllText(decisions, """
            {
              "decidedBy": "SYNTHETIC\\protocol-test",
              "decidedAtLocal": "2026-09-17T14:00:00+12:00",
              "synthetic": true,
              "decisions": [
                { "id": "PORTRAIT-VISUAL-001", "outcome": "Passed", "notes": "Recorded by a protocol test." }
              ]
            }
            """);

        using (EnvironmentVariables environment = new()
        {
            ["PRINTFLOW_REGRESSION_REAGGREGATE"] = folder,
            ["PRINTFLOW_REGRESSION_VISUAL_DECISIONS"] = decisions,
        })
        {
            new Smoke.StandardRegressionSetWorkstationSmoke(new NullOutput())
                .Re_derive_a_completed_run_from_recorded_visual_decisions();
        }

        StandardRegressionSetRunResult reviewed = ReadResult(folder);

        reviewed.Status.ShouldBe("Passed");
        reviewed.RunId.ShouldBe(pending.RunId);
        reviewed.Binding!.InvocationId.ShouldBe(pending.Binding!.InvocationId);
        reviewed.StartedAtLocal.ShouldBe(pending.StartedAtLocal);
        reviewed.CompletedAtLocal.ShouldBe(pending.CompletedAtLocal,
            "A review is not an execution: re-stamping the completion time would make an old run " +
            "read as one that had just finished.");
        reviewed.ProductVersion.ShouldBe(pending.ProductVersion);
        reviewed.Binding.CandidateProductAssemblies.ShouldBe(pending.Binding.CandidateProductAssemblies);
        reviewed.Binding.BuildOrigin.ShouldBe(pending.Binding.BuildOrigin,
            "review re-derives a historical result; it must not attach the reviewer's current origin");

        RegressionReviewRecord review = reviewed.Reviews.ShouldHaveSingleItem();
        review.DecidedBy.ShouldBe(@"SYNTHETIC\protocol-test");
        review.DecidedAtLocal.ShouldBe("2026-09-17T14:00:00+12:00");
        review.Synthetic.ShouldBeTrue("A decision made by a test must never read as one made by a person.");
        review.Decisions.ShouldHaveSingleItem().EvidenceSha256.ShouldBe(artefactDigest);
    }

    /// <summary>
    /// Recording a review of an existing run is still possible at the wrapper, and is not treated as
    /// a new execution.
    /// </summary>
    /// <remarks>
    /// The other half of the reused-identity rule, and the one a regression would quietly break. A
    /// review's destination *must* already exist and its result *was* produced by another
    /// invocation, so applying either new-execution rule to it would make reviewing a run
    /// impossible — and reviewing a run is the legitimate way its qualitative checks get concluded.
    /// The wrapper must therefore refuse neither: not exit 4, not exit 5.
    /// </remarks>
    [Fact]
    public void A_review_is_not_a_new_execution()
    {
        const string runId = "20260910-112500";
        _synthetic.WriteResult(_synthetic.PassingRun(runId, "an-earlier-invocation"));

        string decisions = Path.Combine(_synthetic.RunFolder(runId), "decisions.json");
        File.WriteAllText(decisions, """{ "decidedBy": "SYNTHETIC\\protocol-test", "decisions": [] }""");

        ScriptOutcome outcome = _synthetic.Run(
            Wrapper,
            $"-SetRoot \"{_synthetic.SetRoot}\" -RunId {runId} -RecordVisualReview \"{decisions}\"",
            SyntheticRegressionWorkstation.HostThatEchoesArguments());

        outcome.ExitCode.ShouldBe(0, outcome.Output);
        outcome.Says("Recording the visual review").ShouldBeTrue();
        outcome.Says("already has a destination").ShouldBeFalse("A review's destination is meant to exist.");
        outcome.Says("PRODUCED NO RESULT OF ITS OWN").ShouldBeFalse(
            "A review is not a new execution, so it does not owe a new invocation's result.");
        outcome.Says("vstest").ShouldBeTrue(outcome.Output);
        outcome.Says(Path.Combine(_synthetic.HarnessFolder, "PrintFlow.Tests.dll")).ShouldBeTrue(outcome.Output);
        outcome.Says("PrintFlow.Tests.csproj").ShouldBeFalse(
            "review must use the original receipt's no-build harness rather than conventional project output");
    }

    /// <summary>
    /// A review concludes only what the run left open, and a second review does not erase the first.
    /// </summary>
    /// <remarks>
    /// Three requirements in one pass over the same run, because they are three faces of one rule —
    /// a review is an event that happened to a run, not an edit of it. Re-deciding a decided check is
    /// refused; a case the run recorded as Failed stays Failed however its qualitative checks are
    /// then decided; and the earlier review is still in the run's history afterwards.
    /// </remarks>
    [Fact]
    public void A_review_concludes_only_what_is_outstanding_and_keeps_the_earlier_one()
    {
        const string runId = "20260910-113000";
        string folder = _synthetic.RunFolder(runId);
        Directory.CreateDirectory(folder);

        string artefact = Path.Combine(folder, "portrait-preview.bin");
        File.WriteAllText(artefact, "synthetic preview bytes");
        string digest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(artefact)));

        // The case carries a failed assertion as well as an undecided check, so Conclude() derives
        // Failed for it and a decision must not be able to lift that.
        StandardRegressionSetRunResult pending = PendingRun(runId, artefact, digest);
        pending = pending with
        {
            Cases =
            [
                pending.Cases[0] with
                {
                    Assertions = [new RegressionAssertion("Structure", false, "Did not hold.")],
                },
                .. pending.Cases.Skip(1),
            ],
        };
        _synthetic.WriteResult(pending);

        string first = WriteDecisions(folder, "first.json", "PORTRAIT-VISUAL-001", "Passed");
        Review(folder, first);

        StandardRegressionSetRunResult afterFirst = ReadResult(folder);
        afterFirst.Status.ShouldBe("Failed",
            "A decision cannot turn a case whose structural assertion did not hold into a success.");
        afterFirst.Reviews.ShouldHaveSingleItem();

        // The same check again. It is decided now, so it is not outstanding, so it is refused.
        string second = WriteDecisions(folder, "second.json", "PORTRAIT-VISUAL-001", "Failed");

        using (EnvironmentVariables environment = new()
        {
            ["PRINTFLOW_REGRESSION_REAGGREGATE"] = folder,
            ["PRINTFLOW_REGRESSION_VISUAL_DECISIONS"] = second,
        })
        {
            Should.Throw<InvalidOperationException>(() =>
                    new Smoke.StandardRegressionSetWorkstationSmoke(new NullOutput())
                        .Re_derive_a_completed_run_from_recorded_visual_decisions())
                .Message.ShouldContain("answer nothing this run left open");
        }

        // And the first review is still there, unaltered.
        ReadResult(folder).Reviews.ShouldHaveSingleItem().Decisions
            .ShouldHaveSingleItem().Outcome.ShouldBe(RegressionOutcome.Passed);
    }

    /// <summary>A decision about an artefact that is no longer the one the run produced is refused.</summary>
    [Fact]
    public void A_review_of_a_changed_artefact_is_refused()
    {
        const string runId = "20260910-111500";
        string folder = _synthetic.RunFolder(runId);
        Directory.CreateDirectory(folder);

        string artefact = Path.Combine(folder, "portrait-preview.bin");
        File.WriteAllText(artefact, "synthetic preview bytes");
        _synthetic.WriteResult(PendingRun(runId, artefact,
            "0000000000000000000000000000000000000000000000000000000000000000"));

        string decisions = Path.Combine(folder, "decisions.json");
        File.WriteAllText(decisions, """
            {
              "decidedBy": "SYNTHETIC\\protocol-test",
              "synthetic": true,
              "decisions": [ { "id": "PORTRAIT-VISUAL-001", "outcome": "Passed", "notes": "n/a" } ]
            }
            """);

        using EnvironmentVariables environment = new()
        {
            ["PRINTFLOW_REGRESSION_REAGGREGATE"] = folder,
            ["PRINTFLOW_REGRESSION_VISUAL_DECISIONS"] = decisions,
        };

        Should.Throw<InvalidOperationException>(() =>
                new Smoke.StandardRegressionSetWorkstationSmoke(new NullOutput())
                    .Re_derive_a_completed_run_from_recorded_visual_decisions())
            .Message.ShouldContain("not the artefact the run produced");
    }

    // ==========================================================================================
    // F3 — a record is about the installation it names
    // ==========================================================================================

    /// <summary>
    /// The counterexample: the same version string, different Product bytes, refused.
    /// </summary>
    /// <remarks>
    /// The run genuinely passed and its result is genuine. What it attested is a candidate whose
    /// <c>PrintFlow.Infrastructure.dll</c> is not the one installed here, and no part of "PrintFlow
    /// 0.1.0" distinguishes the two. Before this slice the writer had nothing to compare and
    /// recorded the pass against whatever was installed.
    /// </remarks>
    [Fact]
    public void Publication_refuses_a_candidate_whose_bytes_differ_under_the_same_version()
    {
        const string runId = "20260910-120000";

        ImmutableArray<ProductAssemblyIdentity> otherCandidate =
        [
            .. ProductBuildIdentity.FromFolder(_synthetic.InstallFolder)
                .Select(a => a.Name == "PrintFlow.Infrastructure.dll"
                    ? a with { Sha256 = new string('A', 64) }
                    : a),
        ];

        string result = _synthetic.WriteResult(
            _synthetic.PassingRun(runId, Guid.NewGuid().ToString(), candidate: otherCandidate));

        ScriptOutcome outcome = Publish(result);

        outcome.ExitCode.ShouldNotBe(0);
        outcome.Says("THE PROPOSED REVALIDATION WAS REFUSED").ShouldBeTrue();
        outcome.Says("bytes do not match").ShouldBeTrue();
        File.Exists(_synthetic.RecordPath).ShouldBeFalse("Nothing may be written from refused input.");
    }

    /// <summary>
    /// The pre-origin writer accepted a different-source harness under matching labels; the current
    /// writer requires the run's exact harness to be one side of its captured build pair.
    /// </summary>
    [Fact]
    public void Publication_closes_the_same_label_different_source_counterexample()
    {
        const string runId = "20260910-120050";
        (RegressionBuildOrigin _, ImmutableArray<ProductAssemblyIdentity> differentSourceHarness) =
            _synthetic.WriteDifferentSourceHarnessReceiptWithSameLabels();
        ImmutableArray<ProductAssemblyIdentity> candidate =
            ProductBuildIdentity.FromFolder(_synthetic.InstallFolder);

        ProductBuildIdentity.CompareBuildIdentity(differentSourceHarness, candidate).ShouldBeEmpty(
            "the counterexample keeps every informational label equal");
        ProductBuildIdentity.CompareBytes(differentSourceHarness, candidate).Length.ShouldBeGreaterThan(0,
            "the generated Infrastructure source produces different bytes");

        StandardRegressionSetRunResult proposed = _synthetic.PassingRun(runId, Guid.NewGuid().ToString());
        proposed = proposed with
        {
            Binding = proposed.Binding! with
            {
                Version = 1,
                HarnessProductAssemblies = differentSourceHarness,
            },
        };
        string resultPath = _synthetic.WriteResult(proposed);

        string oldWriter = AuditedScript(
            "tools/installer/Set-PrintFlowProductionRevalidation.ps1",
            "1c591e986472644446d1112462b688e9c11e022b").ShouldNotBeNull(
                "The pre-A0 writer commit must be reachable to prove the consumer counterexample.");
        ScriptOutcome before = _synthetic.Run(
            oldWriter,
            $"-InstallFolder \"{_synthetic.InstallFolder}\" -EnvironmentReadinessPassed " +
            $"-StandardRegressionSetPath \"{_synthetic.ManifestFolder}\" " +
            $"-StandardRegressionSetResult \"{resultPath}\"");
        before.ExitCode.ShouldBe(0, before.Output);
        string active = File.ReadAllText(_synthetic.RecordPath);

        proposed = proposed with
        {
            Binding = proposed.Binding! with
            {
                Version = RegressionEvidenceBinding.CurrentVersion,
                BuildOrigin = _synthetic.BuildOrigin,
            },
        };
        _synthetic.WriteResult(proposed);

        ScriptOutcome after = Publish(resultPath);
        after.ExitCode.ShouldBe(3, after.Output);
        after.Says("recorded harness").ShouldBeTrue(after.Output);
        File.ReadAllText(_synthetic.RecordPath).ShouldBe(active,
            "origin refusal must preserve the active record the installation already had");
    }

    /// <summary>
    /// Publication rechecks both sides of the pair and preserves the active record when either
    /// output has been substituted after the run.
    /// </summary>
    [Fact]
    public void Publication_refuses_substituted_pair_outputs_and_preserves_the_active_record()
    {
        Publish(_synthetic.WriteResult(
            _synthetic.PassingRun("20260910-120100", Guid.NewGuid().ToString()))).ExitCode.ShouldBe(0);
        string active = File.ReadAllText(_synthetic.RecordPath);

        string harnessResult = _synthetic.WriteResult(
            _synthetic.PassingRun("20260910-120101", Guid.NewGuid().ToString()));
        string harnessAssembly = Path.Combine(_synthetic.HarnessFolder, "PrintFlow.Infrastructure.dll");
        byte[] harnessBytes = File.ReadAllBytes(harnessAssembly);
        try
        {
            File.WriteAllBytes(harnessAssembly, [.. harnessBytes, 0xA0]);
            ScriptOutcome refused = Publish(harnessResult);
            refused.ExitCode.ShouldBe(3, refused.Output);
            refused.Says("harness").ShouldBeTrue(refused.Output);
            File.ReadAllText(_synthetic.RecordPath).ShouldBe(active);
        }
        finally
        {
            File.WriteAllBytes(harnessAssembly, harnessBytes);
        }

        string candidateResult = _synthetic.WriteResult(
            _synthetic.PassingRun("20260910-120102", Guid.NewGuid().ToString()));
        string candidateAssembly = Path.Combine(_synthetic.InstallFolder, "PrintFlow.Infrastructure.dll");
        byte[] candidateBytes = File.ReadAllBytes(candidateAssembly);
        try
        {
            File.WriteAllBytes(candidateAssembly, [.. candidateBytes, 0xA0]);
            ScriptOutcome refused = Publish(candidateResult);
            refused.ExitCode.ShouldBe(3, refused.Output);
            refused.Says("candidate").ShouldBeTrue(refused.Output);
            File.ReadAllText(_synthetic.RecordPath).ShouldBe(active);
        }
        finally
        {
            File.WriteAllBytes(candidateAssembly, candidateBytes);
        }
    }

    /// <summary>A run remains bound to the exact receipt bytes it captured.</summary>
    [Fact]
    public void Publication_refuses_a_substituted_receipt_and_preserves_the_active_record()
    {
        Publish(_synthetic.WriteResult(
            _synthetic.PassingRun("20260910-120110", Guid.NewGuid().ToString()))).ExitCode.ShouldBe(0);
        string active = File.ReadAllText(_synthetic.RecordPath);
        string proposed = _synthetic.WriteResult(
            _synthetic.PassingRun("20260910-120111", Guid.NewGuid().ToString()));

        byte[] receipt = File.ReadAllBytes(_synthetic.BuildPairReceiptPath);
        try
        {
            File.WriteAllBytes(_synthetic.BuildPairReceiptPath, [.. receipt, (byte)' ']);
            ScriptOutcome refused = Publish(proposed);
            refused.ExitCode.ShouldBe(3, refused.Output);
            refused.Says("receipt").ShouldBeTrue(refused.Output);
            refused.Says("substituted").ShouldBeTrue(refused.Output);
            File.ReadAllText(_synthetic.RecordPath).ShouldBe(active);
        }
        finally
        {
            File.WriteAllBytes(_synthetic.BuildPairReceiptPath, receipt);
        }
    }

    /// <summary>
    /// F3 as it stood: the audit commit's writer records a genuine pass from one environment as an
    /// attestation about another.
    /// </summary>
    /// <remarks>
    /// The run is real and its result is real; what it tested was preset 0.0.2 and this installation
    /// is configured for 0.0.1. The audited writer never read that field — it read four — so it
    /// records "this installation, and the standard set passed", with nothing forged and nothing
    /// hand-edited. Needs the audit commit reachable; the repair is asserted separately and does not.
    /// </remarks>
    [Fact]
    public void The_audited_writer_recorded_a_pass_from_a_run_of_another_environment()
    {
        StandardRegressionSetRunResult elsewhere =
            _synthetic.PassingRun("20260910-121000", Guid.NewGuid().ToString()) with { PresetVersion = "0.0.2" };

        string audited = AuditedScript("tools/installer/Set-PrintFlowProductionRevalidation.ps1")
            .ShouldNotBeNull(
                "The audit commit 3f83863 must be reachable from this checkout for the historical half " +
                "of this counterexample to run. A shallow clone, an export or a rewritten history " +
                "cannot run it; the repaired behaviour is asserted separately and does not need git.");

        ScriptOutcome before = _synthetic.Run(
            audited,
            $"-InstallFolder \"{_synthetic.InstallFolder}\" -EnvironmentReadinessPassed " +
            $"-StandardRegressionSetPath \"{_synthetic.ManifestFolder}\" " +
            $"-StandardRegressionSetResult \"{_synthetic.WriteResult(elsewhere)}\"");

        before.ExitCode.ShouldBe(0,
            "The audited writer records a pass from a run of a different environment. If this no " +
            "longer reproduces, the counterexample has changed and the repair needs re-deriving, " +
            "not re-baselining.");

        ProductionRevalidationRecord recorded = ReadRecord();
        recorded.StandardRegressionSet!.Status.ShouldBe(StandardRegressionSetStatus.Passed);
        recorded.PresetVersion.ShouldBe(WorkstationVerificationFixture.PresetVersion,
            "The audited writer takes the preset from the machine and the status from the run, which " +
            "is the whole of F3.");
    }

    /// <summary>A run of a different environment is not an attestation about this one.</summary>
    [Fact]
    public void Publication_refuses_a_run_of_a_different_environment()
    {
        StandardRegressionSetRunResult elsewhere =
            _synthetic.PassingRun("20260910-121500", Guid.NewGuid().ToString()) with { PresetVersion = "0.0.2" };

        ScriptOutcome outcome = Publish(_synthetic.WriteResult(elsewhere));

        outcome.ExitCode.ShouldBe(3, outcome.Output);
        outcome.Says("configured for").ShouldBeTrue();
        File.Exists(_synthetic.RecordPath).ShouldBeFalse();
    }

    /// <summary>
    /// A summary that contradicts the cases underneath it is not a verdict, and neither is one that
    /// covers six categories or one twice.
    /// </summary>
    /// <remarks>
    /// One test for three shapes of the same defect — the writer re-deriving rather than copying —
    /// because they are one decision in the script and separate tests would only be separate
    /// spellings of it.
    /// </remarks>
    [Fact]
    public void Publication_re_derives_the_verdict_rather_than_copying_it()
    {
        StandardRegressionSetRunResult honest = _synthetic.PassingRun("20260910-1230", Guid.NewGuid().ToString());

        // A Passed summary over a case nobody decided.
        StandardRegressionSetRunResult contradictory = honest with
        {
            RunId = "20260910-123000",
            Cases =
            [
                honest.Cases[0] with
                {
                    Outcome = RegressionOutcome.Pending,
                    ManualDecisions =
                    [
                        new RegressionManualDecision(
                            "PORTRAIT-VISUAL-001", "Is the cutout clean?",
                            RegressionOutcome.Pending, null, null, null, null),
                    ],
                },
                .. honest.Cases.Skip(1),
            ],
        };

        ScriptOutcome contradiction = Publish(_synthetic.WriteResult(contradictory));
        contradiction.ExitCode.ShouldNotBe(0);
        contradiction.Says("contradicts its own cases").ShouldBeTrue();

        // Six of seven, with one of them claimed twice so the count still reaches seven.
        StandardRegressionSetRunResult duplicated = honest with
        {
            RunId = "20260910-123500",
            Status = "Passed",
            MissingCategories = [],
            Cases = [.. honest.Cases.Skip(1), honest.Cases[1]],
        };

        ScriptOutcome duplicates = Publish(_synthetic.WriteResult(duplicated));
        duplicates.ExitCode.ShouldNotBe(0);
        duplicates.Says("claimed by more than one case").ShouldBeTrue();
        duplicates.Says("no case for required category NORMAL_JPG_PORTRAIT").ShouldBeTrue();

        File.Exists(_synthetic.RecordPath).ShouldBeFalse();
    }

    /// <summary>
    /// Evidence from before the binding existed stays historical: it cannot be topped up into a
    /// current approval.
    /// </summary>
    [Fact]
    public void Legacy_evidence_cannot_be_enriched_into_a_current_approval()
    {
        const string runId = "20260910-124500";

        StandardRegressionSetRunResult legacy =
            _synthetic.PassingRun(runId, Guid.NewGuid().ToString()) with { Binding = null };

        ScriptOutcome outcome = Publish(_synthetic.WriteResult(legacy));

        outcome.ExitCode.ShouldNotBe(0);
        outcome.Says("carries no evidence binding").ShouldBeTrue();
        File.Exists(_synthetic.RecordPath).ShouldBeFalse();

        StandardRegressionSetRunResult versionOne = _synthetic.PassingRun(
            "20260910-124501", Guid.NewGuid().ToString());
        versionOne = versionOne with
        {
            Binding = versionOne.Binding! with { Version = 1, BuildOrigin = null },
        };
        ScriptOutcome oldBinding = Publish(_synthetic.WriteResult(versionOne));
        oldBinding.ExitCode.ShouldBe(3, oldBinding.Output);
        oldBinding.Says("evidence binding is version 1").ShouldBeTrue(oldBinding.Output);

        StandardRegressionSetRunResult missingOrigin = _synthetic.PassingRun(
            "20260910-124502", Guid.NewGuid().ToString());
        missingOrigin = missingOrigin with
        {
            Binding = missingOrigin.Binding! with { BuildOrigin = null },
        };
        ScriptOutcome unbound = Publish(_synthetic.WriteResult(missingOrigin));
        unbound.ExitCode.ShouldBe(3, unbound.Output);
        unbound.Says("cannot enrich historical evidence").ShouldBeTrue(unbound.Output);
        File.Exists(_synthetic.RecordPath).ShouldBeFalse();
    }

    /// <summary>
    /// Refused input leaves the record an installation already has exactly as it was.
    /// </summary>
    /// <remarks>
    /// The property that makes refusal safe to attempt. A writer that cleared or rewrote the active
    /// record on its way to rejecting its input would close Production every time an operator
    /// pointed it at the wrong file.
    /// </remarks>
    [Fact]
    public void Refused_publication_leaves_the_active_record_untouched()
    {
        string valid = _synthetic.WriteResult(
            _synthetic.PassingRun("20260910-130000", Guid.NewGuid().ToString()));
        Publish(valid).ExitCode.ShouldBe(0);

        string published = File.ReadAllText(_synthetic.RecordPath);

        string mismatched = _synthetic.WriteResult(
            _synthetic.PassingRun("20260910-131500", Guid.NewGuid().ToString(), productVersion: "9.9.9"));
        ScriptOutcome refused = Publish(mismatched);

        refused.ExitCode.ShouldNotBe(0);
        refused.Says("is untouched").ShouldBeTrue();
        File.ReadAllText(_synthetic.RecordPath).ShouldBe(published);
    }

    /// <summary>
    /// A supplied path that does not resolve is refused, not read as "no run result was supplied".
    /// </summary>
    /// <remarks>
    /// The difference between omitting a parameter and mistyping one. Omitting
    /// <c>-StandardRegressionSetResult</c> is the operator deliberately recording that this
    /// installation has no passing set; a typo in it is input the script cannot read. Treating the
    /// second as the first would close Production and supersede the operator's existing record on
    /// the strength of a slip — and would do it while reporting the same exit code as the deliberate
    /// act, so nothing downstream could tell them apart.
    /// </remarks>
    [Fact]
    public void A_supplied_path_that_does_not_resolve_is_refused_rather_than_read_as_absent()
    {
        Publish(_synthetic.WriteResult(
            _synthetic.PassingRun("20260910-132500", Guid.NewGuid().ToString()))).ExitCode.ShouldBe(0);

        string published = File.ReadAllText(_synthetic.RecordPath);

        ScriptOutcome typo = _synthetic.Run(
            Writer,
            $"-InstallFolder \"{_synthetic.InstallFolder}\" -EnvironmentReadinessPassed " +
            $"-StandardRegressionSetPath \"{_synthetic.ManifestFolder}\" " +
            $"-StandardRegressionSetResult \"{Path.Combine(_synthetic.SetRoot, "runs", "typo", "result.json")}\"");

        typo.ExitCode.ShouldBe(3, typo.Output);
        typo.Says("does not exist").ShouldBeTrue();
        File.ReadAllText(_synthetic.RecordPath).ShouldBe(published,
            "A typo must not supersede the record the operator already had.");

        // The same for the set path, which decides the seven-category gate.
        ScriptOutcome wrongSet = _synthetic.Run(
            Writer,
            $"-InstallFolder \"{_synthetic.InstallFolder}\" -EnvironmentReadinessPassed " +
            $"-StandardRegressionSetPath \"{Path.Combine(_synthetic.SetRoot, "manifest")}\"");

        wrongSet.ExitCode.ShouldBe(3, wrongSet.Output);
        File.ReadAllText(_synthetic.RecordPath).ShouldBe(published);
    }

    /// <summary>
    /// A run whose qualitative checks were concluded by a synthetic review cannot open Production.
    /// </summary>
    /// <remarks>
    /// The label the review path writes is load-bearing, not decorative. These very tests conclude
    /// visual checks synthetically, and a protocol test must not be able to produce a production
    /// approval however correctly bound the rest of its evidence is.
    /// </remarks>
    [Fact]
    public void A_synthetic_review_cannot_open_production()
    {
        const string runId = "20260910-133000";
        string folder = _synthetic.RunFolder(runId);
        Directory.CreateDirectory(folder);

        string artefact = Path.Combine(folder, "portrait-preview.bin");
        File.WriteAllText(artefact, "synthetic preview bytes");
        string digest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(artefact)));

        _synthetic.WriteResult(PendingRun(runId, artefact, digest));
        Review(folder, WriteDecisions(folder, "decisions.json", "PORTRAIT-VISUAL-001", "Passed"));

        ReadResult(folder).Status.ShouldBe("Passed", "The run itself is now a pass on its own terms.");

        ScriptOutcome outcome = Publish(Path.Combine(folder, "result.json"));

        outcome.ExitCode.ShouldBe(3, outcome.Output);
        outcome.Says("concluded by a synthetic review").ShouldBeTrue();
        File.Exists(_synthetic.RecordPath).ShouldBeFalse();
    }

    /// <summary>
    /// Recording a revalidation without a run result still works, and still closes Production.
    /// </summary>
    /// <remarks>
    /// Refusal and revocation are different operations and this slice keeps them apart. An operator
    /// who runs the writer with no result is deliberately recording that this installation has no
    /// passing set — the existing, legitimate way to close Production — and that must not be
    /// confused with the writer rejecting input it cannot bind.
    /// </remarks>
    [Fact]
    public void Recording_no_passing_set_remains_a_deliberate_operation()
    {
        ScriptOutcome outcome = _synthetic.Run(
            Writer,
            $"-InstallFolder \"{_synthetic.InstallFolder}\" -EnvironmentReadinessPassed " +
            $"-StandardRegressionSetPath \"{_synthetic.ManifestFolder}\"");

        outcome.ExitCode.ShouldBe(2, outcome.Output);
        outcome.Says("Production remains CLOSED").ShouldBeTrue();

        ProductionRevalidationRecord record = ReadRecord();
        record.StandardRegressionSet!.Status.ShouldBe(StandardRegressionSetStatus.NotAvailable);
    }

    // ==========================================================================================
    // The Product reads what was published
    // ==========================================================================================

    /// <summary>
    /// An accepted publication is read back by the Product's own evaluator, and the same record
    /// against different Product bytes is not.
    /// </summary>
    /// <remarks>
    /// The end of the chain, and the one test that crosses it whole: a run result written by the
    /// run result's own writer, published by the operator's script, then read from disk by
    /// <see cref="FileProductionRevalidationReader"/> and judged by the evaluator the application
    /// uses. The second half changes one thing — the bytes the evaluator observes — and the record
    /// stops applying, which is the guarantee a version string could not give.
    /// </remarks>
    [Fact]
    public void An_accepted_publication_is_read_by_the_product_evaluator()
    {
        Publish(_synthetic.WriteResult(
            _synthetic.PassingRun("20260910-140000", Guid.NewGuid().ToString()))).ExitCode.ShouldBe(0);

        ProductionRevalidationRecord record = ReadRecord();
        record.SchemaVersion.ShouldBe(ProductionRevalidationRecord.CurrentSchemaVersion);
        record.StandardRegressionSet!.Status.ShouldBe(StandardRegressionSetStatus.Passed);
        record.StandardRegressionSet.RunId.ShouldBe("20260910-140000");

        WorkstationRequirements requirements = Requirements();

        Evaluate(record, requirements, ProductBuildIdentity.Running())
            .Outcome.ShouldBe(WorkstationCheckOutcome.Passed);

        ImmutableArray<ProductAssemblyIdentity> different =
        [
            .. ProductBuildIdentity.Running()
                .Select(a => a.Name == "PrintFlow.App.dll" ? a with { Sha256 = new string('B', 64) } : a),
        ];

        WorkstationCheckResult refused = Evaluate(record, requirements, different);
        refused.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        refused.Explanation.ShouldContain("not the ones the recorded revalidation was run against");
    }

    /// <summary>
    /// A record written before the binding existed is readable, blocking, and says which it is.
    /// </summary>
    /// <remarks>
    /// Compatibility without promotion. The historical record states honestly what an operator did;
    /// what it cannot state is that the run behind it tested these bytes, so it blocks — and it
    /// blocks with its own sentence rather than a generic unknown-schema one, because an operator
    /// reading it needs to know that nothing is broken and a run is simply owed.
    /// </remarks>
    [Fact]
    public void A_pre_binding_record_stays_historical()
    {
        WorkstationRequirements requirements = Requirements();

        ProductionRevalidationRecord historical = _workstation.MatchingRevalidationRecord() with
        {
            SchemaVersion = ProductionRevalidationRecord.HistoricalSchemaVersion,
            ProductAssemblies = [],
            StandardRegressionSet = new StandardRegressionSetOutcome(
                "printflow-regression-v1", StandardRegressionSetStatus.Passed,
                "2026-09-09T14:30:00+12:00", @"D:\PrintFlowStudio\TestData\v1\runs\20260909-183000"),
        };

        WorkstationCheckResult result = Evaluate(historical, requirements, ProductBuildIdentity.Running());
        result.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        result.Explanation.ShouldContain("predates the regression evidence binding");

        // And the enrichment shape: the same historical outcome under the current schema version,
        // which is what topping an old record up would produce. It names no candidate, so it does
        // not describe these bytes and cannot pass either.
        WorkstationCheckResult enriched = Evaluate(
            historical with { SchemaVersion = ProductionRevalidationRecord.CurrentSchemaVersion },
            requirements,
            ProductBuildIdentity.Running());

        enriched.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        enriched.Explanation.ShouldContain("no bytes recorded");
    }

    /// <summary>A pass with no execution behind it is not a pass.</summary>
    [Fact]
    public void A_recorded_pass_names_the_execution_it_passed_in()
    {
        WorkstationRequirements requirements = Requirements();

        ProductionRevalidationRecord unbound = _workstation.MatchingRevalidationRecord() with
        {
            StandardRegressionSet = new StandardRegressionSetOutcome(
                "printflow-regression-v1", StandardRegressionSetStatus.Passed,
                "2026-09-09T14:30:00+12:00", "evidence"),
        };

        WorkstationCheckResult result = Evaluate(unbound, requirements, ProductBuildIdentity.Running());
        result.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        result.Explanation.ShouldContain("names no execution");
    }

    // ==========================================================================================
    // Plumbing
    // ==========================================================================================

    private ScriptOutcome Publish(string resultPath) =>
        _synthetic.Run(
            Writer,
            $"-InstallFolder \"{_synthetic.InstallFolder}\" -EnvironmentReadinessPassed " +
            $"-StandardRegressionSetPath \"{_synthetic.ManifestFolder}\" " +
            $"-StandardRegressionSetResult \"{resultPath}\"");

    private ProductionRevalidationRecord ReadRecord() =>
        new FileProductionRevalidationReader(_synthetic.WorkspaceRoot).Read()
            .ShouldNotBeNull("No revalidation record was written.");

    private WorkstationRequirements Requirements() =>
        PresetWorkstationRequirements.Read(
            _workstation.ManifestPath,
            WorkstationVerificationFixture.PresetId,
            WorkstationVerificationFixture.PresetVersion,
            _workstation.ManifestSha256).Value;

    private WorkstationCheckResult Evaluate(
        ProductionRevalidationRecord record,
        WorkstationRequirements requirements,
        ImmutableArray<ProductAssemblyIdentity> observed) =>
        ProductionRevalidationEvaluator.Evaluate(
            record,
            requirements,
            _workstation.ManifestSha256,
            record.OperatingSystemBuild!,
            record.ProductVersion!,
            observed);

    private StandardRegressionSetRunResult PendingRun(string runId, string artefact, string artefactDigest)
    {
        StandardRegressionSetRunResult complete = _synthetic.PassingRun(runId, Guid.NewGuid().ToString());

        return complete with
        {
            Status = "Pending",
            Cases =
            [
                complete.Cases[0] with
                {
                    Outcome = RegressionOutcome.Pending,
                    ProducedArtefacts = [new RegressionArtefact("preview", artefact, artefactDigest, 23)],
                    ManualDecisions =
                    [
                        new RegressionManualDecision(
                            "PORTRAIT-VISUAL-001", "Is the cutout clean?",
                            RegressionOutcome.Pending, null, null, artefact, null),
                    ],
                },
                .. complete.Cases.Skip(1),
            ],
        };
    }

    private static string WriteDecisions(string folder, string name, string id, string outcome)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, $$"""
            {
              "decidedBy": "SYNTHETIC\\protocol-test",
              "decidedAtLocal": "2026-09-17T14:00:00+12:00",
              "synthetic": true,
              "decisions": [ { "id": "{{id}}", "outcome": "{{outcome}}", "notes": "Protocol test." } ]
            }
            """);
        return path;
    }

    private static void Review(string folder, string decisionsPath)
    {
        using EnvironmentVariables environment = new()
        {
            ["PRINTFLOW_REGRESSION_REAGGREGATE"] = folder,
            ["PRINTFLOW_REGRESSION_VISUAL_DECISIONS"] = decisionsPath,
        };

        new Smoke.StandardRegressionSetWorkstationSmoke(new NullOutput())
            .Re_derive_a_completed_run_from_recorded_visual_decisions();
    }

    private static StandardRegressionSetRunResult ReadResult(string folder) =>
        System.Text.Json.JsonSerializer.Deserialize<StandardRegressionSetRunResult>(
            File.ReadAllText(Path.Combine(folder, "result.json")),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            })!;

    /// <summary>
    /// The audit commit's own version of a script, written where this test's temporary files live.
    /// </summary>
    /// <remarks>
    /// These reproductions are the strongest evidence in the slice — they show each defect at the
    /// public boundary rather than describing it — so they are kept as tests rather than moved into
    /// prose. They can only run where the audit commit is reachable, so each is a test of its own and
    /// the repaired behaviour is asserted by a separate test that needs no git at all. A checkout that
    /// cannot reach the commit fails these two with a stated reason, which is the honest outcome: the
    /// alternative is a test that passes while proving nothing.
    /// </remarks>
    private string? AuditedScript(
        string repositoryPath,
        string revision = "3f83863521c9b682f02b19ede1bcdb3a86dc60aa")
    {
        string spec = $"{revision}:{repositoryPath}";
        byte[] content;

        try
        {
            using System.Diagnostics.Process git = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"show {spec}",
                    WorkingDirectory = RepositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                })!;

            using MemoryStream buffer = new();
            git.StandardOutput.BaseStream.CopyTo(buffer);
            git.WaitForExit();

            if (git.ExitCode != 0 || buffer.Length == 0)
            {
                return null;
            }

            content = buffer.ToArray();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }

        string path = _synthetic.Scratch("audited-" + Path.GetFileName(repositoryPath));
        File.WriteAllBytes(path, content);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintFlowStudio.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    public void Dispose()
    {
        _synthetic.Dispose();
        _workstation.Dispose();
    }

    /// <summary>Swallows the runner's narration; these tests assert on files, not on prose.</summary>
    private sealed class NullOutput : Xunit.Abstractions.ITestOutputHelper
    {
        public void WriteLine(string message)
        {
        }

        public void WriteLine(string format, params object[] args)
        {
        }
    }
}
