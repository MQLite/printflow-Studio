using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;

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

    /// <summary>Confirm the target physical dimensions.</summary>
    public sealed record SetPrintDimensions(PrintDimensions Dimensions) : WorkflowCommand;

    /// <summary>
    /// Record the operator's explicit white-underbase decision. Required before Photoshop
    /// output may start; the system never infers it (MVP design §12).
    /// </summary>
    public sealed record SelectWhiteUnderbaseBranch(
        WhiteUnderbaseBranch Branch,
        string Justification) : WorkflowCommand;

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
    }
}
