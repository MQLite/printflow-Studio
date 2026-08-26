using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;

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
/// Pure string rendering only — no file-system access. Collision handling (<c>_02</c>,
/// <c>_03</c>, atomic reservation) is a workspace concern because only the workspace module
/// can see what already exists on disk (Epic 11100 Task 11107; plan §13.3).
///
/// Every pattern goes through <see cref="NamingPatternRenderer"/>, which honours only the
/// named tokens listed for each artefact kind below. Nothing here reaches
/// <c>string.Format</c>: the accepted manifest's patterns are named-token patterns, and
/// handing one to composite formatting is what terminated the WPF process at the R2 Final
/// Gate (naming-contract fix §4).
/// </remarks>
public static class OutputFileNaming
{
    /// <summary>
    /// Builds the proposed file name for <paramref name="kind"/>, before any collision
    /// suffix is applied.
    /// </summary>
    /// <param name="kind">Which artefact is being named.</param>
    /// <param name="name">The sanitised operator output name, bound to <c>{Name}</c>.</param>
    /// <param name="patterns">Naming patterns loaded from the verified preset.</param>
    /// <param name="targetWidthMm">
    /// Required only for <see cref="NamingArtifactKind.ProductionTiff"/>, whose pattern also
    /// carries <c>{SizeMm}</c> — the target width in millimetres (MVP design §9.4 example:
    /// <c>Name_280mm_CMYK_W.tif</c>).
    /// </param>
    /// <returns>
    /// The proposed name, or a structured failure when the preset's pattern is not one this
    /// artefact kind can render. A pattern defect is reported, never thrown, because it
    /// arrives as configuration data and must reach the operator through the ordinary failure
    /// surface rather than as an unhandled exception (§6).
    /// </returns>
    public static OperationResult<string> BuildProposedFileName(
        NamingArtifactKind kind,
        OutputName name,
        NamingPatternSet patterns,
        double? targetWidthMm = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        return kind switch
        {
            NamingArtifactKind.Enhanced =>
                NamingPatternRenderer.Render(patterns.EnhancedPattern, NamingPatternRenderer.ForName(name)),

            NamingArtifactKind.Cutout =>
                NamingPatternRenderer.Render(patterns.CutoutPattern, NamingPatternRenderer.ForName(name)),

            NamingArtifactKind.ProductionTiff => targetWidthMm is double widthMm
                ? NamingPatternRenderer.Render(
                    patterns.ProductionTiffPattern,
                    NamingPatternRenderer.ForName(name),
                    NamingPatternRenderer.ForSizeMm(widthMm))
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
    /// <remarks>
    /// The suffix pattern carries <c>{Sequence}</c> or <c>{Sequence:00}</c> and nothing else:
    /// the stem it is inserted into has already been rendered, so a suffix pattern that named
    /// the output again would be a second, contradictory naming authority.
    /// </remarks>
    public static OperationResult<string> BuildCollisionCandidate(
        string proposedFileName, NamingPatternSet patterns, int sequence)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Sequence starts at 1.");
        }

        if (sequence == 1)
        {
            return OperationResult.Ok(proposedFileName);
        }

        OperationResult<string> suffix = NamingPatternRenderer.Render(
            patterns.CollisionSuffixPattern, NamingPatternRenderer.ForSequence(sequence));
        if (suffix.IsFailure)
        {
            return OperationResult.Fail<string>(suffix.Failure);
        }

        int dot = proposedFileName.LastIndexOf('.');
        string stem = dot >= 0 ? proposedFileName[..dot] : proposedFileName;
        string extension = dot >= 0 ? proposedFileName[dot..] : string.Empty;
        return OperationResult.Ok(stem + suffix.Value + extension);
    }
}
