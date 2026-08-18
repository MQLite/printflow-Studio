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
}
