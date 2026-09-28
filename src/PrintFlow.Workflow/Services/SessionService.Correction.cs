using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// Asking a colleague to correct a background-removal result, and importing their correction
/// (SCRUM-11148; design §6, addendum §2–§3).
/// </summary>
/// <remarks>
/// Three dedicated entries, each taking the session gate exactly once and calling the under-gate
/// core, never <see cref="ExecuteAsync"/>. They are the only places a <see cref="CorrectionContext"/>
/// is built, and the context is the only thing that lets <see cref="ExecuteCoreAsync"/> accept either
/// correction command. No gate is held while a person or the file picker is involved, and no
/// automation lock is taken for any of this work.
/// </remarks>
public sealed partial class SessionService
{
    /// <summary>
    /// The stable reason recorded when a correction-bound import did not finish but its request no
    /// longer matches (addendum §3.2 fail-closed rule).
    /// </summary>
    internal const string CorrectionImportUnfinishedReason = CorrectionClosing.UnfinishedReason;

    /// <summary>The optional instructions file written beside the two copies.</summary>
    internal const string CorrectionInstructionsFileName = "Instructions.txt";

    /// <summary>The session sub-folder packages live in: outside Working\, so no cleanup reaches it.</summary>
    private const string CorrectionArea = "Correction";

    private const int MaxCorrectionNoteLength = 1000;
    private const int MaxCorrectionNameStem = 40;

    /// <summary>What a dedicated correction entry is allowed to do in the core.</summary>
    private enum CorrectionPurpose
    {
        Prepare,
        Import,
    }

    /// <summary>
    /// The internal authority for one correction command, built only by the dedicated entries after
    /// they have checked the request row. A display flag, a file name, reason text or a known request
    /// id is never a substitute for it.
    /// </summary>
    private sealed record CorrectionContext(Guid RequestId, CorrectionPurpose Purpose, Sha256? SelectedHash = null);

    /// <inheritdoc />
    public async Task<OperationResult<SessionView>> RejectExactReviewAsync(
        SessionId id, StepKind step, RevisionId revision, Sha256 reviewedHash,
        RejectionReason reason, string? notes, string? operatorName, CancellationToken cancellationToken)
    {
        // One gate acquisition covers the identity check and the ordinary Reject core, exactly as
        // ApproveExactReviewAsync does.
        using IDisposable completionLease = await SessionCompletionGate.EnterAsync(id, cancellationToken);
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure) return OperationResult.Fail<SessionView>(loaded.Failure);
        SessionStep? current = loaded.Value?.Steps.SingleOrDefault(s => s.Step == step);
        if (current is null || current.CurrentRevisionId != revision || current.CurrentRevisionSha256 != reviewedHash)
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet,
                "The result on screen is no longer the step's current result.");
        return await ExecuteCoreAsync(id, new WorkflowCommand.Reject(step, reviewedHash, reason, notes), operatorName,
            expectedFailureAttemptId: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SessionView>> RequestColleagueCorrectionAsync(
        SessionId id, Guid requestId, RevisionId reviewedRevision, Sha256 reviewedHash, string? note,
        CorrectionFileNaming naming, string? operatorName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(naming);
        if (_correctionPackages is null)
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, "Colleague correction is not available here.");
        if (requestId == Guid.Empty)
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, "A correction request needs its own identity.");
        if (note is { Length: > MaxCorrectionNoteLength })
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet,
                $"The note is longer than {MaxCorrectionNoteLength} characters.");

        using IDisposable lease = await SessionCompletionGate.EnterAsync(id, cancellationToken);
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure) return OperationResult.Fail<SessionView>(loaded.Failure);
        if (loaded.Value is not { } aggregate)
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, $"No session {id} exists.");

        WorkflowSnapshot snapshot = aggregate.ToSnapshot(ConfiguredRecommendations());
        CommandContext context = CommandContext.Create(_timeProvider, _idGenerator, operatorName);

        // 1. Resolve which row this call is about. A repeat of the same intent never makes a
        //    second package, and a retry is never permission to retarget an old request.
        List<CorrectionRequestChange> opening = [];
        CorrectionRequest request;
        CorrectionRequest? existing = aggregate.CorrectionRequests.SingleOrDefault(r => r.Id == requestId);
        if (existing is not null)
        {
            if (existing.Status == CorrectionRequestStatus.Ready && aggregate.Session.State == SessionState.HandedOff)
                return await LoadAsync(id, cancellationToken);
            if (existing.Status != CorrectionRequestStatus.Preparing ||
                existing.HandedOutRevisionId != reviewedRevision || existing.HandedOutSha256 != reviewedHash)
                return NoLongerAvailable();
            request = existing;
        }
        else
        {
            CorrectionRequest? open = aggregate.CorrectionRequests.SingleOrDefault(r => r.IsOpen);
            if (open is { Status: CorrectionRequestStatus.Ready } &&
                CorrectionRequestEligibility.Resolve(snapshot, aggregate.CorrectionRequests, aggregate.Attempts)?.Request.Id == open.Id)
                return await LoadAsync(id, cancellationToken);

            if (open is { Status: CorrectionRequestStatus.Preparing } &&
                open.HandedOutRevisionId == reviewedRevision && open.HandedOutSha256 == reviewedHash)
            {
                request = open;
            }
            else
            {
                if (open is not null) opening.Add(new CorrectionRequestChange.Supersede(open.Id, context.NowUtc));

                CorrectionCandidate? fresh = CorrectionRequestEligibility.RequestCandidate(
                    snapshot, aggregate.Revisions, aggregate.Outputs);
                if (fresh is null || fresh.HandedOut.Id != reviewedRevision || fresh.HandedOut.Sha256 != reviewedHash)
                    return NoLongerUnderReview();

                OperationResult<CorrectionRequest> planned = PlanCorrectionRequest(
                    aggregate.Session, requestId, fresh, note, naming, context.NowUtc);
                if (planned.IsFailure) return OperationResult.Fail<SessionView>(planned.Failure);
                request = planned.Value;
                opening.Add(new CorrectionRequestChange.Insert(request));
            }
        }

        // 2. Eligibility, on the aggregate this call will commit against — for a resumed row too.
        CorrectionCandidate? candidate = CorrectionRequestEligibility.RequestCandidate(
            snapshot, aggregate.Revisions, aggregate.Outputs);
        if (candidate is null ||
            candidate.HandedOut.Id != request.HandedOutRevisionId || candidate.HandedOut.Sha256 != request.HandedOutSha256 ||
            candidate.Reference.Id != request.ReferenceRevisionId || candidate.Reference.Sha256 != request.ReferenceSha256)
            return NoLongerUnderReview();

        WorkflowCommand.RequestColleagueCorrection handOff = new(
            request.Id, request.HandedOutRevisionId, request.HandedOutSha256,
            request.ReferenceRevisionId, request.ReferenceSha256, request.Note);
        WorkflowTransition probe = _engine.Apply(snapshot, handOff, context);
        if (probe.IsRejected) return OperationResult.Fail<SessionView>(MapRejection(probe.Rejection!));

        foreach (Revision source in new[] { candidate.Reference, candidate.HandedOut })
        {
            OperationResult<Unit> verified = await VerifyOrInvalidateAsync(aggregate, source, context, cancellationToken);
            if (verified.IsFailure) return OperationResult.Fail<SessionView>(verified.Failure);
        }

        // 3. Commit 1: the row, and the supersede of an obsolete one. Session and step unchanged.
        if (opening.Count > 0)
        {
            OperationResult<Unit> recorded = await _repository.CommitAsync(
                SessionMutation.Empty(aggregate.Session) with { CorrectionRequestChanges = opening }, cancellationToken);
            if (recorded.IsFailure) return OperationResult.Fail<SessionView>(recorded.Failure);
        }

        // 4. The files, inside the gate: independent verified copies, never an overwrite.
        OperationResult<Unit> files = await PlaceCorrectionFilesAsync(
            aggregate, request, candidate.Reference, candidate.HandedOut, naming.Instructions,
            onlyMissing: false, context, cancellationToken);
        if (files.IsFailure) return OperationResult.Fail<SessionView>(files.Failure);

        // 5. Commit 2: HandedOff and READY together, through the core with this row's context.
        OperationResult<SessionView> handedOff = await ExecuteCoreAsync(
            id, handOff, operatorName, expectedFailureAttemptId: null, cancellationToken,
            new CorrectionContext(request.Id, CorrectionPurpose.Prepare));
        if (handedOff.IsFailure) return handedOff;

        // 6. Reported as handed off only now, from the committed state.
        return await LoadAsync(id, CancellationToken.None);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SessionView>> RepairCorrectionFilesAsync(
        SessionId id, Guid requestId, CorrectionFileNaming naming, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(naming);
        if (_correctionPackages is null)
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, "Colleague correction is not available here.");

        using IDisposable lease = await SessionCompletionGate.EnterAsync(id, cancellationToken);
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure) return OperationResult.Fail<SessionView>(loaded.Failure);
        if (loaded.Value is not { } aggregate)
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, $"No session {id} exists.");

        WorkflowSnapshot snapshot = aggregate.ToSnapshot(ConfiguredRecommendations());
        if (CorrectionRequestEligibility.Resolve(snapshot, aggregate.CorrectionRequests, aggregate.Attempts) is not { } eligible ||
            eligible.Request.Id != requestId ||
            FindRevision(aggregate, eligible.Request.ReferenceRevisionId) is not { } reference ||
            FindRevision(aggregate, eligible.Request.HandedOutRevisionId) is not { } handedOut ||
            reference.Sha256 != eligible.Request.ReferenceSha256 || handedOut.Sha256 != eligible.Request.HandedOutSha256)
            return NoLongerAvailable();

        CommandContext context = CommandContext.Create(_timeProvider, _idGenerator, null);
        foreach (Revision source in new[] { reference, handedOut })
        {
            OperationResult<Unit> verified = await VerifyOrInvalidateAsync(aggregate, source, context, cancellationToken);
            if (verified.IsFailure) return OperationResult.Fail<SessionView>(verified.Failure);
        }

        // Only missing files are recreated; a present file — edited or not — is never touched.
        OperationResult<Unit> files = await PlaceCorrectionFilesAsync(
            aggregate, eligible.Request, reference, handedOut, naming.Instructions,
            onlyMissing: true, context, cancellationToken);
        return files.IsFailure
            ? OperationResult.Fail<SessionView>(files.Failure)
            : await LoadAsync(id, CancellationToken.None);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SessionView>> ImportCorrectedImageAsync(
        SessionId id, Guid requestId, RevisionId? reviewedRevision, Sha256? reviewedHash, string selectedPath,
        string? operatorName, CancellationToken cancellationToken)
    {
        if (_manualResults is null)
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, "Manual result import is unavailable.");
        if (string.IsNullOrWhiteSpace(selectedPath))
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, "Choose the corrected picture first.");

        // 1. Outside the gate: the read-only check of the chosen file, with the importer's own limits.
        //    A refusal here writes nothing, and the handoff and R are exactly as they were.
        OperationResult<SessionAggregate?> read = await _repository.LoadAsync(id, cancellationToken);
        if (read.IsFailure) return OperationResult.Fail<SessionView>(read.Failure);
        if (read.Value is not { } before)
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, $"No session {id} exists.");
        if (EligibleFor(before, requestId) is not { } eligibleBefore ||
            FindRevision(before, eligibleBefore.Request.ReferenceRevisionId) is not { } referenceBefore)
            return RequestRefusal(before, requestId);

        // The reference copy is named plainly, before its own content is judged.
        OperationResult<Sha256> chosen = await _manualResults.HashSelectedAsync(selectedPath, cancellationToken);
        if (chosen.IsSuccess && IsReferenceBytes(chosen.Value, eligibleBefore.Request))
            return IsReferenceCopy();

        OperationResult<ManualResultPreflight> preflight = await _manualResults.PreflightAsync(
            StepKind.BackgroundRemoval, referenceBefore.Facts, selectedPath, cancellationToken);
        if (preflight.IsFailure) return OperationResult.Fail<SessionView>(preflight.Failure);
        if (IsReferenceBytes(preflight.Value.SelectedSha256, eligibleBefore.Request))
            return IsReferenceCopy();

        // 2. Under one gate: the exact request again, the same bytes, then the reviewed import route.
        using IDisposable lease = await SessionCompletionGate.EnterAsync(id, cancellationToken);
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure) return OperationResult.Fail<SessionView>(loaded.Failure);
        if (loaded.Value is not { } aggregate)
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, $"No session {id} exists.");
        if (EligibleFor(aggregate, requestId) is not { } eligible)
            return RequestRefusal(aggregate, requestId);
        if (eligible.Mode == CorrectionImportMode.Review &&
            (reviewedRevision != eligible.Request.HandedOutRevisionId || reviewedHash != eligible.Request.HandedOutSha256))
            return NoLongerUnderReview();

        OperationResult<Sha256> held = await _manualResults.HashSelectedAsync(selectedPath, cancellationToken);
        if (held.IsFailure) return OperationResult.Fail<SessionView>(held.Failure);
        if (held.Value != preflight.Value.SelectedSha256)
            return OperationResult.Fail<SessionView>(OperationFailure.Create(
                FailureCode.OutputValidationFailed,
                "The selected picture changed between the check and the import; nothing was imported.",
                isRetryable: true, messageKey: "Session_CorrectionChanged"));
        if (IsReferenceBytes(held.Value, eligible.Request)) return IsReferenceCopy();

        WorkflowCommand command = eligible.Mode == CorrectionImportMode.Review
            ? new WorkflowCommand.ImportCorrectedImage(
                eligible.Request.Id, eligible.Request.HandedOutRevisionId, eligible.Request.HandedOutSha256, selectedPath)
            : new WorkflowCommand.SubmitManualResult(StepKind.BackgroundRemoval, selectedPath);

        OperationResult<SessionView> imported = await ExecuteCoreAsync(
            id, command, operatorName, expectedFailureAttemptId: null, cancellationToken,
            new CorrectionContext(eligible.Request.Id, CorrectionPurpose.Import, held.Value));
        return imported.IsFailure ? imported : await LoadAsync(id, CancellationToken.None);
    }

    // -------------------------------------------------------------------------------------
    // Core guard, used by ExecuteCoreAsync
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The service half of both correction commands and of the generic-submit guard
    /// (design §6.1; addendum §2.2 guard b). Returns a refusal, or null to continue.
    /// </summary>
    private static OperationFailure? RefuseCorrectionMisuse(
        WorkflowCommand command, SessionAggregate aggregate, WorkflowSnapshot snapshot, CorrectionContext? correction)
    {
        bool correctionCommand = command is WorkflowCommand.RequestColleagueCorrection or WorkflowCommand.ImportCorrectedImage;
        if (correctionCommand && correction is null)
        {
            return OperationFailure.Create(FailureCode.PreconditionNotMet,
                "Colleague correction is available only through its own request and import actions.");
        }

        CorrectionEligibility? eligible = CorrectionRequestEligibility.Resolve(
            snapshot, aggregate.CorrectionRequests, aggregate.Attempts);

        if (command is WorkflowCommand.SubmitManualResult && correction is null &&
            eligible?.Mode == CorrectionImportMode.AfterUnfinishedImport)
        {
            return OperationFailure.Create(FailureCode.PreconditionNotMet,
                "This job is waiting for a colleague's corrected picture; generic manual import is refused.",
                messageKey: "Session_CorrectionUseImport");
        }

        if (correction is null)
        {
            return null;
        }

        bool matches = (command, correction.Purpose) switch
        {
            (WorkflowCommand.RequestColleagueCorrection request, CorrectionPurpose.Prepare) =>
                aggregate.CorrectionRequests.SingleOrDefault(r => r.Id == correction.RequestId) is
                { Status: CorrectionRequestStatus.Preparing } row &&
                request.CorrectionRequestId == row.Id &&
                request.ReviewedRevision == row.HandedOutRevisionId && request.ReviewedHash == row.HandedOutSha256 &&
                request.ReferenceRevision == row.ReferenceRevisionId && request.ReferenceHash == row.ReferenceSha256 &&
                request.EffectiveReason == row.EffectiveReason,
            (WorkflowCommand.ImportCorrectedImage import, CorrectionPurpose.Import) =>
                eligible is { Mode: CorrectionImportMode.Review } review &&
                review.Request.Id == correction.RequestId && import.CorrectionRequestId == correction.RequestId &&
                import.ReviewedRevision == review.Request.HandedOutRevisionId &&
                import.ReviewedHash == review.Request.HandedOutSha256,
            (WorkflowCommand.SubmitManualResult submit, CorrectionPurpose.Import) =>
                eligible is { Mode: CorrectionImportMode.AfterUnfinishedImport } unfinished &&
                unfinished.Request.Id == correction.RequestId && submit.Step == StepKind.BackgroundRemoval,
            _ => false,
        };

        return matches
            ? null
            : OperationFailure.Create(FailureCode.PreconditionNotMet,
                "The correction request is no longer the one this action was for; nothing was changed.",
                messageKey: "Session_CorrectionNotAvailable");
    }

    // -------------------------------------------------------------------------------------
    // Package planning and file work
    // -------------------------------------------------------------------------------------

    private static OperationResult<CorrectionRequest> PlanCorrectionRequest(
        ProcessingSession session, Guid requestId, CorrectionCandidate candidate, string? note,
        CorrectionFileNaming naming, DateTimeOffset nowUtc)
    {
        string? referenceExtension = candidate.Reference.Facts.Format switch
        {
            ImageFormat.Png => ".png",
            ImageFormat.Jpeg => ".jpg",
            ImageFormat.Tiff => ".tif",
            _ => null,
        };
        if (referenceExtension is null)
        {
            return OperationResult.Fail<CorrectionRequest>(FailureCode.PreconditionNotMet,
                $"The picture background removal used is a {candidate.Reference.Facts.Format}, which cannot be handed out as a reference copy.");
        }

        string stem = session.OutputName.Value.Length <= MaxCorrectionNameStem
            ? session.OutputName.Value
            : session.OutputName.Value[..MaxCorrectionNameStem].TrimEnd();
        string suffix = requestId.ToString("N")[..8];
        string? reference = NameFrom(naming.ReferencePattern, stem, referenceExtension);
        string? working = NameFrom(naming.WorkingPattern, stem, ".png");
        string? returned = NameFrom(naming.ReturnPattern, stem, ".png");
        if (reference is null || working is null || returned is null ||
            string.Equals(reference, working, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reference, CorrectionInstructionsFileName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(working, CorrectionInstructionsFileName, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<CorrectionRequest>(FailureCode.PreconditionNotMet,
                "The correction file names are not usable file names.");
        }

        string trimmedNote = string.IsNullOrWhiteSpace(note) ? string.Empty : note.Trim();
        WorkflowCommand.RequestColleagueCorrection reasonSource = new(
            requestId, candidate.HandedOut.Id, candidate.HandedOut.Sha256,
            candidate.Reference.Id, candidate.Reference.Sha256, trimmedNote.Length == 0 ? null : trimmedNote);

        return OperationResult.Ok(new CorrectionRequest(
            requestId,
            session.Id,
            StepKind.BackgroundRemoval,
            candidate.HandedOut.Id,
            candidate.HandedOut.Sha256,
            candidate.Reference.Id,
            candidate.Reference.Sha256,
            WorkspaceDirRef.Create($"{session.Workspace.RelativePath}/{CorrectionArea}/{SafeFolderStem(stem)}-{suffix}"),
            reference,
            working,
            returned,
            trimmedNote.Length == 0 ? null : trimmedNote,
            reasonSource.EffectiveReason,
            CorrectionRequestStatus.Preparing,
            nowUtc,
            ReadyAtUtc: null,
            ClosedAtUtc: null,
            LastImportAttemptId: null,
            ResultRevisionId: null));
    }

    /// <summary>
    /// Places the reference copy (from U) and the working copy (from R), then the optional
    /// instructions. Nothing is deleted: a leftover temporary of this request is overwritten by its
    /// next try and is otherwise inert.
    /// </summary>
    private async Task<OperationResult<Unit>> PlaceCorrectionFilesAsync(
        SessionAggregate aggregate, CorrectionRequest request, Revision reference, Revision handedOut,
        string? instructions, bool onlyMissing, CommandContext context, CancellationToken cancellationToken)
    {
        ICorrectionPackageStore store = _correctionPackages!;
        string partial = PartialSuffixOf(request.Id);

        (string Name, Revision Source, bool ReadOnly)[] copies =
        [
            (request.ReferenceFileName, reference, true),
            (request.WorkingFileName, handedOut, false),
        ];

        foreach ((string name, Revision source, bool readOnly) in copies)
        {
            if (onlyMissing && store.Exists(request.Folder, name))
            {
                continue;
            }

            OperationResult<CorrectionFileOutcome> placed = await store.PlaceVerifiedCopyAsync(
                request.Folder, name, source.File, source.Sha256, partial, readOnly, cancellationToken);
            if (placed.IsFailure)
            {
                // A written copy that did not hash as recorded: find out whether the source itself
                // changed, which invalidates it through the existing FileMutated route.
                if (placed.Failure.Context.ContainsKey("copyMismatch"))
                {
                    OperationResult<Unit> source2 = await VerifyOrInvalidateAsync(aggregate, source, context, cancellationToken);
                    if (source2.IsFailure) return source2;
                }

                return OperationResult.Fail<Unit>(placed.Failure);
            }
        }

        if (instructions is { Length: > 0 } template)
        {
            // The screen wrote the steps in both languages; the names are the ones this row persisted.
            string text = template
                .Replace("{reference}", request.ReferenceFileName, StringComparison.Ordinal)
                .Replace("{working}", request.WorkingFileName, StringComparison.Ordinal)
                .Replace("{return}", request.SuggestedReturnName, StringComparison.Ordinal);
            OperationResult<CorrectionFileOutcome> written = await store.WriteTextOnceAsync(
                request.Folder, CorrectionInstructionsFileName, text, cancellationToken);
            if (written.IsFailure) return OperationResult.Fail<Unit>(written.Failure);
        }

        return OperationResult.Ok();
    }

    /// <summary>
    /// Re-hashes one Revision; a mismatch invalidates it in its own transaction and refuses,
    /// exactly as <see cref="EnsureIntegrityAsync"/> does for a command's subject.
    /// </summary>
    private async Task<OperationResult<Unit>> VerifyOrInvalidateAsync(
        SessionAggregate aggregate, Revision subject, CommandContext context, CancellationToken cancellationToken)
    {
        OperationResult<Sha256> verified = await _integrityGuard.VerifyAsync(subject, cancellationToken);
        if (verified.IsSuccess)
        {
            return OperationResult.Ok();
        }

        RevisionInvalidation invalidation = new(subject.Id, InvalidationReason.FileMutated, context.NowUtc);
        SessionMutation mutation = new(
            aggregate.Session with { UpdatedAtUtc = context.NowUtc },
            aggregate.Steps, [], [invalidation], [], [], [], null, null);
        await _repository.CommitAsync(mutation, cancellationToken);
        return OperationResult.Fail<Unit>(verified.Failure);
    }

    private CorrectionEligibility? EligibleFor(SessionAggregate aggregate, Guid requestId) =>
        CorrectionRequestEligibility.Resolve(
            aggregate.ToSnapshot(ConfiguredRecommendations()), aggregate.CorrectionRequests, aggregate.Attempts) is { } eligible &&
        eligible.Request.Id == requestId
            ? eligible
            : null;

    /// <summary>Why a request grants no import: already imported, or no longer current.</summary>
    private static OperationResult<SessionView> RequestRefusal(SessionAggregate aggregate, Guid requestId) =>
        aggregate.CorrectionRequests.SingleOrDefault(r => r.Id == requestId) is { Status: CorrectionRequestStatus.Returned }
            ? OperationResult.Fail<SessionView>(OperationFailure.Create(FailureCode.PreconditionNotMet,
                "This correction has already been imported; no second result is created.",
                messageKey: "Session_CorrectionAlreadyImported"))
            : NoLongerAvailable();

    private static OperationResult<SessionView> NoLongerAvailable() =>
        OperationResult.Fail<SessionView>(OperationFailure.Create(FailureCode.PreconditionNotMet,
            "This correction request is no longer available; nothing was changed.",
            messageKey: "Session_CorrectionNotAvailable"));

    private static OperationResult<SessionView> NoLongerUnderReview() =>
        OperationResult.Fail<SessionView>(OperationFailure.Create(FailureCode.PreconditionNotMet,
            "This result is no longer the one under review; nothing was changed.",
            messageKey: "Session_CorrectionResultChanged"));

    /// <summary>
    /// Whether the chosen bytes are the reference copy rather than a correction. Bytes identical to
    /// the working copy that was sent are always accepted — a colleague may judge R fine — even in
    /// the degenerate case where R and U have the same bytes.
    /// </summary>
    private static bool IsReferenceBytes(Sha256 selected, CorrectionRequest request) =>
        selected == request.ReferenceSha256 && selected != request.HandedOutSha256;

    private static OperationResult<SessionView> IsReferenceCopy() =>
        OperationResult.Fail<SessionView>(OperationFailure.Create(FailureCode.OutputValidationFailed,
            "The selected file is the reference copy, not a corrected picture; nothing was imported.",
            messageKey: "Session_CorrectionIsReference"));

    private static string PartialSuffixOf(Guid requestId) => "partial-" + requestId.ToString("N")[..8];

    /// <summary>
    /// A file name from an operator-language pattern, or null when the result could not be a plain
    /// Windows file name. Pure string work: this project touches no file system.
    /// </summary>
    private static string? NameFrom(string pattern, string stem, string extension)
    {
        if (string.IsNullOrWhiteSpace(pattern) || !pattern.Contains("{0}", StringComparison.Ordinal))
        {
            return null;
        }

        string name = pattern.Replace("{0}", stem, StringComparison.Ordinal).Trim() + extension;
        return name.Length is > 0 and <= 160 && IsPlainFileName(name) ? name : null;
    }

    private static string SafeFolderStem(string stem)
    {
        char[] cleaned = stem.Select(c => IsForbiddenInName(c) ? '_' : c).ToArray();
        string folder = new string(cleaned).Trim().TrimEnd('.');
        return folder.Length == 0 ? "job" : folder;
    }

    private static bool IsPlainFileName(string name) =>
        !name.Any(IsForbiddenInName) && !name.EndsWith('.') && !name.EndsWith(' ') && name is not ("." or "..");

    private static bool IsForbiddenInName(char c) =>
        c < 32 || c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|';
}
