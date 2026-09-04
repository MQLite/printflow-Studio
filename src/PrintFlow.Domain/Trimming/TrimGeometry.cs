namespace PrintFlow.Domain.Trimming;

/// <summary>
/// The two rectangles one successful automatic trim established: what was detected, and what
/// was actually cropped to (SCRUM-11081).
/// </summary>
/// <remarks>
/// The pair is one value rather than two loose nullable properties because neither half means
/// anything alone. "The applied rectangle, with no record of what was detected" cannot answer
/// how much safety margin a result carries, and "the detected rectangle, with no record of what
/// was applied" cannot be checked against the file that was produced. A trim either established
/// both or established neither, and this type is the shape of that fact.
/// <para>
/// Both rectangles are in the <b>source</b> image's pixel coordinates and use
/// <see cref="TrimBounds"/>'s half-open <c>[left, top → right, bottom)</c> convention unchanged.
/// Nothing here converts between conventions: a second coordinate system whose only purpose was
/// storage would be one more place for an off-by-one to hide.
/// </para>
/// <para>
/// Immutable attempt metadata. It describes how one exact Revision was produced, so a later
/// retry at a different margin records its own geometry rather than editing this one.
/// </para>
/// </remarks>
/// <param name="ContentBounds">
/// The smallest rectangle containing every source pixel whose alpha is greater than zero,
/// <i>before</i> any operator safety margin — exactly what <see cref="AlphaBounds.Compute"/>
/// returned. Never touched by margins, display scaling or print sizing.
/// </param>
/// <param name="AppliedBounds">
/// The rectangle actually cropped to: <paramref name="ContentBounds"/> grown by the attempt's
/// <see cref="TrimMargin"/> and clamped to the source canvas. The produced PNG is
/// <see cref="TrimBounds.Width"/> × <see cref="TrimBounds.Height"/> of this rectangle, by
/// construction rather than by coincidence.
/// </param>
public sealed record TrimGeometry(TrimBounds ContentBounds, TrimBounds AppliedBounds)
{
    /// <summary>
    /// Records the geometry of one produced trim, refusing a pair that could not have been
    /// produced by margin expansion.
    /// </summary>
    /// <remarks>
    /// A margin expands the crop and the clamp only ever stops it at the canvas edge, so an
    /// applied rectangle can never cut <i>inside</i> the detected content. A pair that says it
    /// did is not a trim this product performed, and storing it would put a row in the history
    /// that no reader can honestly interpret — the same reason migration 0002 refuses a
    /// negative margin.
    /// </remarks>
    public static TrimGeometry Create(TrimBounds contentBounds, TrimBounds appliedBounds)
    {
        if (contentBounds.IsEmpty)
        {
            throw new ArgumentException("A detected content rectangle contains at least one pixel.", nameof(contentBounds));
        }

        if (appliedBounds.IsEmpty)
        {
            throw new ArgumentException("An applied crop rectangle contains at least one pixel.", nameof(appliedBounds));
        }

        if (appliedBounds.Left > contentBounds.Left ||
            appliedBounds.Top > contentBounds.Top ||
            appliedBounds.RightExclusive < contentBounds.RightExclusive ||
            appliedBounds.BottomExclusive < contentBounds.BottomExclusive)
        {
            throw new ArgumentException(
                $"An applied crop {appliedBounds} cannot be smaller than the content {contentBounds} it was expanded from.",
                nameof(appliedBounds));
        }

        return new TrimGeometry(contentBounds, appliedBounds);
    }

    /// <summary>True when the margin added nothing — a tight trim, or one clamped away entirely.</summary>
    public bool IsTightToContent => ContentBounds == AppliedBounds;
}
