namespace PrintFlow.Domain.Trimming;

/// <summary>
/// The declared mode of outward margin expansion around a base rectangle.
/// </summary>
/// <remarks>
/// The mode is the operator's declared <i>intent</i>, not a restatement of the numbers: a
/// uniform margin of zero and a tight crop produce the same rectangle but mean different
/// things to the person who chose them, and a later margin UI needs to know which was asked
/// for. It is therefore carried by <see cref="TrimMargin"/> itself rather than travelling
/// beside it, so the two can never disagree.
/// </remarks>
public enum TrimMode
{
    /// <summary>Crop exactly to the base rectangle: zero margin on all four edges.</summary>
    TightCrop,

    /// <summary>Keep the same non-negative pixel margin on all four edges.</summary>
    UniformMargin,

    /// <summary>Keep an independently chosen non-negative pixel margin per edge.</summary>
    EdgeSpecificMargin,
}

/// <summary>
/// What a trim attempt established about the image (Epic 11200 Part B §3).
/// </summary>
/// <remarks>
/// Deliberately three values, not two. "Nothing to crop" and "this image cannot be cropped
/// deterministically" are different facts with different consequences: the first is a normal
/// success that still produces a Revision, the second is a refusal to guess that must reach
/// a human. Collapsing them would make a fully transparent or alpha-less file look like a
/// finished trim.
/// </remarks>
public enum TrimOutcome
{
    /// <summary>Alpha content was found and the canvas was reduced to it.</summary>
    Trimmed,

    /// <summary>Alpha content was found, but it already fills the canvas; the extent is unchanged.</summary>
    NoChangeRequired,

    /// <summary>
    /// No usable alpha content could be established, so no automatic crop is honest. The
    /// operator must crop manually; PrintFlow never infers a background from colour.
    /// </summary>
    ManualCropRequired,
}
