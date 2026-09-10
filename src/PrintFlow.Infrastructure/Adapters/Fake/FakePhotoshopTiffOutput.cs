namespace PrintFlow.Infrastructure.Adapters.Fake;

/// <summary>
/// Which class of production TIFF a successful Fake Photoshop call actually writes
/// (SCRUM-11097).
/// </summary>
/// <remarks>
/// The behavioural half of the fake's vocabulary — export failure, timeout, interruption, unknown
/// dialog, missing output, unreadable output — is <see cref="FakeAdapterScenario"/>, shared with
/// the Meitu fake because those outcomes are identical for both. This is the half that is
/// Photoshop-specific: what the file on disk <i>is</i> when a call gets far enough to write one.
/// The two are set independently because they are independent questions, and collapsing them into
/// one enum would force every behavioural case to be restated once per output class.
/// <para>
/// Every member except <see cref="CopyApprovedInput"/> causes a genuine TIFF to be written and
/// then handed to the real <c>ProductionTiffInspector</c> and
/// <c>ProductionTiffPreparationMatch</c>. The fake never returns a pre-labelled verdict: a
/// <see cref="WrongPixelWidth"/> run fails because the bytes on disk really are the wrong width
/// and the Product really refuses them, not because the fake said so. That is the difference
/// between evidence that PrintFlow detects a fault and evidence that a double can be scripted.
/// </para>
/// </remarks>
public enum FakePhotoshopTiffOutput
{
    /// <summary>
    /// Copies the approved input to the reserved output path (the default, unchanged).
    /// </summary>
    /// <remarks>
    /// Deliberately still the default. It is exactly right for the workflow mechanics it was
    /// built for — a PNG named <c>.tif</c> proves a Revision, a <c>PrintOutput</c> and a review
    /// step are created — and dozens of workflow tests depend on a Fake success being cheap and
    /// structurally uninteresting. Making a real TIFF the default would change what every one of
    /// those tests is exercising.
    /// </remarks>
    CopyApprovedInput = 0,

    /// <summary>An accepted production TIFF at exactly the preparation's projected geometry.</summary>
    ValidTiff,

    /// <summary>
    /// Separated CMYK with no fifth ink at all: four 8-bit samples, so no W1 channel exists in
    /// the raster.
    /// </summary>
    /// <remarks>
    /// The Photoshop resource block is left in place, so the file claims a W1 spot channel it no
    /// longer has. That is a deliberate consequence of changing exactly one option rather than
    /// several, and it is invisible to the outcome: <c>ProductionTiffInspector</c> checks the
    /// sample count before it reads any resource, so this is refused as "requires exactly five
    /// 8-bit samples". Photoshop could not emit this exact file — it is a scenario isolating one
    /// rule, not a forgery of a real export.
    /// </remarks>
    MissingWhiteChannel,

    /// <summary>
    /// A named W1 spot channel whose stored samples are wholly blank — no white ink anywhere.
    /// </summary>
    EmptyWhiteChannel,

    /// <summary>
    /// The right five samples in the wrong colour space: PhotometricInterpretation RGB, not
    /// separated CMYK.
    /// </summary>
    /// <remarks>
    /// The sample count is deliberately held at five so that the photometric rule is the one being
    /// exercised. A real Photoshop RGB export would carry three samples and would be refused one
    /// check earlier, on the sample count — which would leave
    /// <c>ProductionTiffInspector</c>'s "not separated CMYK" branch with no scenario reaching it
    /// at all. Isolating the rule is the point; imitating an RGB export is not.
    /// </remarks>
    WrongColourMode,

    /// <summary>One pixel wider than the preparation projected. Every other fact is accepted.</summary>
    WrongPixelWidth,

    /// <summary>One pixel taller than the preparation projected. Every other fact is accepted.</summary>
    WrongPixelHeight,

    /// <summary>
    /// The projected pixel grid stamped at 150 PPI, so the file resolves to twice the physical
    /// canvas that was asked for.
    /// </summary>
    IncorrectDpi,

    /// <summary>
    /// Correct geometry and correct channels, with the Compression tag declaring LZW instead of
    /// the contracted None (TIFF value 1).
    /// </summary>
    /// <remarks>
    /// The strips are still written uncompressed, so the file declares a codec its bytes do not
    /// use and a reader that trusted the tag would fail to decode it. That is accurate to the
    /// scenario's purpose — <c>ProductionTiffInspector</c> refuses the tag before any strip is
    /// decoded, which is exactly the "matches validated bit depth and compression" clause of the
    /// TIFF contract — but it should not be mistaken for a file a viewer would open happily. For
    /// that, see <see cref="IncorrectChannelMetadata"/>.
    /// </remarks>
    IncorrectMetadata,

    /// <summary>
    /// A completely well-formed, openable production TIFF whose Photoshop channel metadata names
    /// the spot channel "White" instead of the contracted "W1".
    /// </summary>
    /// <remarks>
    /// The second and last metadata fault in the vocabulary, and the one that shows why the clause
    /// needs more than <see cref="IncorrectMetadata"/>: every byte here decodes, the geometry is
    /// right, the resolution is right, the ink is there — and it is still not a file Maintop may
    /// be handed, because PrintFlow's white-underbase contract is a channel <i>named</i> W1. The
    /// inspector refuses it as "Photoshop image resources do not identify exactly one W1 channel
    /// as a spot colour".
    /// <para>
    /// The remaining metadata faults the inspector refuses — byte order, planar configuration,
    /// alpha/extra samples, layer compression, image pyramid — stay covered as
    /// <c>ProductionTiffInspectorTests</c> rows. Promoting all of them to output classes would
    /// prove the same seam seven times.
    /// </para>
    /// </remarks>
    IncorrectChannelMetadata,
}
