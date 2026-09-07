using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// What the source artwork actually resolves to at the size this output was made at
/// (SCRUM-11104 §18–§22).
/// </summary>
/// <remarks>
/// Two different facts live here and they are deliberately not merged. The production TIFF's own
/// resolution is fixed at <see cref="OutputDpi"/> — it is a contract, not a measurement — while
/// <see cref="EffectiveDpiX"/> and <see cref="EffectiveDpiY"/> say how many source pixels the
/// operator's chosen physical size actually had behind it. A 214-PPI effective resolution inside
/// a 300-PPI file is an ordinary, reportable state of affairs, and flattening the two into one
/// number is exactly the misreading §19 exists to prevent.
/// <para>
/// Every input is bound to <see cref="SourceRevisionId"/>: the Revision the producing attempt's
/// preparation was calculated from, and never the session's original import, a historical
/// Revision, or whatever the screen currently happens to be showing (§20).
/// </para>
/// <para>
/// There are no bands, thresholds or colours here, and no <c>IsAcceptable</c>. SCRUM-11095
/// settled that question with explicit operator enlargement authority rather than invented
/// quality tiers, and a second opinion expressed as a traffic light would quietly become a
/// competing authority (§21).
/// </para>
/// </remarks>
/// <param name="SourcePixelWidth">The bound Revision's own pixel width, from its validated facts.</param>
/// <param name="SourcePixelHeight">The bound Revision's own pixel height.</param>
/// <param name="PhysicalWidthMm">The physical width the preparation projected for this output.</param>
/// <param name="PhysicalHeightMm">The physical height the preparation projected for this output.</param>
/// <param name="EnlargementAuthorityRequired">
/// Whether the run added pixels and therefore needed an explicit operator authority at all.
/// </param>
/// <param name="EnlargementAuthorised">
/// Whether that authority was actually present. Emphatically not a claim that the effective
/// resolution is good enough: authorising an enlargement is a decision to proceed knowing the
/// number, not a judgement that the number is fine (§22).
/// </param>
public sealed record TiffEffectiveResolution(
    RevisionId SourceRevisionId,
    int SourcePixelWidth,
    int SourcePixelHeight,
    double PhysicalWidthMm,
    double PhysicalHeightMm,
    double EffectiveDpiX,
    double EffectiveDpiY,
    int OutputDpi,
    bool EnlargementAuthorityRequired,
    bool EnlargementAuthorised)
{
    private const double MillimetresPerInch = 25.4;

    /// <summary>
    /// The one effective-resolution formula in the codebase (§19).
    /// </summary>
    /// <remarks>
    /// Source pixels over requested inches, per axis. It deliberately does not consult the TIFF's
    /// own resolution tag: that tag says the file is 300 PPI, which is true of every production
    /// output and therefore answers a different question from "how much artwork was there".
    /// </remarks>
    public static double EffectiveDpi(int sourcePixels, double physicalMillimetres) =>
        physicalMillimetres > 0 && double.IsFinite(physicalMillimetres)
            ? sourcePixels / (physicalMillimetres / MillimetresPerInch)
            : double.NaN;

    /// <summary>Whether the source held fewer pixels than the fixed production resolution wanted.</summary>
    /// <remarks>
    /// A statement of arithmetic, not a verdict. It exists so a screen can say "below the
    /// production resolution" in words rather than leaving an operator to compare two numbers,
    /// and it has no threshold of its own beyond the 300 PPI the output contract already fixes.
    /// </remarks>
    public bool IsBelowProductionResolution =>
        EffectiveDpiX < OutputDpi - 0.5 || EffectiveDpiY < OutputDpi - 0.5;
}

/// <summary>
/// One production TIFF, decoded and described for final review (SCRUM-11104 §44).
/// </summary>
/// <remarks>
/// The payload is bound to exactly one <see cref="PrintOutputId"/> and one <see cref="Sha256"/>,
/// and the bytes the three previews were decoded from are the bytes that hash. That binding is
/// the whole point of the record: a screen holding one of these cannot be showing output A's
/// pixels beside output B's metadata, and a caching layer keyed on it cannot serve a stale
/// preview for a file that changed (§16, §38).
/// <para>
/// No WPF type appears here and none ever may: the payloads are encoded PNG bytes, which the
/// view layer turns into an <c>ImageSource</c> at bind time (§44).
/// </para>
/// </remarks>
/// <param name="OutputPath">
/// The absolute path of the managed TIFF this payload was decoded from (§23). Present because
/// the acceptance criteria require the output location to be inspectable; it is a value the
/// screen displays, never one a caller may supply.
/// </param>
/// <param name="EffectiveResolution">
/// Null when the producing attempt recorded no preparation — a row written before preparations
/// were snapshotted. Absent rather than guessed (§25).
/// </param>
public sealed record TiffReviewPayload(
    PrintOutputId PrintOutputId,
    RevisionId RevisionId,
    Sha256 Sha256,
    string FileName,
    string OutputPath,
    WorkspaceArea Area,
    long ByteLength,
    int PixelWidth,
    int PixelHeight,
    int PreviewPixelWidth,
    int PreviewPixelHeight,
    double XResolutionDpi,
    double YResolutionDpi,
    string ColourMode,
    int BitsPerSample,
    int InkChannelCount,
    string WhiteInkChannelName,
    long WhiteInkSampleCount,
    WhiteUnderbaseBranch Branch,
    ProductionPresetRef Preset,
    string ColourConversion,
    string WhiteInkPolarity,
    double PhysicalWidthMm,
    double PhysicalHeightMm,
    TiffEffectiveResolution? EffectiveResolution,
    ReadOnlyMemory<byte> ColourPayload,
    ReadOnlyMemory<byte> WhiteInkPayload,
    ReadOnlyMemory<byte> OverlayPayload)
{
    /// <summary>True when the payloads are reduced for display rather than every pixel.</summary>
    public bool IsDownsampledForDisplay =>
        PreviewPixelWidth < PixelWidth || PreviewPixelHeight < PixelHeight;

    /// <summary>Whether this payload describes the output identified by both halves of its identity.</summary>
    /// <remarks>
    /// The predicate a cache is keyed on. Both halves must match, for the reason every review
    /// binding in this codebase keeps both: the id answers "which output" and the hash answers
    /// "which bytes", and an id alone still matches after the file underneath it was replaced.
    /// </remarks>
    public bool Covers(PrintOutputId outputId, Sha256 sha256) =>
        PrintOutputId == outputId && Sha256.Equals(sha256);
}

/// <summary>
/// The only route by which production-TIFF review pixels reach the UI (SCRUM-11104 §43, §45).
/// </summary>
/// <remarks>
/// Shaped exactly like <see cref="IArtefactPreviewService"/> and for the same reason: it names a
/// <see cref="SessionId"/> and a <see cref="RevisionId"/> and never a path, so there is no
/// arbitrary-file-read case to reject. A view model holding both identifiers can still only see
/// the artefact the operator is already reviewing.
/// <para>
/// Strictly read-only. It creates no Revision, records no review, promotes nothing and advances
/// nothing — looking at a white-ink channel is not an action (§36).
/// </para>
/// <para>
/// It is never a validation gate. A <c>PrintOutput</c> exists only after
/// <c>ProductionTiffInspector</c> accepted the file, and this asks for one; a TIFF that reached
/// review without being validated has no output row to find, and gets no payload (§45).
/// </para>
/// </remarks>
public interface IProductionTiffReviewService
{
    /// <summary>
    /// Decodes the production output whose TIFF is Revision <paramref name="revisionId"/> of
    /// session <paramref name="sessionId"/>.
    /// </summary>
    /// <remarks>
    /// Fails with <see cref="FailureCode.PreconditionNotMet"/> when the session does not exist,
    /// the Revision does not belong to it, or the Revision is not a production output; with
    /// <see cref="FailureCode.RevisionIntegrityMismatch"/> when the bytes on disk are no longer
    /// the ones the output records; and with the decoder's own failure when the file is missing
    /// or is not an accepted production TIFF. No outcome changes anything about the session.
    /// </remarks>
    Task<OperationResult<TiffReviewPayload>> GetReviewAsync(
        SessionId sessionId, RevisionId revisionId, CancellationToken cancellationToken);
}
