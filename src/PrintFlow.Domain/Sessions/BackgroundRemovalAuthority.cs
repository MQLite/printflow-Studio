using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Domain.Sessions;

/// <summary>
/// The explicit authority required before Background Removal may use Meitu's automatic
/// selection over content that has already been reviewed for that purpose.
/// </summary>
/// <remarks>
/// <see cref="Unspecified"/> is a refusal state, not a default. Nothing infers it, nothing
/// defaults to it, and a Background Removal step still holding it never reaches the adapter
/// (Epic 11300 Part C2B1 §7).
/// <para>
/// It lives in the Domain rather than beside <c>MeituRequest</c> because the same value is now
/// three things at once: pending session state, an immutable field of the
/// <c>ProcessingAttempt</c> that consumed it, and a field of the adapter request. Only the
/// third is an adapter concern, and <c>PrintFlow.Domain</c> is the one assembly all three can
/// see. The enum itself — its name, its two members, and their meaning — is exactly the one
/// Part C2A introduced; nothing about the adapter contract changed.
/// </para>
/// </remarks>
public enum BackgroundRemovalDecision
{
    /// <summary>No decision has been made. Never an authorisation, and never a default.</summary>
    Unspecified,

    /// <summary>
    /// The operator reviewed this specific content and authorised Meitu's automatic selection
    /// for it.
    /// </summary>
    UseAutomaticSelectionForReviewedContent,

    ManualResultForReviewedContent,
}

/// <summary>
/// One <see cref="BackgroundRemovalDecision"/> bound to the exact reviewed content it
/// authorises (Epic 11300 Part C2B1 §4).
/// </summary>
/// <remarks>
/// The whole point of this record is the binding. The decision means "automatic selection is
/// authorised for <i>this</i> reviewed content", never "automatic selection is enabled for this
/// session" — so the authority carries the identity of the artefact the operator actually
/// looked at, and is usable only while that artefact is still the one Background Removal will
/// consume (§8).
/// <para>
/// Both halves of the binding are kept. <see cref="ReviewedRevisionId"/> answers "which
/// artefact", and <see cref="ReviewedSha256"/> answers "which bytes" — the same pair every
/// review decision binds to (MVP design invariants 2 and 3), and for the same reason: an id
/// alone would still match after the file underneath it changed.
/// </para>
/// <para>
/// It is <i>pending</i> state. What a Background Removal attempt actually ran with is recorded
/// on that attempt and never rewritten, exactly as trim parameters are (§11).
/// </para>
/// </remarks>
/// <param name="Decision">The authorised decision. Never <see cref="BackgroundRemovalDecision.Unspecified"/>.</param>
/// <param name="ReviewedRevisionId">The upstream Revision whose content was reviewed.</param>
/// <param name="ReviewedSha256">The bytes of that Revision as they were when reviewed.</param>
public sealed record BackgroundRemovalAuthority(
    BackgroundRemovalDecision Decision,
    RevisionId ReviewedRevisionId,
    Sha256 ReviewedSha256)
{
    /// <summary>
    /// Creates an authority, refusing one that authorises nothing.
    /// </summary>
    /// <remarks>
    /// <see cref="BackgroundRemovalDecision.Unspecified"/> bound to a Revision would be a record
    /// that reads as a decision and grants nothing — the exact shape a later reader would
    /// mistake for an authorisation. The absence of an authority is <c>null</c>, and that is the
    /// only way to spell it.
    /// </remarks>
    public static BackgroundRemovalAuthority For(
        BackgroundRemovalDecision decision, RevisionId reviewedRevisionId, Sha256 reviewedSha256)
    {
        if (decision == BackgroundRemovalDecision.Unspecified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decision), decision,
                "Unspecified is the absence of a decision; an authority that authorises nothing is null, not a record.");
        }

        return new BackgroundRemovalAuthority(decision, reviewedRevisionId, reviewedSha256);
    }

    /// <summary>
    /// Whether this authority covers the artefact identified by <paramref name="revisionId"/>
    /// and <paramref name="sha256"/>.
    /// </summary>
    /// <remarks>
    /// The single predicate behind every stale-authority rule in the slice: the engine's
    /// Background Removal precondition, the read model's "is this session ready", and the
    /// re-binding check the decision command performs all ask this one question, so an offered
    /// control and an accepted command cannot disagree about what "still valid" means (§23).
    /// <para>
    /// Both halves must match. Requiring the hash as well as the id is what makes a Revision
    /// whose bytes were replaced in place fail this check rather than pass it on identity
    /// alone (§24).
    /// </para>
    /// </remarks>
    public bool Authorises(RevisionId revisionId, Sha256 sha256) =>
        ReviewedRevisionId == revisionId && ReviewedSha256.Equals(sha256);
}
