using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Workflow.Definitions;

namespace PrintFlow.Workflow.Engine;

/// <summary>
/// Everything the workflow engine is allowed to see about one session.
/// </summary>
/// <remarks>
/// This is the reducer's entire input state. It contains no file handles, no repository, no
/// clock and no adapter — which is what makes the engine testable without a database or a
/// disk (Epic 11100 plan §8.1).
///
/// <see cref="HasDerivedRevision"/> is the fact that freezes the workflow choice: once any
/// non-root Revision exists, the session cannot switch workflows (MVP design §6.1).
/// </remarks>
/// <param name="SessionId">Which session this describes.</param>
/// <param name="WorkflowType">The selected workflow.</param>
/// <param name="OutputName">The current editable output name.</param>
/// <param name="SessionState">Session-level lifecycle state.</param>
/// <param name="Steps">One entry per step of the workflow definition, in ordinal order.</param>
/// <param name="HasDerivedRevision">Whether any Revision beyond the import root exists.</param>
/// <param name="LatestApprovedRevisionId">The most recent approved Revision, if any.</param>
/// <param name="Dimensions">Confirmed print dimensions, once the operator has set them.</param>
/// <param name="WhiteUnderbaseBranch">
/// The explicitly chosen W1 branch. Null means not chosen — there is deliberately no default.
/// </param>
/// <param name="ApprovedPrintOutputCount">How many approved PrintOutputs this session holds.</param>
public sealed record WorkflowSnapshot(
    SessionId SessionId,
    WorkflowType WorkflowType,
    OutputName OutputName,
    SessionState SessionState,
    IReadOnlyList<SessionStep> Steps,
    bool HasDerivedRevision,
    RevisionId? LatestApprovedRevisionId,
    PrintDimensions? Dimensions,
    WhiteUnderbaseBranch? WhiteUnderbaseBranch,
    int ApprovedPrintOutputCount)
{
    /// <summary>The definition this snapshot is being driven against.</summary>
    public bool RequiresPsdPreparation { get; init; }

    public WorkflowDefinition Definition => WorkflowCatalog.For(WorkflowType, RequiresPsdPreparation);

    /// <summary>
    /// The margin the next deterministic Trim attempt will run with
    /// (Epic 11200 Part C3 §10, §13).
    /// </summary>
    /// <remarks>
    /// <b>This one does have a default, and that is deliberate.</b> It starts at
    /// <see cref="TrimMargin.Tight"/> — crop exactly to the alpha content, zero margin on all
    /// four edges — which is the behaviour every trim has had since Part B, so a session that
    /// never opens the margin controls behaves exactly as it did before. Contrast
    /// <see cref="WhiteUnderbaseBranch"/>, which is null until an operator chooses: W1 is a
    /// classification of the artwork that only a human can make, and a pre-selected branch
    /// would be that judgement made by the software (MVP design §12). A trim margin is an
    /// operational parameter of a deterministic algorithm, and "no margin" is the honest
    /// zero rather than a guess.
    /// <para>
    /// What is emphatically <i>not</i> defaulted is a non-zero safety margin: nothing anywhere
    /// adds pixels the operator did not ask for (§10).
    /// </para>
    /// <para>
    /// An <c>init</c> property rather than a positional parameter so <c>default</c> is
    /// <see cref="TrimMargin.Tight"/> at every construction site without one of them having to
    /// say so.
    /// </para>
    /// </remarks>
    public TrimMargin TrimMargin { get; init; } = TrimMargin.Tight;

    /// <summary>
    /// The reviewed-content authority the next Background Removal attempt would run under
    /// (Epic 11300 Part C2B1 §4).
    /// </summary>
    /// <remarks>
    /// <b>Null by default, and that is deliberate.</b> Authorising Meitu to select a subject
    /// automatically is a judgement about content a human looked at, so there is nothing for the
    /// software to default to — the same reason <see cref="WhiteUnderbaseBranch"/> is null until
    /// chosen (MVP design §12), and the opposite of <see cref="TrimMargin"/>, whose zero is an
    /// honest zero rather than a guess.
    /// <para>
    /// Holding a non-null value here is not the same as being authorised. The authority names
    /// the artefact it covers, and <see cref="UsableBackgroundRemovalAuthority"/> is the only
    /// thing that answers whether it covers the artefact Background Removal is about to consume
    /// (§8).
    /// </para>
    /// <para>
    /// An <c>init</c> property rather than a positional parameter so <c>default</c> is "no
    /// decision" at every construction site without one of them having to say so.
    /// </para>
    /// </remarks>
    public BackgroundRemovalAuthority? BackgroundRemovalAuthority { get; init; }

    /// <summary>
    /// The authority that currently authorises a Background Removal run, or null when none
    /// does (Epic 11300 Part C2B1 §8, §9, §23).
    /// </summary>
    /// <remarks>
    /// The single definition of "the authority is still usable", asked by the engine before it
    /// will start the step, by the decision command before it will re-bind, and by
    /// <c>SessionView</c> before it will report the session as ready. One predicate is what
    /// makes an offered control and an accepted command unable to disagree.
    /// <para>
    /// This is also the whole of the invalidation strategy (§9). A stale authority is not
    /// hunted down and deleted when its Revision is replaced; it is simply never usable again,
    /// because the artefact it names is no longer the one on offer. Retry over byte-identical
    /// reviewed content therefore stays authorised without anything having to re-grant it (§17),
    /// and a changed upstream stops being authorised without anything having to revoke it (§19).
    /// </para>
    /// </remarks>
    public BackgroundRemovalAuthority? UsableBackgroundRemovalAuthority
    {
        get
        {
            if (BackgroundRemovalAuthority is not { } authority ||
                UpstreamResultOf(StepKind.BackgroundRemoval) is not { } upstream)
            {
                return null;
            }

            return authority.Authorises(upstream.Id, upstream.Sha256) ? authority : null;
        }
    }

    /// <summary>
    /// What <see cref="Dimensions"/> means on this session (Epic 11400 Part B1A.2A §4, §9).
    /// </summary>
    /// <remarks>
    /// Null exactly when <see cref="Dimensions"/> is null, and never a default: a resumed session
    /// whose pair was written before the maximum-bound contract reads back as
    /// <see cref="PrintDimensionSemantics.LegacyExactPair"/> rather than being reinterpreted as a
    /// fit box (§9).
    /// <para>
    /// An <c>init</c> property rather than a positional parameter so <c>default</c> is "no
    /// dimensions and therefore no reading" at every construction site.
    /// </para>
    /// </remarks>
    public PrintDimensionSemantics? DimensionSemantics { get; init; }

    /// <summary>
    /// The source-bound plan the next Photoshop attempt would run with
    /// (Epic 11400 Part B1A.2A §5).
    /// </summary>
    /// <remarks>
    /// <b>Null by default, and that is deliberate.</b> A limiting edge is a property of a fit box
    /// <i>against particular source pixels</i>, so there is nothing for the software to default
    /// to — the same reason <see cref="BackgroundRemovalAuthority"/> is null until granted.
    /// <para>
    /// Holding a non-null value here is not the same as being runnable. The plan names the
    /// artefact it was calculated from, and
    /// <see cref="UsablePrintPreparationPlan"/> is the only thing that answers whether that is
    /// still the artefact Photoshop is about to consume (§7).
    /// </para>
    /// </remarks>
    public PrintPreparationPlan? PrintPreparationPlan { get; init; }

    /// <summary>
    /// The plan that currently permits a Photoshop output run, or null when none does
    /// (Epic 11400 Part B1A.2A §7, §15, §16).
    /// </summary>
    /// <remarks>
    /// The single definition of "the plan is still usable", asked by the engine before it will
    /// start the step, by the service before it snapshots the plan onto an attempt, and by
    /// <c>SessionView</c> before it reports readiness. One predicate is what makes an offered
    /// control and an accepted command unable to disagree — the arrangement
    /// <see cref="UsableBackgroundRemovalAuthority"/> established, applied to the same problem.
    /// <para>
    /// It is also the whole invalidation strategy. A stale plan is never hunted down and deleted;
    /// it is simply never usable again, because the artefact it names is no longer the one on
    /// offer. A retry over byte-identical upstream content therefore stays runnable without
    /// anything re-granting it (§16), and a changed upstream stops being runnable without
    /// anything revoking it.
    /// </para>
    /// <para>
    /// The semantics check comes first and is not redundant. A legacy exact pair has no plan at
    /// all, so it fails here for the honest reason — its dimensions were never a fit box — rather
    /// than by accidentally missing a binding (§10).
    /// </para>
    /// </remarks>
    public PrintPreparationPlan? UsablePrintPreparationPlan
    {
        get
        {
            if (DimensionSemantics != PrintDimensionSemantics.MaxBoundsV1 ||
                PrintPreparationPlan is not { } plan ||
                UpstreamResultOf(StepKind.PhotoshopOutput) is not { } upstream)
            {
                return null;
            }

            return plan.Covers(upstream.Id, upstream.Sha256) && MatchesConfiguredRecommendation
                ? plan
                : null;
        }
    }

    /// <summary>
    /// The named-size recommendations this installation's verified preset configures right now,
    /// or null when this build could not ask (post-final A5 correction §14).
    /// </summary>
    /// <remarks>
    /// Supplied by whoever reconstructs the snapshot, because it is a fact about the current
    /// installation rather than about the session — the session's own rows say what the operator
    /// was shown when they decided, and those two can now differ.
    /// </remarks>
    public PresetPrintRecommendationSet? ConfiguredRecommendations { get; init; }

    /// <summary>
    /// Whether an ordinary preset fit was made against the recommendation this installation
    /// configures today (post-final A5 correction §13, §14).
    /// </summary>
    /// <remarks>
    /// A pending preset decision is a decision to print "what A5 means here", and what A5 means
    /// here is versioned. When v1.15.0 changed A5 from a 135 mm maximum long edge to a 135 mm
    /// maximum <i>short</i> edge, every pending A5 plan in the database became a plan for a
    /// geometry the shop no longer recommends — so it stops being usable and the operator
    /// reconfirms the size through the ordinary <c>ReturnToStep(PrintDimensions)</c> route. The
    /// stored rows are not touched, exactly as a stale plan's rows are not: it simply stops being
    /// returned, and <see cref="NeedsDimensionReview"/> becomes true (§13).
    /// <para>
    /// The whole recommendation is compared, not just the preset name — kind and both millimetre
    /// values — because "A5 at 135" was true before the correction and after it, and only the kind
    /// says which 135 mm was meant (§14).
    /// </para>
    /// <para>
    /// It gates <see cref="UsablePrintPreparationPlan"/> and nothing else. A custom target edge is
    /// the operator's own explicit millimetres and stays executable across a preset change; the
    /// recommendation it carries is context for the override notice, not the thing being run (§14).
    /// </para>
    /// <para>
    /// A null <see cref="ConfiguredRecommendations"/> fails closed for a preset fit. An
    /// installation that cannot say what A5 currently means cannot confirm that this plan is still
    /// what A5 means, and guessing that it is would be the silent execution §13 forbids.
    /// </para>
    /// </remarks>
    private bool MatchesConfiguredRecommendation =>
        SizeSelection is not { Mode: OperatorSizingMode.PresetFit, Recommendation: { } held } ||
        ConfiguredRecommendations?.For(held.Preset) == held;

    /// <summary>
    /// The operator's current size decision in the flexible-size vocabulary
    /// (Epic 11400 Part B1A.2D §6).
    /// </summary>
    /// <remarks>
    /// What was chosen, not what it works out to. Null on a session whose bounds were typed
    /// rather than chosen from a named preset, and on every session recorded before the
    /// flexible-size contract. Null never means "PresetFit was assumed" (§21).
    /// </remarks>
    public FlexibleSizeSelection? SizeSelection { get; init; }

    /// <summary>
    /// The TargetEdgeV1 plan the next Photoshop attempt would run with
    /// (Epic 11400 Part B1A.2D §8).
    /// </summary>
    /// <remarks>
    /// The flexible-size sibling of <see cref="PrintPreparationPlan"/>. At most one of the two is
    /// set, and <see cref="DimensionSemantics"/> says which — an existing MaxBoundsV1 record is
    /// never re-read as a target-edge plan (§5, §18).
    /// </remarks>
    public TargetEdgePrintPreparationPlan? TargetEdgePlan { get; init; }

    /// <summary>
    /// The operator's explicit permission to enlarge, when one has been granted
    /// (Epic 11400 Part B1A.2D §9, §10).
    /// </summary>
    /// <remarks>
    /// <b>Null by default, and that is deliberate.</b> Nothing creates one when a size is
    /// recorded: adding pixels the source does not hold is a separate judgement, made knowingly
    /// and confirmed a second time (§10).
    /// <para>
    /// Holding a non-null value here is not the same as being authorised — the authority names the
    /// exact source, edge, request and projection it covers, and
    /// <see cref="UsablePhotoshopPreparation"/> is the only thing that answers whether it covers
    /// the run about to start.
    /// </para>
    /// </remarks>
    public EnlargementAuthority? EnlargementAuthority { get; init; }

    /// <summary>
    /// The TargetEdgeV1 plan that currently permits a Photoshop output run, or null when none
    /// does (Epic 11400 Part B1A.2D §13).
    /// </summary>
    /// <remarks>
    /// The same shape as <see cref="UsablePrintPreparationPlan"/>, asking the semantics question
    /// first for the same reason: a session holding maximum bounds fails here honestly — those
    /// millimetres were never a target edge — rather than by accidentally missing a binding.
    /// <para>
    /// It says nothing about enlargement. Whether the plan may actually run is
    /// <see cref="UsablePhotoshopPreparation"/>'s answer, which is where the authority requirement
    /// lives, so a caller cannot get "the plan is current" and read it as "the plan may run".
    /// </para>
    /// </remarks>
    public TargetEdgePrintPreparationPlan? UsableTargetEdgePlan
    {
        get
        {
            if (DimensionSemantics != PrintDimensionSemantics.TargetEdgeV1 ||
                TargetEdgePlan is not { } plan ||
                UpstreamResultOf(StepKind.PhotoshopOutput) is not { } upstream)
            {
                return null;
            }

            return plan.Covers(upstream.Id, upstream.Sha256) ? plan : null;
        }
    }

    /// <summary>
    /// The one authority for "could Photoshop output start right now, and with what"
    /// (Epic 11400 Part B1A.2D §13).
    /// </summary>
    /// <remarks>
    /// <b>Every caller asks this and nothing else.</b> The engine's <c>StartStep</c> precondition,
    /// the attempt snapshot, and <c>SessionView</c>'s readiness all read this single property, so
    /// an offered control, an accepted command and a written audit row cannot disagree about what
    /// "usable" means — the arrangement <see cref="UsableBackgroundRemovalAuthority"/> established,
    /// extended to a decision that now has two accepted shapes.
    /// <para>
    /// It answers with a <see cref="PhotoshopPreparation"/> rather than a boolean, which is what
    /// makes the agreement structural: the value a caller receives <i>is</i> the thing that was
    /// validated, so nothing downstream re-derives which plan applied or re-checks whether an
    /// enlargement was permitted. A <see cref="TargetEdgePreparation"/> cannot even be constructed
    /// for an enlargement without its matching authority (§12).
    /// </para>
    /// <para>
    /// The whole invalidation strategy lives here too. A stale plan is never hunted down and
    /// deleted; it simply stops being returned, because the artefact it names is no longer the one
    /// on offer. Retry over byte-identical content therefore stays runnable — enlargement
    /// authority included — without anything re-granting it, and a changed upstream stops being
    /// runnable without anything revoking it (§30).
    /// </para>
    /// </remarks>
    public PhotoshopPreparation? UsablePhotoshopPreparation
    {
        get
        {
            if (UsablePrintPreparationPlan is { } bounds)
            {
                // The ordinary preset fit behind those bounds travels with them, so the attempt
                // this preparation is snapshotted onto can state the configured recommendation it
                // ran under rather than leaving a reader to infer one from the bounds
                // (post-final A5 correction §18).
                return new FitWithinBoundsPreparation(
                    bounds,
                    SizeSelection is { Mode: OperatorSizingMode.PresetFit } fit ? fit : null);
            }

            if (UsableTargetEdgePlan is not { } plan)
            {
                return null;
            }

            // The authority is matched against this exact plan, never merely held. An enlargement
            // with no matching authority is not "nearly runnable": it is a missing product
            // decision, and returning null is what makes StartStep refuse before an attempt row
            // exists (§10, §14).
            EnlargementAuthority? authority =
                plan.RequiresEnlargementAuthority && EnlargementAuthority?.Authorises(plan) == true
                    ? EnlargementAuthority
                    : null;

            return plan.IsExecutableWith(authority)
                ? new TargetEdgePreparation(plan, authority)
                : null;
        }
    }

    /// <summary>
    /// Whether the operator must reconfirm the print limits before Photoshop output can run
    /// (Epic 11400 Part B1A.2A §10, §19).
    /// </summary>
    /// <remarks>
    /// True exactly when this session already holds dimensions that cannot be executed: a legacy
    /// exact pair, or a maximum-bound plan whose source has since changed. It is deliberately
    /// <i>not</i> true for a session that simply has not set dimensions yet — that one is not
    /// under review, it is at an ordinary unfinished step.
    /// <para>
    /// The historical dimensions stay on the session in both cases. This says they need looking
    /// at again, never that they should be discarded or guessed at.
    /// </para>
    /// </remarks>
    public bool NeedsDimensionReview =>
        Definition.Contains(StepKind.PhotoshopOutput) &&
        Dimensions is not null &&
        UsablePhotoshopPreparation is null &&
        !NeedsEnlargementAuthority;

    /// <summary>
    /// Whether a current, correctly sized decision is waiting only on the operator's explicit
    /// permission to enlarge (Epic 11400 Part B1A.2D §10, §28).
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="NeedsDimensionReview"/>, because the two ask the
    /// operator for opposite things. A session needing dimension review has a size that cannot be
    /// executed and must be chosen again; a session needing enlargement authority has a size that
    /// is exactly right and needs confirming. Telling an operator to redo a size they meant would
    /// be the software losing their decision (§11).
    /// </remarks>
    public bool NeedsEnlargementAuthority =>
        UsableTargetEdgePlan is { RequiresEnlargementAuthority: true } plan &&
        EnlargementAuthority?.Authorises(plan) != true;

    /// <summary>
    /// Whether a granted enlargement authority actually covers the plan on offer (§10, §30).
    /// </summary>
    public bool HasUsableEnlargementAuthority =>
        UsableTargetEdgePlan is { RequiresEnlargementAuthority: true } plan &&
        EnlargementAuthority?.Authorises(plan) == true;

    /// <summary>
    /// Value equality, including the step list element by element.
    /// </summary>
    /// <remarks>
    /// The compiler-generated comparison would use reference equality for
    /// <see cref="Steps"/>, so two snapshots describing an identical session would compare
    /// unequal purely because the lists were built separately. That would quietly weaken
    /// every "the state did not change" and "reload restores the same state" assertion, so
    /// the comparison is written out.
    /// </remarks>
    public bool Equals(WorkflowSnapshot? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return SessionId.Equals(other.SessionId)
            && WorkflowType == other.WorkflowType
            && OutputName.Equals(other.OutputName)
            && SessionState == other.SessionState
            && HasDerivedRevision == other.HasDerivedRevision
            && Nullable.Equals(LatestApprovedRevisionId, other.LatestApprovedRevisionId)
            && Nullable.Equals(Dimensions, other.Dimensions)
            && Nullable.Equals(WhiteUnderbaseBranch, other.WhiteUnderbaseBranch)
            && ApprovedPrintOutputCount == other.ApprovedPrintOutputCount
            && TrimMargin.Equals(other.TrimMargin)
            && Equals(BackgroundRemovalAuthority, other.BackgroundRemovalAuthority)
            && Nullable.Equals(DimensionSemantics, other.DimensionSemantics)
            && Equals(PrintPreparationPlan, other.PrintPreparationPlan)
            && Equals(SizeSelection, other.SizeSelection)
            && Equals(TargetEdgePlan, other.TargetEdgePlan)
            && Equals(EnlargementAuthority, other.EnlargementAuthority)
            && Steps.SequenceEqual(other.Steps);
    }

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(SessionId);
        hash.Add(WorkflowType);
        hash.Add(OutputName);
        hash.Add(SessionState);
        hash.Add(HasDerivedRevision);
        hash.Add(LatestApprovedRevisionId);
        hash.Add(Dimensions);
        hash.Add(WhiteUnderbaseBranch);
        hash.Add(ApprovedPrintOutputCount);
        hash.Add(TrimMargin);
        hash.Add(BackgroundRemovalAuthority);
        hash.Add(DimensionSemantics);
        hash.Add(PrintPreparationPlan);
        hash.Add(SizeSelection);
        hash.Add(TargetEdgePlan);
        hash.Add(EnlargementAuthority);

        foreach (SessionStep step in Steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }

    /// <summary>Returns the step entry for <paramref name="kind"/>, or null when absent from this workflow.</summary>
    public SessionStep? Step(StepKind kind)
    {
        foreach (SessionStep step in Steps)
        {
            if (step.Step == kind)
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>
    /// The step the operator is expected to act on: the first that is neither Approved nor
    /// Skipped. Null when every step is finished and only <c>Complete</c> remains.
    /// </summary>
    public SessionStep? CurrentStep
    {
        get
        {
            foreach (SessionStep step in Steps)
            {
                if (!step.IsFinished)
                {
                    return step;
                }
            }

            return null;
        }
    }

    /// <summary>True when every step of the workflow is Approved or Skipped.</summary>
    public bool AllStepsFinished => CurrentStep is null;

    /// <summary>Creates the initial snapshot for a workflow, with every step Waiting.</summary>
    public static WorkflowSnapshot Create(
        SessionId sessionId,
        WorkflowType workflowType,
        OutputName outputName,
        DateTimeOffset nowUtc)
    {
        WorkflowDefinition definition = WorkflowCatalog.For(workflowType);
        List<SessionStep> steps = new(definition.Steps.Count);
        foreach (StepDefinition step in definition.Steps)
        {
            steps.Add(new SessionStep(
                step.Kind,
                step.Ordinal,
                StepState.Waiting,
                CurrentRevisionId: null,
                CurrentRevisionSha256: null,
                SkipReason: null,
                AttemptCount: 0,
                nowUtc));
        }

        return new WorkflowSnapshot(
            sessionId,
            workflowType,
            outputName,
            SessionState.Active,
            steps,
            HasDerivedRevision: false,
            LatestApprovedRevisionId: null,
            Dimensions: null,
            WhiteUnderbaseBranch: null,
            ApprovedPrintOutputCount: 0);
    }

    /// <summary>
    /// The most recent Revision offered by a step strictly before <paramref name="kind"/>.
    /// </summary>
    /// <remarks>
    /// This is what a step consumes as input. Skipped steps hold no Revision, so the search
    /// naturally falls through to the last step that actually produced one — which is exactly
    /// the rule "skipping a step makes the downstream input the last approved upstream
    /// Revision" (Epic 11100 plan §17.1), with no special case needed.
    /// </remarks>
    public RevisionId? UpstreamRevisionOf(StepKind kind)
    {
        RevisionId? found = null;
        foreach (SessionStep step in Steps)
        {
            if (step.Step == kind)
            {
                break;
            }

            if (step.CurrentRevisionId is not null)
            {
                found = step.CurrentRevisionId;
            }
        }

        return found;
    }

    /// <summary>
    /// The most recent Revision offered by a step strictly before <paramref name="kind"/>,
    /// together with the hash of the bytes that step is offering
    /// (Epic 11300 Part C2B1 §4).
    /// </summary>
    /// <remarks>
    /// The same search <see cref="UpstreamRevisionOf"/> performs, answering with both halves of
    /// the artefact identity instead of only the id. A reviewed-content authority has to be
    /// checked against both: an id alone still matches after the bytes underneath it changed,
    /// which is the one case §24 exists to refuse.
    /// <para>
    /// Null when no upstream result exists, and also when one exists without a recorded hash —
    /// the two are written together by every path that sets them, so a half-filled step is a
    /// state no authority should be matched against.
    /// </para>
    /// </remarks>
    public (RevisionId Id, Sha256 Sha256)? UpstreamResultOf(StepKind kind)
    {
        (RevisionId, Sha256)? found = null;
        foreach (SessionStep step in Steps)
        {
            if (step.Step == kind)
            {
                break;
            }

            if (step.CurrentRevisionId is RevisionId id && step.CurrentRevisionSha256 is Sha256 hash)
            {
                found = (id, hash);
            }
        }

        return found;
    }

    /// <summary>Returns a copy with <paramref name="replacement"/> substituted for its step.</summary>
    public WorkflowSnapshot WithStep(SessionStep replacement)
    {
        List<SessionStep> steps = new(Steps.Count);
        foreach (SessionStep step in Steps)
        {
            steps.Add(step.Step == replacement.Step ? replacement : step);
        }

        return this with { Steps = steps };
    }
}
