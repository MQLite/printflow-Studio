namespace PrintFlow.Domain.Results;

/// <summary>
/// Stable, internal, English failure codes. Persisted as TEXT and never localised
/// (MVP design §13.4 — internal states and error codes retain stable English names).
/// </summary>
/// <remarks>
/// Adding a value is a product decision: every code must map to an operator-facing
/// message key and, where applicable, a recovery action. Values are never renumbered
/// because they are written to the database as names.
/// </remarks>
public enum FailureCode
{
    /// <summary>The required adapter is not installed, not configured, or disabled.</summary>
    AdapterUnavailable,

    /// <summary>The workstation environment has not been verified (Epic 11500 gate).</summary>
    EnvironmentNotVerified,

    /// <summary>The signed workstation preset did not match its expected SHA-256.</summary>
    PresetHashMismatch,

    /// <summary>An unrecognised dialog or screen was encountered; automation must stop.</summary>
    UnknownDialog,

    /// <summary>The operation did not reach an observable completed state in time.</summary>
    Timeout,

    /// <summary>The operator or the application cancelled the operation.</summary>
    Cancelled,

    /// <summary>The expected output file does not exist.</summary>
    OutputMissing,

    /// <summary>The output file exists but could not be read completely.</summary>
    OutputUnreadable,

    /// <summary>The output was readable but failed its structural or production validation.</summary>
    OutputValidationFailed,

    /// <summary>A file's current bytes no longer match the SHA-256 recorded for its Revision.</summary>
    RevisionIntegrityMismatch,

    /// <summary>A workspace operation (create, copy, move, recycle) failed.</summary>
    WorkspaceError,

    /// <summary>A metadata read or write failed.</summary>
    PersistenceError,

    /// <summary>A required precondition of the requested operation was not satisfied.</summary>
    PreconditionNotMet,

    /// <summary>
    /// Deterministic trimming established no usable alpha content, so no automatic crop is
    /// honest and the operator must crop manually (Epic 11200 Part B §10).
    /// </summary>
    /// <remarks>
    /// Never retryable: the same bytes deterministically produce the same answer, so offering
    /// Retry would invite the operator to keep pressing a button that cannot succeed. The
    /// recovery action is a human crop, whose re-entry surface is Epic 11200 Part C.
    /// </remarks>
    ManualCropRequired,

    /// <summary>
    /// The Meitu executable named by the signed workstation preset is absent, unreadable, or
    /// does not match the accepted binary identity (Epic 11300 Part A §7).
    /// </summary>
    /// <remarks>
    /// Never retryable: pressing Retry cannot install a binary or change its hash. The recovery
    /// action is an environment repair followed by proportionate preset revalidation.
    /// </remarks>
    MeituNotInstalled,

    /// <summary>
    /// Meitu was started from the accepted executable but never reached an identifiable window
    /// in a recognised safe state before the bounded launch timeout expired.
    /// </summary>
    MeituLaunchFailed,

    /// <summary>
    /// A Meitu process exists, but no top-level window belonging to that exact process could be
    /// identified — so there is no target any interaction may be addressed to.
    /// </summary>
    MeituWindowNotFound,

    /// <summary>
    /// The verified Meitu window stopped being the interaction target — most often because
    /// another application took the foreground — so the pending input was abandoned unsent.
    /// </summary>
    /// <remarks>
    /// This code exists to make the Epic 11300 Part A §3 rule observable rather than merely
    /// intended: PrintFlow re-verifies the target immediately before every keystroke and every
    /// invoke, and reports this instead of sending input to whatever happens to hold focus.
    /// </remarks>
    MeituTargetLost,

    /// <summary>
    /// Meitu is on a screen PrintFlow cannot positively recognise. Automation stops; nothing is
    /// clicked, dismissed or closed on the strength of a guess (MVP design §11.4).
    /// </summary>
    MeituUnknownState,

    /// <summary>
    /// A dialog owned by Meitu is blocking the main window. PrintFlow never guesses how to
    /// dismiss one — the operator resolves it (MVP design §11.4).
    /// </summary>
    MeituBlockingDialog,

    /// <summary>
    /// The prepared working copy could not be handed to Meitu through a positively identified
    /// control, so no file was opened.
    /// </summary>
    MeituOpenInputFailed,

    /// <summary>
    /// The Photoshop executable named by the signed workstation preset is absent, unreadable,
    /// or does not match the accepted binary identity (Epic 11400 Part A §5).
    /// </summary>
    /// <remarks>
    /// Deliberately a separate code from <see cref="MeituNotInstalled"/> rather than a shared
    /// "external application missing". The two have different accepted binaries, different
    /// evidence chains and different repair procedures, and an operator reading a failure needs
    /// to know which application to look at without parsing the message text.
    /// </remarks>
    PhotoshopNotInstalled,

    /// <summary>
    /// Photoshop was started from the accepted executable but never reached an identifiable
    /// window in a recognised safe state before the bounded launch timeout expired.
    /// </summary>
    PhotoshopLaunchFailed,

    /// <summary>
    /// A Photoshop process exists, but no top-level window belonging to that exact process
    /// could be identified — so there is no target any interaction may be addressed to.
    /// </summary>
    PhotoshopWindowNotFound,

    /// <summary>
    /// The verified Photoshop window stopped being the interaction target — most often because
    /// another application took the foreground — so the pending input was abandoned unsent.
    /// </summary>
    PhotoshopTargetLost,

    /// <summary>
    /// Photoshop is on a screen PrintFlow cannot positively recognise. Automation stops;
    /// nothing is clicked, dismissed or closed on the strength of a guess.
    /// </summary>
    PhotoshopUnknownState,

    /// <summary>
    /// A dialog owned by Photoshop is blocking its main window. PrintFlow never guesses how to
    /// dismiss one — the operator resolves it.
    /// </summary>
    PhotoshopBlockingDialog,

    /// <summary>
    /// The managed Working file could not be handed to Photoshop through a positively
    /// identified control, so no file was opened.
    /// </summary>
    PhotoshopOpenInputFailed,

    /// <summary>
    /// Photoshop is holding a document, but PrintFlow could not positively establish that it is
    /// the exact managed Working file this attempt handed over (Epic 11400 Part A §11, §12).
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="PhotoshopUnknownState"/> on purpose, because the two describe
    /// opposite problems. Unknown state means PrintFlow does not recognise the screen at all.
    /// This means the screen was recognised perfectly well and the document on it is not — or
    /// cannot be shown to be — the one that was asked for, which is exactly the condition that
    /// must never be allowed to continue into an irreversible production step.
    /// </remarks>
    PhotoshopDocumentIdentityUnconfirmed,

    /// <summary>The inspected PSD mode/depth or existing ink channels cannot be prepared safely.</summary>
    PsdUnsupported,
    /// <summary>No positive Photoshop-compatible composite evidence exists in this PSD.</summary>
    PsdCompositeMissing,
    /// <summary>The PSD container cannot be read safely.</summary>
    PsdUnreadable,
    /// <summary>PSD preparation did not produce a verified full-canvas raster.</summary>
    PsdPreparationFailed,
}
