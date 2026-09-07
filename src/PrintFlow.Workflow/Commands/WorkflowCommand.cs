using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.Workflow.Commands;

/// <summary>
/// Everything that can legally be asked of a session.
/// </summary>
/// <remarks>
/// There is deliberately no <c>SetState(...)</c> and no <c>MoveToStep(...)</c>. State is
/// reachable only through a command whose legality the engine checks, which is how
/// MVP design invariant 12 ("the UI never directly changes state") becomes structural
/// rather than a convention.
///
/// The nested <see cref="System"/> commands are issued by the application layer when an
/// attempt finishes or when startup recovery finds a crashed attempt. They are not
/// operator commands and the UI cannot construct one: their constructors are internal to
/// this assembly, so a WPF view model has no way to synthesise "the adapter succeeded".
/// </remarks>
public abstract record WorkflowCommand
{
    private protected WorkflowCommand()
    {
    }

    /// <summary>A short stable name used in rejection messages and logs.</summary>
    public string Kind => GetType().Name;

    /// <summary>Choose the workflow. Legal only before any derived Revision exists.</summary>
    public sealed record SelectWorkflow(WorkflowType Type) : WorkflowCommand;

    /// <summary>Rename the produced files. Never renames the operator's source file.</summary>
    public sealed record SetOutputName(OutputName Name) : WorkflowCommand;

    /// <summary>Confirm the imported original and advance past OriginalConfirmation.</summary>
    public sealed record ConfirmOriginal(string? Notes = null) : WorkflowCommand;

    /// <summary>Begin an attempt for the given step.</summary>
    public sealed record StartStep(StepKind Step) : WorkflowCommand;

    /// <summary>Approve the result currently awaiting review, bound to the hash reviewed.</summary>
    public sealed record Approve(StepKind Step, Sha256 ReviewedHash, string? Notes = null) : WorkflowCommand;

    /// <summary>Reject the result currently awaiting review.</summary>
    public sealed record Reject(
        StepKind Step,
        Sha256 ReviewedHash,
        RejectionReason Reason,
        string? Notes = null) : WorkflowCommand;

    /// <summary>Return a rejected, failed or interrupted step to a state where a new attempt is legal.</summary>
    public sealed record Retry(StepKind Step) : WorkflowCommand;

    /// <summary>
    /// Crop <paramref name="Crop"/> out of the step's upstream Revision, because automatic
    /// trimming could not decide the rectangle (Epic 11200 Part C2 §12).
    /// </summary>
    /// <remarks>
    /// An operator command like any other, and emphatically <b>not</b> a
    /// <see cref="HandOff"/>: the work stays inside PrintFlow, the session stays
    /// <c>Active</c>, and the result goes through the same attempt, hash and review machinery
    /// every other produced file does. What it is not is a general image editor — the only
    /// geometry supplied is a rectangle and explicit outward manual margins.
    /// <para>
    /// <paramref name="Crop"/> is in the <b>source image's own pixel coordinates</b>. A
    /// viewport rectangle would mean the same drag produced a different crop depending on the
    /// window size, so the display-to-source mapping happens on the review surface and only
    /// its answer reaches a command (Part C2 §6, §7).
    /// </para>
    /// </remarks>
    public sealed record SubmitManualCrop(StepKind Step, TrimBounds Crop, ManualCropMargin Margin = default) : WorkflowCommand;

    /// <summary>Decline Trim and continue with its approved upstream artwork, without producing a result.</summary>
    public sealed record KeepOriginalExtent : WorkflowCommand
    {
        /// <summary>Stable persisted operator intent; display wording is localized separately.</summary>
        public const string Reason = "Operator chose to keep original extent";
    }

    /// <summary>Skip a skippable step. Creates no Revision.</summary>
    public sealed record Skip(StepKind Step, string? Reason = null) : WorkflowCommand
    {
        /// <summary>The default reason recorded when the operator gives none (MVP design §7.2).</summary>
        public const string DefaultReason = "File already satisfies this step";

        /// <summary>The reason actually recorded.</summary>
        public string EffectiveReason =>
            string.IsNullOrWhiteSpace(Reason) ? DefaultReason : Reason!.Trim();
    }

    /// <summary>Transfer the work to the operator. Ends automated progression for this session.</summary>
    public sealed record HandOff(StepKind Step, string Reason) : WorkflowCommand;

    /// <summary>
    /// Bring a handed-off session back under automation, explicitly (Epic 11300 Part D2A §22).
    /// </summary>
    /// <remarks>
    /// The whole of §22 in one command. A takeover ends automation, and re-entry must be a
    /// thing the operator <i>does</i> rather than something that happens because they reopened
    /// the app or pressed Run again — so <c>SessionState.HandedOff</c> refuses every ordinary
    /// progression command (<see cref="Engine.SessionStateRules.AllowsProgress"/>), and this is
    /// the only command that lifts it.
    /// <para>
    /// It creates no attempt and no Revision. What it does is return the session to
    /// <c>Active</c> and put the handed-off step back to <c>Waiting</c>, from which the ordinary
    /// <see cref="StartStep"/> path produces a <b>new</b> attempt against a fresh working copy
    /// with the usual safe-state verification. The handed-off attempt is untouched: it is a
    /// closed row, and it stays in the history exactly as it was written (§15, §23).
    /// </para>
    /// <para>
    /// It adopts nothing. There is no payload naming a file, no folder scan and no filename
    /// inference anywhere on this path, so whatever the operator did in the external application
    /// while they owned it cannot become this session's output by re-entering (§23).
    /// </para>
    /// </remarks>
    public sealed record ReenterAutomation : WorkflowCommand;

    /// <summary>SCRUM-11092 / SCRUM-11112: import a result after explicit manual handoff.</summary>
    public sealed record SubmitManualResult(StepKind Step, string SelectedPath) : WorkflowCommand;

    /// <summary>
    /// Confirm typed maximum bounds. The custom fit-box route only
    /// (Epic 11400 Part B1A.2D §3, §19).
    /// </summary>
    /// <remarks>
    /// A named preset is refused here rather than accepted with whatever millimetres the caller
    /// supplied. Under v1.11.0 the executable limit for a named size comes from the configured
    /// preset and from nowhere else, so "A4" arriving with a pair of millimetres attached is a
    /// caller having decided what A4 means — which is exactly what §3 removes. Named sizes go
    /// through <see cref="SetPresetFitSize"/>.
    /// </remarks>
    public sealed record SetPrintDimensions(PrintDimensions Dimensions) : WorkflowCommand;

    /// <summary>
    /// Choose a named preset and take its configured recommendation
    /// (Epic 11400 Part B1A.2D §3, §15, §17).
    /// </summary>
    /// <remarks>
    /// It carries the preset and nothing else, and that is the point: the millimetres are not the
    /// caller's to supply. <c>SessionService</c> resolves the recommendation from
    /// <see cref="Ports.IWorkstationPresetProvider.GetPrintSizeRecommendations"/> and fits it with
    /// <c>FitWithinBounds</c>, so a screen cannot record an A4 job at the ISO paper size, and a
    /// stale build cannot record one at last version's limit (§3, §4).
    /// <para>
    /// Ordinary preset use asks for no axis and no resampling method, because neither is the
    /// operator's under the accepted contract — which is why there is nothing else on this
    /// command to fill in.
    /// </para>
    /// </remarks>
    public sealed record SetPresetFitSize(SizePreset Preset) : WorkflowCommand;

    /// <summary>
    /// Record one exact operator-chosen physical edge, optionally as an explicit override of a
    /// named preset's recommendation (Epic 11400 Part B1A.2D §6, §15).
    /// </summary>
    /// <remarks>
    /// Exactly one edge and exactly one millimetre value: a second authoritative dimension is not
    /// representable, and Photoshop derives the other edge with proportions constrained.
    /// <para>
    /// <paramref name="Millimetres"/> is <see cref="decimal"/> rather than <c>double</c>, and that
    /// is the operator's number carried intact. The accepted target-edge calculation is exact —
    /// it converts this decimal to a rational and rounds on integer remainders — so passing it
    /// through binary floating point on the way in would decide midpoint cases somewhere other
    /// than where the contract decides them (§7).
    /// </para>
    /// <para>
    /// <paramref name="OverriddenPreset"/> names the recommendation being set aside, or is null
    /// for an ordinary custom size. It records <i>that</i> the operator went past a recommendation
    /// and which one; it is emphatically <b>not</b> permission to enlarge. A 320 mm override of a
    /// 280 mm recommendation over a large enough source exceeds the preset and adds no pixels at
    /// all — the two decisions stay separate, and only <see cref="AuthoriseEnlargement"/> grants
    /// the second (§11).
    /// </para>
    /// </remarks>
    public sealed record SetCustomTargetEdgeSize(
        TargetEdge Edge,
        decimal Millimetres,
        SizePreset? OverriddenPreset = null) : WorkflowCommand;

    /// <summary>
    /// Record the operator's explicit permission to enlarge past what the source holds at the
    /// production resolution (Epic 11400 Part B1A.2D §9, §10).
    /// </summary>
    /// <remarks>
    /// A <b>second</b> confirmation, never a by-product of recording a size. Recording a target
    /// that happens to need more pixels than exist is one act; agreeing to synthesise those
    /// pixels is another, and the accepted contract requires the operator to make it knowingly
    /// (§10).
    /// <para>
    /// The payload is what the operator was looking at when they agreed —
    /// <paramref name="ReviewedRevisionId"/>, <paramref name="DisplayedHash"/>,
    /// <paramref name="Edge"/> and <paramref name="Millimetres"/> — and the command is accepted
    /// only when that is still the plan on offer. So "I agreed to enlarge this photo to 320 mm"
    /// can never quietly become "enlargement is on for this session", exactly as
    /// <see cref="SetBackgroundRemovalDecision"/> refuses to become a setting (§9).
    /// </para>
    /// <para>
    /// It is a decision and not an attempt: accepting one starts nothing, produces no file, and
    /// creates no Revision.
    /// </para>
    /// </remarks>
    public sealed record AuthoriseEnlargement(
        RevisionId ReviewedRevisionId,
        Sha256 DisplayedHash,
        TargetEdge Edge,
        decimal Millimetres) : WorkflowCommand;

    /// <summary>
    /// Record the operator's explicit white-underbase decision. Required before Photoshop
    /// output may start; the system never infers it (MVP design §12).
    /// </summary>
    public sealed record SelectWhiteUnderbaseBranch(
        WhiteUnderbaseBranch Branch,
        string Justification) : WorkflowCommand;

    /// <summary>
    /// Record the trim margin the next deterministic Trim attempt will run with
    /// (Epic 11200 Part C3 §13).
    /// </summary>
    /// <remarks>
    /// Exists so the operator's margin reaches <see cref="Ports.ITrimProcessor"/> the same way
    /// every other operator decision reaches the work that consumes it: as a command the engine
    /// checks and the application layer persists. A view model handed a margin straight to the
    /// processor would be pixel work with no attempt row, no legality check and nothing to
    /// audit afterwards — the same side door <see cref="SubmitManualCrop"/> exists to avoid.
    /// <para>
    /// It is a <b>decision</b> and not an attempt: accepting one starts nothing, produces no
    /// file, and creates no Revision. <see cref="StartStep"/> for Trim then reads the recorded
    /// value, so what runs is always what was last recorded rather than whatever a screen
    /// happened to be holding.
    /// </para>
    /// <para>
    /// <paramref name="Margin"/> is already validated by construction —
    /// <see cref="TrimMargin"/>'s factories refuse a negative pixel count outright — so this
    /// command cannot carry a margin the domain would reject (§11).
    /// </para>
    /// </remarks>
    public sealed record SetTrimParameters(TrimMargin Margin) : WorkflowCommand;

    /// <summary>
    /// Record the operator's explicit authority for Meitu's automatic selection over the
    /// reviewed content Background Removal is about to consume (Epic 11300 Part C2B1 §5).
    /// </summary>
    /// <remarks>
    /// The command exists because the authority is a <b>product decision about specific
    /// content</b>, not a setting. <paramref name="ReviewedRevisionId"/> and
    /// <paramref name="DisplayedHash"/> are what the operator was actually looking at when they
    /// decided, and the engine accepts the decision only when that is still the artefact
    /// Background Removal will consume — so "I authorised automatic selection for this photo"
    /// can never quietly become "automatic selection is on for this session" (§4, §6).
    /// <para>
    /// It is a <b>decision</b> and not an attempt: accepting one starts nothing, produces no
    /// file, and creates no Revision. <see cref="StartStep"/> for Background Removal then
    /// requires it, so what runs is always something a human authorised over content they saw.
    /// </para>
    /// <para>
    /// <paramref name="Decision"/> must be an explicit authorisation.
    /// <c>BackgroundRemovalDecision.Unspecified</c> is the absence of a decision, and a command
    /// carrying it is refused rather than recorded — recording it would leave a row that reads
    /// like a decision and authorises nothing (§7).
    /// </para>
    /// <para>
    /// The reviewed hash goes through the existing exact-hash authority — the same pair
    /// <see cref="Approve"/> binds to — rather than any new file hashing path. Nothing here
    /// opens a file (§4).
    /// </para>
    /// </remarks>
    public sealed record SetBackgroundRemovalDecision(
        BackgroundRemovalDecision Decision,
        RevisionId ReviewedRevisionId,
        Sha256 DisplayedHash) : WorkflowCommand;

    /// <summary>Go back to an earlier step, invalidating everything derived from it.</summary>
    public sealed record ReturnToStep(StepKind Target) : WorkflowCommand;

    /// <summary>Finish the session. Legal only when every required condition is met.</summary>
    public sealed record Complete : WorkflowCommand;

    /// <summary>Reopen a completed production session to produce another output size.</summary>
    public sealed record AddAnotherSize : WorkflowCommand;

    /// <summary>Abandon the session. Files are retained.</summary>
    public sealed record AbandonSession(string Reason) : WorkflowCommand;

    /// <summary>
    /// Commands the application layer raises on the session's behalf. Never operator input.
    /// </summary>
    public abstract record System : WorkflowCommand
    {
        private protected System()
        {
        }

        /// <summary>An attempt produced a validated Revision.</summary>
        public sealed record AttemptSucceeded : System
        {
            internal AttemptSucceeded(
                AttemptId attemptId,
                StepKind step,
                RevisionId outputRevision,
                Sha256 outputHash)
            {
                AttemptId = attemptId;
                Step = step;
                OutputRevision = outputRevision;
                OutputHash = outputHash;
            }

            public AttemptId AttemptId { get; }

            public StepKind Step { get; }

            public RevisionId OutputRevision { get; }

            /// <summary>
            /// The hash of the validated output. Computing it is the readability proof, so a
            /// value here means the file existed, was fully readable, and was hashed
            /// (Epic 11100 plan §10.1).
            /// </summary>
            public Sha256 OutputHash { get; }
        }

        /// <summary>An attempt failed with a structured failure. No Revision was created.</summary>
        public sealed record AttemptFailed : System
        {
            internal AttemptFailed(AttemptId attemptId, StepKind step, OperationFailure failure)
            {
                AttemptId = attemptId;
                Step = step;
                Failure = failure;
            }

            public AttemptId AttemptId { get; }

            public StepKind Step { get; }

            public OperationFailure Failure { get; }
        }

        /// <summary>Startup recovery found an attempt that was still running when the app stopped.</summary>
        public sealed record AttemptInterrupted : System
        {
            internal AttemptInterrupted(AttemptId attemptId, StepKind step)
            {
                AttemptId = attemptId;
                Step = step;
            }

            public AttemptId AttemptId { get; }

            public StepKind Step { get; }
        }

        /// <summary>
        /// A human stopped a running attempt: an operator Stop, or an operator taking the
        /// external application over (Epic 11300 Part D2A §12, §19).
        /// </summary>
        /// <remarks>
        /// A separate command from <see cref="AttemptFailed"/> because the two are separate
        /// claims about what happened, and conflating them would make the history unreadable in
        /// the direction that matters. <c>AttemptFailed</c> asserts that automation ran and did
        /// not produce a valid result — a real production event, and the thing a step's Failed
        /// state is about. A stop asserts that a person ended the run; nothing failed, and the
        /// step's own state afterwards is <c>Interrupted</c>, the state meaning "this did not
        /// finish" (§12).
        /// <para>
        /// It carries an <see cref="OperationFailure"/> all the same, and that is not a
        /// contradiction: <c>OperationFailure</c> is this codebase's structured "why did this
        /// not produce anything" record, and it is where §29's audit lives — which mode was
        /// requested, whether a signed cancel was invoked, and what the external application may
        /// still be holding. The code on it is <c>FailureCode.Cancelled</c>.
        /// </para>
        /// <para>
        /// Like every other <see cref="System"/> command, the UI cannot construct one: a screen
        /// that could synthesise "this attempt was cancelled" could close an attempt without a
        /// run having stopped.
        /// </para>
        /// </remarks>
        public sealed record AttemptCancelled : System
        {
            internal AttemptCancelled(AttemptId attemptId, StepKind step, OperationFailure failure)
            {
                AttemptId = attemptId;
                Step = step;
                Failure = failure;
            }

            public AttemptId AttemptId { get; }

            public StepKind Step { get; }

            /// <summary>Why the run stopped, and what it left behind. Never null.</summary>
            public OperationFailure Failure { get; }
        }
    }
}
