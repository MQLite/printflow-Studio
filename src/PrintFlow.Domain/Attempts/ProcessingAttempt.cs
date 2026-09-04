using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.Domain.Attempts;

/// <summary>The outcome of one attempt.</summary>
public enum AttemptStatus
{
    /// <summary>The attempt has started. A row in this state at startup means the app crashed.</summary>
    Running,

    Succeeded,
    Failed,

    /// <summary>
    /// The attempt did not finish and produced nothing, because the run itself stopped.
    /// </summary>
    /// <remarks>
    /// Written by startup recovery for an attempt whose process died mid-run (Epic 11300
    /// Part D1). It is deliberately <i>not</i> what an operator Stop produces — see
    /// <see cref="Cancelled"/> — so "the computer stopped" and "a human stopped it" stay
    /// distinguishable in the history.
    /// </remarks>
    Interrupted,

    /// <summary>
    /// A human ended the attempt: an operator Stop, or an operator taking Meitu over
    /// (Epic 11300 Part D2A §12, §19).
    /// </summary>
    /// <remarks>
    /// Never carries an output Revision, which the database enforces as well as this type does
    /// — the <c>ProcessingAttempt</c> CHECK admits an <c>OutputRevisionId</c> only for
    /// <c>SUCCEEDED</c>. That is what makes §12's rule structural: a Meitu operation that
    /// cancelled cleanly is still an attempt that produced nothing, and there is no such thing
    /// as a "cancelled Revision" for it to point at.
    /// <para>
    /// Why an operator's Stop and their Take Over share one status rather than having one each:
    /// both are "a human ended this attempt without a result", and what separates them is not
    /// the attempt's outcome but what happens to the external application afterwards. That
    /// difference is recorded where it belongs — in <see cref="ProcessingAttempt.Failure"/>'s
    /// structured context and in <see cref="ProcessingAttempt.AdapterNotes"/> — rather than by
    /// splitting a lifecycle value in two (§29).
    /// </para>
    /// </remarks>
    Cancelled,
}

/// <summary>
/// One automated processing or manual-handoff attempt (MVP design §5.4).
/// </summary>
/// <remarks>
/// Attempts and Revisions are separate concepts, and that separation is what makes a crash
/// detectable: the row is written before the work starts. <see cref="OutputRevisionId"/> is
/// non-null only when the attempt succeeded — design invariant 4 made structural, and
/// enforced again by a database CHECK in Task 11108.
///
/// Retries chain through <see cref="RetryOfAttemptId"/> so a step's failure history stays
/// queryable rather than being overwritten.
/// </remarks>
public sealed record ProcessingAttempt(
    AttemptId Id,
    SessionId SessionId,
    StepKind Step,
    RevisionId? InputRevisionId,
    OperationKind Operation,
    string AdapterId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    AttemptStatus Status,
    RevisionId? OutputRevisionId,
    OperationFailure? Failure,
    AttemptId? RetryOfAttemptId,
    int RetrySequence)
{
    public PrintFlow.Domain.Files.PsdInspection? PsdInspection { get; init; }
    public PrintFlow.Domain.Files.PdfInspection? PdfInspection { get; init; }

    /// <summary>Starts an attempt. The output Revision, if any, is attached on success.</summary>
    public static ProcessingAttempt Start(
        AttemptId id,
        SessionId sessionId,
        StepKind step,
        RevisionId? inputRevisionId,
        OperationKind operation,
        string adapterId,
        DateTimeOffset startedAtUtc,
        AttemptId? retryOfAttemptId = null,
        int retrySequence = 0) =>
        new(id,
            sessionId,
            step,
            inputRevisionId,
            operation,
            adapterId,
            startedAtUtc,
            EndedAtUtc: null,
            AttemptStatus.Running,
            OutputRevisionId: null,
            Failure: null,
            retryOfAttemptId,
            retrySequence);

    /// <summary>
    /// The trim margin this attempt ran with, when it ran the deterministic trim
    /// (Epic 11200 Part C3 §14).
    /// </summary>
    /// <remarks>
    /// The answer to "how was this Trim Revision produced?", recorded on the attempt rather
    /// than only on the session. A session-level setting answers "what would the <i>next</i>
    /// run use", which is a different question and one that changes: an operator who rejects a
    /// uniform 4&#160;px trim and re-runs at 1/2/3/4 would, under a session-only record, have
    /// retrospectively rewritten what the first attempt did. An attempt row is written once and
    /// never rewritten, so both settings stay readable side by side (§15).
    /// <para>
    /// Null for everything that is not a deterministic trim — an adapter call, a promotion, and
    /// specifically a manual crop, whose rectangle the operator drew and to which an automatic
    /// margin means nothing (§17). Null therefore reads as "this attempt had no trim margin",
    /// never as "it used the default".
    /// </para>
    /// <para>
    /// An <c>init</c> property rather than a positional parameter, so every existing call site
    /// keeps saying what it meant: an attempt carries trim parameters only when something
    /// deliberately attaches them.
    /// </para>
    /// </remarks>
    public TrimMargin? TrimParameters { get; init; }

    /// <summary>Records the trim margin this attempt is about to run with.</summary>
    public ProcessingAttempt WithTrimParameters(TrimMargin margin) =>
        this with { TrimParameters = margin };

    /// <summary>
    /// The rectangles the deterministic trim established when this attempt produced a cropped
    /// file (SCRUM-11081).
    /// </summary>
    /// <remarks>
    /// The answer to "what did the alpha scan actually find, and what was actually cropped
    /// out?" — the crop geometry itself, next to the <see cref="TrimParameters"/> that shaped
    /// it. Together the two say everything about how this Trim Revision was produced: the
    /// detected extent, the operator's margin, and the rectangle those two combined into.
    /// <para>
    /// On the attempt, and for the same reason <see cref="TrimParameters"/> is: an operator who
    /// re-runs at a wider margin produces a second attempt with the same content rectangle and
    /// a different applied one, and a session-level record would have retrospectively rewritten
    /// what the first attempt cropped. Attempt rows are written once, so both readings stay
    /// side by side.
    /// </para>
    /// <para>
    /// Null for everything that is not a produced automatic trim — an adapter call, a
    /// promotion, a manual crop whose rectangle a human drew, and a trim that ended in
    /// <c>ManualCropRequired</c>, where no content was detected and nothing was cropped. Null
    /// therefore reads as "this attempt established no trim geometry", never as "the whole
    /// canvas was kept"; the whole canvas is itself a rectangle this type can state.
    /// </para>
    /// <para>
    /// Written by the attempt's closing transaction rather than its opening one, because it is
    /// the processor's result and does not exist until the pixel work has run. That is the one
    /// difference from <see cref="TrimParameters"/>, and it is why the attempt upsert must
    /// carry these columns in its update clause as well as its insert.
    /// </para>
    /// </remarks>
    public TrimGeometry? TrimGeometry { get; init; }

    /// <summary>Records the geometry the trim this attempt just ran established.</summary>
    public ProcessingAttempt WithTrimGeometry(TrimGeometry geometry) =>
        this with { TrimGeometry = geometry };

    /// <summary>
    /// The reviewed-content authority this attempt ran Background Removal under
    /// (Epic 11300 Part C2B1 §11).
    /// </summary>
    /// <remarks>
    /// The answer to "who authorised automatic selection over this content, and which content
    /// was it?" — recorded on the attempt rather than only on the session, for the same reason
    /// <see cref="TrimParameters"/> is. A session-level record answers "what would the
    /// <i>next</i> run be allowed to do", and that value changes: an operator who rejects a
    /// cutout, returns upstream and authorises different content would, under a session-only
    /// record, have retrospectively rewritten what the first attempt was authorised to do. An
    /// attempt row is written once and never rewritten, so both readings stay side by side
    /// (§18).
    /// <para>
    /// Null for everything that is not an authorised Background Removal — an Enhancement call,
    /// a promotion, a trim, a manual crop. Null therefore reads as "this attempt had no
    /// background-removal authority", never as "it used the default"; there is no default.
    /// </para>
    /// <para>
    /// An <c>init</c> property rather than a positional parameter, so every existing call site
    /// keeps saying what it meant.
    /// </para>
    /// </remarks>
    public BackgroundRemovalAuthority? BackgroundRemovalAuthority { get; init; }

    /// <summary>
    /// Adapter-supplied completion evidence and cleanup warnings for a successful attempt.
    /// </summary>
    /// <remarks>
    /// Kept on the attempt rather than the Revision because it describes the runtime that
    /// produced the Revision, including a non-fatal inability to return an external application
    /// to neutral state. Null for failed/interrupted attempts and for processors with nothing
    /// useful to add.
    /// </remarks>
    public string? AdapterNotes { get; init; }

    /// <summary>Records the reviewed-content authority this attempt is about to run under.</summary>
    public ProcessingAttempt WithBackgroundRemovalAuthority(BackgroundRemovalAuthority authority) =>
        this with { BackgroundRemovalAuthority = authority };

    /// <summary>
    /// The exact source-bound geometry this attempt produced its output under
    /// (Epic 11400 Part B1A.2A §12; Part B1A.2D §24).
    /// </summary>
    /// <remarks>
    /// The answer to "which fit box, which source, and which single edge produced this TIFF?" —
    /// recorded on the attempt rather than only on the session, for the same reason
    /// <see cref="TrimParameters"/> and <see cref="BackgroundRemovalAuthority"/> are. A
    /// session-level record answers "what would the <i>next</i> run do", and that value changes:
    /// an operator who rejects an output, returns upstream and records different limits would,
    /// under a session-only record, have retrospectively relabelled what the first attempt did.
    /// An attempt row is written once and never rewritten — the upsert leaves these columns out
    /// of its <c>DO UPDATE</c> clause — so both readings stay side by side.
    /// <para>
    /// Null for everything that is not a Photoshop output — a Meitu call, a trim, a manual crop,
    /// a promotion. Null therefore reads as "this attempt had no preparation plan", never as "it
    /// used the default"; there is no default, and the workflow refuses to start Photoshop
    /// output without a currently usable plan (§15).
    /// </para>
    /// <para>
    /// It is the <see cref="PhotoshopPreparation"/> union rather than one of the two plan types,
    /// so an attempt made under either accepted sizing contract is audited in the shape it was
    /// actually made — including, for an enlargement, the exact
    /// <see cref="EnlargementAuthority"/> it ran under. A later size change or a later
    /// enlargement decision cannot relabel this row (Part B1A.2D §24).
    /// </para>
    /// <para>
    /// An <c>init</c> property rather than a positional parameter, so every existing call site
    /// keeps saying what it meant.
    /// </para>
    /// </remarks>
    public PhotoshopPreparation? Preparation { get; init; }

    /// <summary>Records the resolved geometry this attempt is about to run with.</summary>
    public ProcessingAttempt WithPreparation(PhotoshopPreparation preparation) =>
        this with { Preparation = preparation };

    public ProcessingAttempt Succeed(
        RevisionId outputRevisionId, DateTimeOffset endedAtUtc, string? adapterNotes = null) =>
        this with
        {
            Status = AttemptStatus.Succeeded,
            OutputRevisionId = outputRevisionId,
            EndedAtUtc = endedAtUtc,
            AdapterNotes = adapterNotes,
        };

    public ProcessingAttempt Fail(OperationFailure failure, DateTimeOffset endedAtUtc) =>
        this with { Status = AttemptStatus.Failed, Failure = failure, EndedAtUtc = endedAtUtc };

    public ProcessingAttempt Interrupt(DateTimeOffset endedAtUtc) =>
        this with { Status = AttemptStatus.Interrupted, EndedAtUtc = endedAtUtc };

    /// <summary>
    /// Ends the attempt because a human stopped it, recording why and what was left behind
    /// (Epic 11300 Part D2A §12, §29).
    /// </summary>
    /// <remarks>
    /// Takes <paramref name="adapterNotes"/>, which <see cref="Fail"/> and
    /// <see cref="Interrupt"/> do not, and the asymmetry is deliberate. A stop is the one
    /// non-success whose <i>external</i> consequences an operator has to be told about: whether
    /// a signed cancel was actually invoked, whether the application positively left Busy, and
    /// whether it may still be holding a processed result. None of that fits in a failure code,
    /// and all of it is exactly what the adapter observed.
    /// <para>
    /// <see cref="OutputRevisionId"/> is left null by construction — the record is copied with
    /// a status change and nothing else — so §12's "no cancelled Revision" holds here for the
    /// same reason it holds for a failure.
    /// </para>
    /// </remarks>
    public ProcessingAttempt Cancel(
        OperationFailure failure, DateTimeOffset endedAtUtc, string? adapterNotes = null) =>
        this with
        {
            Status = AttemptStatus.Cancelled,
            Failure = failure,
            EndedAtUtc = endedAtUtc,
            AdapterNotes = adapterNotes,
        };

    /// <summary>True when this attempt legitimately carries an output Revision.</summary>
    public bool ProducedRevision => Status == AttemptStatus.Succeeded && OutputRevisionId is not null;
}
