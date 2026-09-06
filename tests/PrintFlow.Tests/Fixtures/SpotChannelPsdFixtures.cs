using System.IO;
using System.Text;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A synthetic RGB/8 PSD carrying exactly one <c>W1</c> spot channel whose content is a
/// deliberately asymmetric, deterministic quadrant pattern.
/// </summary>
/// <remarks>
/// Built for the SCRUM-11101-A Retain gates, where "the W1 still exists afterwards" is not a
/// sufficient answer. The pattern is chosen so that three different failures are separable from
/// a genuine retention:
/// <list type="bullet">
/// <item><description>
/// <b>Loss</b> — every W1 pixel reads 255 (no ink), or the channel is gone.
/// </description></item>
/// <item><description>
/// <b>Regeneration</b> — the validated production Action derives W1 from the composite, so a
/// regenerated channel covers the artwork almost everywhere. This pattern deliberately leaves one
/// whole quadrant with no ink at all and gives the other three distinct densities, which no
/// generated underbase would reproduce.
/// </description></item>
/// <item><description>
/// <b>Misalignment</b> — the four ink densities are distinct in both axes, so a flip, a rotation
/// or a shift relative to the visual canvas changes which density lands in which quadrant. The
/// RGB composite carries the same quadrant structure, so W1 and the visible artwork can be
/// compared to each other rather than only to their own earlier selves.
/// </description></item>
/// </list>
/// <para>
/// Spot-channel convention: 0 is full ink and 255 is no ink, which is what makes bins 0–254 the
/// "non-empty" test used by the accepted W1 validation.
/// </para>
/// No customer artwork is involved and no third-party PSD writer is used; the bytes below are the
/// Adobe file format written directly, in the same style as
/// <see cref="Integration.Ui.PsdInputPreparationTests.RgbCompositePsd"/>.
/// </remarks>
internal static class SpotChannelPsdFixtures
{
    /// <summary>Canvas width in pixels. Divisible by four so quadrant centres are exact.</summary>
    internal const int Width = 400;

    /// <summary>Canvas height in pixels.</summary>
    internal const int Height = 300;

    /// <summary>W1 ink density in the top-left quadrant: full ink.</summary>
    internal const byte TopLeftInk = 0;

    /// <summary>W1 ink density in the top-right quadrant.</summary>
    internal const byte TopRightInk = 96;

    /// <summary>W1 ink density in the bottom-left quadrant.</summary>
    internal const byte BottomLeftInk = 192;

    /// <summary>W1 ink density in the bottom-right quadrant: no ink at all.</summary>
    internal const byte BottomRightInk = 255;

    /// <summary>The visible RGB colour of each quadrant, in the same TL, TR, BL, BR order.</summary>
    private static readonly byte[][] QuadrantColours =
    [
        [220, 40, 40],
        [40, 180, 60],
        [50, 70, 220],
        [240, 235, 210],
    ];

    /// <summary>The four ink densities in TL, TR, BL, BR order.</summary>
    internal static byte[] QuadrantInk => [TopLeftInk, TopRightInk, BottomLeftInk, BottomRightInk];

    /// <summary>Which quadrant a pixel falls in: 0 = TL, 1 = TR, 2 = BL, 3 = BR.</summary>
    private static int Quadrant(int x, int y) => (y < Height / 2 ? 0 : 2) + (x < Width / 2 ? 0 : 1);

    /// <summary>
    /// RGB/8, one W1 spot channel, uncompressed planar composite, no layers, 72 PPI by omission
    /// of a resolution resource.
    /// </summary>
    internal static byte[] QuadrantW1Psd() => QuadrantSpotPsd("W1");

    /// <summary>
    /// The same canvas carrying two spot channels, so nothing can positively classify which one
    /// is the production white ink.
    /// </summary>
    /// <remarks>
    /// The supported-existing-W1 contract is "exactly one spot channel, named W1". This file
    /// satisfies neither half on its own terms and must be refused rather than guessed at — the
    /// original Jira row defines behaviour for "an existing white-ink spot channel", singular, and
    /// keeps ink identity a production-preset concern rather than something the workflow may infer.
    /// </remarks>
    internal static byte[] AmbiguousTwoSpotPsd() => QuadrantSpotPsd("W1", "W2");

    /// <summary>
    /// RGB/8 with one spot channel per supplied name; the first carries the quadrant ink pattern
    /// and any further channel is solid, i.e. empty of ink.
    /// </summary>
    private static byte[] QuadrantSpotPsd(params string[] spotNames)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        void U16(ushort value) { writer.Write((byte)(value >> 8)); writer.Write((byte)value); }
        void U32(uint value) { U16((ushort)(value >> 16)); U16((ushort)value); }

        // File header: R, G, B plus one channel per spot; RGB colour mode, 8 bits.
        writer.Write("8BPS"u8); U16(1); writer.Write(new byte[6]); U16((ushort)(3 + spotNames.Length));
        U32(Height); U32(Width); U16(8); U16(3);

        // Colour mode data: none for RGB.
        U32(0);

        // 1006, alpha channel names: one Pascal string per extra channel, the block padded to an
        // even length. 1007, display info: 14 bytes per extra channel, kind 2 meaning spot.
        byte[] names = [.. spotNames.SelectMany(name =>
            new byte[] { (byte)name.Length }.Concat(Encoding.ASCII.GetBytes(name)))];
        int namesPadded = names.Length + (names.Length % 2);
        int displayLength = 14 * spotNames.Length;

        // Image resources: merged-data marker, then the two blocks above.
        U32((uint)(30 + (12 + namesPadded) + (12 + displayLength)));
        writer.Write("8BIM"u8); U16(1057); U16(0); U32(17);
        U32(1); writer.Write((byte)1); U32(0); U32(0); U32(1); writer.Write((byte)0);
        writer.Write("8BIM"u8); U16(1006); U16(0); U32((uint)names.Length);
        writer.Write(names);
        if (namesPadded != names.Length) writer.Write((byte)0);
        writer.Write("8BIM"u8); U16(1007); U16(0); U32((uint)displayLength);
        foreach (string _ in spotNames)
        {
            // RGB space, pure red ink colour, 100% opacity, kind 2 = spot, one padding byte.
            U16(0); U16(65535); U16(0); U16(0); U16(0); U16(100); writer.Write(new byte[] { 2, 0 });
        }

        // Layer and mask information: none. The merged composite is the whole document.
        U32(0);

        // Image data: compression 0 (raw), then one full plane per channel in order.
        U16(0);
        for (int component = 0; component < 3; component++)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    writer.Write(QuadrantColours[Quadrant(x, y)][component]);
                }
            }
        }
        for (int spot = 0; spot < spotNames.Length; spot++)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    // Only the first spot carries the quadrant pattern; any further one is blank.
                    writer.Write(spot == 0 ? QuadrantInk[Quadrant(x, y)] : BottomRightInk);
                }
            }
        }

        return stream.ToArray();
    }
}
