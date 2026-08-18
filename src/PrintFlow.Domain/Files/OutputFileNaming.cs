using System.Globalization;
using PrintFlow.Domain.Outputs;

namespace PrintFlow.Domain.Files;

/// <summary>Which produced artefact a file name is being built for (Epic 11100 plan §13.2).</summary>
public enum NamingArtifactKind
{
    /// <summary>Meitu-enhanced PNG.</summary>
    Enhanced,

    /// <summary>Background-removed / trimmed cut-out PNG.</summary>
    Cutout,

    /// <summary>Production CMYK + white-ink TIFF.</summary>
    ProductionTiff,
}

/// <summary>
/// Builds the proposed (pre-collision) file name for a produced artefact from the operator's
/// output name and the preset-driven naming patterns.
/// </summary>
/// <remarks>
/// Pure string formatting only — no file-system access. Collision handling (<c>_02</c>,
/// <c>_03</c>, atomic reservation) is a workspace concern because only the workspace module
/// can see what already exists on disk (Epic 11100 Task 11107; plan §13.3).
/// </remarks>
public static class OutputFileNaming
{
    /// <summary>
    /// Builds the proposed file name for <paramref name="kind"/>, before any collision
    /// suffix is applied.
    /// </summary>
    /// <param name="kind">Which artefact is being named.</param>
    /// <param name="name">The sanitised operator output name.</param>
    /// <param name="patterns">Naming patterns loaded from the verified preset.</param>
    /// <param name="targetWidthMm">
    /// Required only for <see cref="NamingArtifactKind.ProductionTiff"/>, whose pattern
    /// includes the target width in millimetres (MVP design §9.4 example: <c>Name_280mm_CMYK_W.tif</c>).
    /// </param>
    public static string BuildProposedFileName(
        NamingArtifactKind kind,
        OutputName name,
        NamingPatternSet patterns,
        double? targetWidthMm = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        return kind switch
        {
            NamingArtifactKind.Enhanced =>
                string.Format(CultureInfo.InvariantCulture, patterns.EnhancedPattern, name.Value),

            NamingArtifactKind.Cutout =>
                string.Format(CultureInfo.InvariantCulture, patterns.CutoutPattern, name.Value),

            NamingArtifactKind.ProductionTiff => targetWidthMm is double widthMm
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    patterns.ProductionTiffPattern,
                    name.Value,
                    (int)Math.Round(widthMm, MidpointRounding.AwayFromZero))
                : throw new ArgumentException(
                    "A production TIFF file name requires the target width in millimetres.",
                    nameof(targetWidthMm)),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown artefact kind."),
        };
    }

    /// <summary>
    /// Builds one collision candidate: the proposed name for <paramref name="sequence"/> = 1,
    /// or the proposed stem with the preset's collision suffix inserted before the extension
    /// for <paramref name="sequence"/> &gt;= 2 (MVP design §9.4: <c>Name.png</c>,
    /// <c>Name_02.png</c>, <c>Name_03.png</c>, …).
    /// </summary>
    public static string BuildCollisionCandidate(
        string proposedFileName, NamingPatternSet patterns, int sequence)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Sequence starts at 1.");
        }

        if (sequence == 1)
        {
            return proposedFileName;
        }

        int dot = proposedFileName.LastIndexOf('.');
        string stem = dot >= 0 ? proposedFileName[..dot] : proposedFileName;
        string extension = dot >= 0 ? proposedFileName[dot..] : string.Empty;
        string suffix = string.Format(CultureInfo.InvariantCulture, patterns.CollisionSuffixPattern, sequence);
        return stem + suffix + extension;
    }
}
