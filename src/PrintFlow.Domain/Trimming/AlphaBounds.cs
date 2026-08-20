namespace PrintFlow.Domain.Trimming;

/// <summary>
/// The deterministic alpha-content scan: the smallest rectangle containing every pixel whose
/// alpha is greater than zero (Epic 11200 Part B §6).
/// </summary>
/// <remarks>
/// <b>The rule is exactly <c>alpha &gt; 0</c>.</b> There is no opacity threshold, and adding
/// one would be a product decision, not an optimisation: a barely-visible antialiased edge
/// pixel is still the operator's artwork, and a threshold of 10 or 128 would quietly shave
/// the soft edge off every cut-out in the shop. A single stray pixel in a corner therefore
/// keeps the whole canvas — which is visible to the operator in review, whereas silent
/// clipping is not.
///
/// Pure by construction: it takes an alpha plane and image dimensions and returns geometry.
/// Which pixel format the file used, whether its alpha was premultiplied, and how the plane
/// was extracted are the decoder's problem, not this function's.
/// </remarks>
public static class AlphaBounds
{
    /// <summary>
    /// Returns the content rectangle, or <c>null</c> when no pixel has any alpha at all.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="alpha">
    /// One alpha byte per pixel, row-major, top row first: index <c>y * width + x</c>.
    /// </param>
    /// <returns>
    /// The smallest <see cref="TrimBounds"/> containing every pixel with <c>alpha &gt; 0</c>,
    /// or <c>null</c> if there is no such pixel. <c>null</c> is the algorithm-level
    /// <see cref="TrimOutcome.ManualCropRequired"/>: there is nothing to crop <i>to</i>, and
    /// guessing a foreground is not on the table.
    /// </returns>
    public static TrimBounds? Compute(int width, int height, ReadOnlySpan<byte> alpha)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "An image has positive width.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "An image has positive height.");
        }

        long expected = (long)width * height;
        if (alpha.Length != expected)
        {
            throw new ArgumentException(
                $"An alpha plane for {width}×{height} holds {expected} bytes, not {alpha.Length}.", nameof(alpha));
        }

        int left = width;
        int right = -1;
        int top = -1;
        int bottom = -1;

        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = alpha.Slice(y * width, width);

            int rowLeft = -1;
            for (int x = 0; x < width; x++)
            {
                if (row[x] > 0)
                {
                    rowLeft = x;
                    break;
                }
            }

            if (rowLeft < 0)
            {
                continue;
            }

            int rowRight = rowLeft;
            for (int x = width - 1; x > rowLeft; x--)
            {
                if (row[x] > 0)
                {
                    rowRight = x;
                    break;
                }
            }

            if (top < 0)
            {
                top = y;
            }

            bottom = y;
            left = Math.Min(left, rowLeft);
            right = Math.Max(right, rowRight);
        }

        return top < 0 ? null : TrimBounds.FromEdges(left, top, right + 1, bottom + 1);
    }
}
