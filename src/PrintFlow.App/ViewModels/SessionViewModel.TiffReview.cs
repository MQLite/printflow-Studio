using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>Which of the three production-TIFF representations is on screen (SCRUM-11104 §5).</summary>
/// <remarks>
/// Ephemeral presentation state, exactly like <see cref="ReviewComparisonMode"/>. Nothing about
/// which mode an operator was looking at is persisted, and nothing about it reaches a review
/// decision — approving is approving the file, not the picture (§36, §39).
/// </remarks>
public enum TiffReviewMode
{
    /// <summary>The CMYK content, converted for screen.</summary>
    Colour,

    /// <summary>The validated W1 spot channel on its own.</summary>
    WhiteInk,

    /// <summary>The colour content with white-ink coverage marked over it.</summary>
    Overlay,
}

/// <summary>
/// One line of the production metadata block (SCRUM-11104 §25).
/// </summary>
/// <remarks>
/// <paramref name="Key"/> is stable English and is never shown: it is what the view turns into an
/// <c>AutomationProperties.AutomationId</c>, so a test or a screen reader addresses the row by
/// identity while an operator reads a localised label beside a localised value. Putting the two
/// in one record is what keeps them from drifting apart (§29, §31, §32).
/// </remarks>
public sealed record TiffMetadataRow(string Key, string Label, string Value);

public sealed partial class SessionViewModel
{
    private TiffReviewPayload? _tiffReview;

    /// <summary>
    /// Which representation is showing. Presentation only (§36).
    /// </summary>
    /// <remarks>
    /// Deliberately not reset when the mode changes and deliberately not persisted. It <i>is</i>
    /// reset to Colour when a new payload arrives, because a new payload is a different output
    /// and starting a fresh review in the mode the previous one ended in would be a surprising
    /// place to begin.
    /// </remarks>
    [ObservableProperty] private TiffReviewMode _tiffReviewMode;

    /// <summary>The production facts an operator reads beside the decision (§25).</summary>
    public ObservableCollection<TiffMetadataRow> TiffMetadata { get; } = [];

    /// <summary>Whether the specialist TIFF review surface has something to show.</summary>
    /// <remarks>
    /// False for every artefact that is not a validated production output, and false for a
    /// production output whose bytes could not be decoded — in which case the generic review
    /// panes stay up and the workflow controls are untouched, exactly as a failed preview has
    /// always behaved (§17, §40).
    /// </remarks>
    public bool HasTiffReview => _tiffReview is not null;

    /// <summary>Why the specialist surface is absent, or null when it is present.</summary>
    public string? TiffReviewUnavailable { get; private set; }

    public bool IsTiffReviewUnavailable => TiffReviewUnavailable is not null;

    // --- The three representations -------------------------------------------------------
    //
    // All three are held at once. A mode switch changes which of the already-decoded bitmaps is
    // bound, never which file is read: one decode, three payloads (§15).

    public ReadOnlyMemory<byte> TiffColourPayload => _tiffReview?.ColourPayload ?? ReadOnlyMemory<byte>.Empty;

    public ReadOnlyMemory<byte> TiffWhiteInkPayload => _tiffReview?.WhiteInkPayload ?? ReadOnlyMemory<byte>.Empty;

    public ReadOnlyMemory<byte> TiffOverlayPayload => _tiffReview?.OverlayPayload ?? ReadOnlyMemory<byte>.Empty;

    /// <summary>The payload dimensions every mode shares, so the surface lays out once (§35).</summary>
    public int TiffPreviewPixelWidth => _tiffReview?.PreviewPixelWidth ?? 0;

    /// <inheritdoc cref="TiffPreviewPixelWidth" />
    public int TiffPreviewPixelHeight => _tiffReview?.PreviewPixelHeight ?? 0;

    /// <summary>The identity the payload on screen is bound to, for a test to check against.</summary>
    /// <remarks>
    /// Exposed as text rather than as ids because a view model is not a place to hand out domain
    /// identities, and because the point of the property is that it can be compared with what was
    /// persisted after a mode switch (§49).
    /// </remarks>
    public string? TiffReviewPrintOutputId => _tiffReview?.PrintOutputId.Value.ToString();

    /// <inheritdoc cref="TiffReviewPrintOutputId" />
    public string? TiffReviewSha256 => _tiffReview?.Sha256.Value;

    /// <summary>The managed TIFF's own absolute path (§23).</summary>
    public string? TiffOutputPath => _tiffReview?.OutputPath;

    /// <summary>The managed TIFF's file name, shown prominently beside the full path (§23).</summary>
    public string? TiffOutputFileName => _tiffReview?.FileName;

    // --- Labels --------------------------------------------------------------------------

    public string TiffReviewModeHeading => Strings.Session_TiffModeHeading;

    public string TiffColourModeLabel => Strings.Session_TiffModeColour;

    public string TiffWhiteInkModeLabel => Strings.Session_TiffModeWhiteInk;

    public string TiffOverlayModeLabel => Strings.Session_TiffModeOverlay;

    /// <summary>The accessible name of the image itself, which changes with the mode (§31).</summary>
    public string TiffPreviewAccessibleName => TiffReviewMode switch
    {
        TiffReviewMode.WhiteInk => Strings.Session_TiffWhiteInkPreviewName,
        TiffReviewMode.Overlay => Strings.Session_TiffOverlayPreviewName,
        _ => Strings.Session_TiffColourPreviewName,
    };

    /// <summary>
    /// What the picture currently on screen means (§7, §12).
    /// </summary>
    /// <remarks>
    /// It changes with the mode because each mode makes a different, and differently limited,
    /// claim. The colour legend is the one that matters most: an uncalibrated screen conversion
    /// of separated CMYK must never be read as evidence of printed colour, and the only reliable
    /// place to say so is beside the picture.
    /// </remarks>
    public string TiffPreviewLegend => TiffReviewMode switch
    {
        TiffReviewMode.WhiteInk => Strings.Session_TiffWhiteInkLegend,
        TiffReviewMode.Overlay => Strings.Session_TiffOverlayLegend,
        _ => Strings.Session_TiffColourLegend,
    };

    public string TiffProductionHeading => Strings.Session_TiffProductionHeading;

    public string TiffOutputPathLabel => Strings.Session_TiffLabelOutputPath;

    /// <summary>Pixel figures, plus a note when the payloads were reduced to fit the screen.</summary>
    public string TiffPreviewDetail
    {
        get
        {
            if (_tiffReview is not { } review)
            {
                return string.Empty;
            }

            string pixels = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_PreviewPixels,
                review.PixelWidth,
                review.PixelHeight);

            return review.IsDownsampledForDisplay
                ? $"{pixels} · {Strings.Session_PreviewReduced}"
                : pixels;
        }
    }

    public bool IsTiffColourMode
    {
        get => TiffReviewMode == TiffReviewMode.Colour;
        set { if (value) TiffReviewMode = TiffReviewMode.Colour; }
    }

    public bool IsTiffWhiteInkMode
    {
        get => TiffReviewMode == TiffReviewMode.WhiteInk;
        set { if (value) TiffReviewMode = TiffReviewMode.WhiteInk; }
    }

    public bool IsTiffOverlayMode
    {
        get => TiffReviewMode == TiffReviewMode.Overlay;
        set { if (value) TiffReviewMode = TiffReviewMode.Overlay; }
    }

    /// <summary>
    /// Nothing but presentation changes (§36).
    /// </summary>
    /// <remarks>
    /// The absence of anything else in this method is the whole assurance. There is no command
    /// issued, no repository call, no payload reload and no viewport reset — which is also what
    /// keeps zoom and pan where the operator left them as they move between modes (§34).
    /// </remarks>
    partial void OnTiffReviewModeChanged(TiffReviewMode value)
    {
        OnPropertyChanged(nameof(IsTiffColourMode));
        OnPropertyChanged(nameof(IsTiffWhiteInkMode));
        OnPropertyChanged(nameof(IsTiffOverlayMode));
        OnPropertyChanged(nameof(TiffPreviewAccessibleName));
        OnPropertyChanged(nameof(TiffPreviewLegend));
    }

    /// <summary>
    /// Asks the specialist seam for the output under review, once per state change (§15, §38).
    /// </summary>
    /// <remarks>
    /// Keyed on nothing: it simply requests the payload for the Revision the screen is showing,
    /// and the payload that comes back carries the <c>PrintOutput</c> identity and hash it was
    /// decoded from. A session holding several sizes therefore cannot show one output's pixels
    /// beside another's metadata — there is no cache to key wrongly, because the only thing held
    /// is the payload for the artefact currently on screen (§38).
    /// <para>
    /// A failure leaves <see cref="HasTiffReview"/> false and sets nothing else. No
    /// <see cref="Notice"/>, no command, no reload: a decoder that could not draw a container has
    /// said nothing about whether the bytes on disk are the ones about to be approved, and a
    /// mutated file that fails the hash check must leave the review panel exactly as
    /// unapprovable-by-mistake as it was (§17, §40).
    /// </para>
    /// </remarks>
    private async Task LoadTiffReviewAsync(
        Workflow.Services.SessionView session, int generation, CancellationToken cancellationToken)
    {
        if (!IsProductionTiffReview || session.CurrentArtefact is not { } current)
        {
            return;
        }

        OperationResult<TiffReviewPayload> loaded = await _tiffReviews
            .GetReviewAsync(session.Id, current.RevisionId, cancellationToken)
            .ConfigureAwait(true);

        if (generation != _previewGeneration)
        {
            return;
        }

        if (loaded.IsFailure)
        {
            TiffReviewUnavailable = Strings.Session_TiffPreviewUnavailable;
            OnPropertyChanged(nameof(TiffReviewUnavailable));
            OnPropertyChanged(nameof(IsTiffReviewUnavailable));
            return;
        }

        _tiffReview = loaded.Value;
        TiffReviewUnavailable = null;
        TiffReviewMode = TiffReviewMode.Colour;
        BuildTiffMetadata(loaded.Value);
        NotifyTiffReviewChanged();
    }

    /// <summary>Drops the payload so its bitmaps become collectable at once.</summary>
    private void ClearTiffReview()
    {
        if (_tiffReview is null && TiffReviewUnavailable is null && TiffMetadata.Count == 0)
        {
            return;
        }

        _tiffReview = null;
        TiffReviewUnavailable = null;
        TiffMetadata.Clear();
        NotifyTiffReviewChanged();
    }

    private void NotifyTiffReviewChanged()
    {
        OnPropertyChanged(nameof(HasTiffReview));
        OnPropertyChanged(nameof(TiffReviewUnavailable));
        OnPropertyChanged(nameof(IsTiffReviewUnavailable));
        OnPropertyChanged(nameof(TiffColourPayload));
        OnPropertyChanged(nameof(TiffWhiteInkPayload));
        OnPropertyChanged(nameof(TiffOverlayPayload));
        OnPropertyChanged(nameof(TiffPreviewPixelWidth));
        OnPropertyChanged(nameof(TiffPreviewPixelHeight));
        OnPropertyChanged(nameof(TiffPreviewDetail));
        OnPropertyChanged(nameof(TiffReviewPrintOutputId));
        OnPropertyChanged(nameof(TiffReviewSha256));
        OnPropertyChanged(nameof(TiffOutputPath));
        OnPropertyChanged(nameof(TiffOutputFileName));
        OnPropertyChanged(nameof(TiffPreviewAccessibleName));
        OnPropertyChanged(nameof(TiffPreviewLegend));
    }

    /// <summary>
    /// The complete production metadata block, from what is actually recorded (§25).
    /// </summary>
    /// <remarks>
    /// Rows a payload cannot answer are absent rather than blank or guessed. Effective source
    /// resolution needs the producing attempt's preparation, and the enlargement row is written
    /// only when an enlargement was actually required — a job that never needed authority is not
    /// improved by a line saying so (§22, §25).
    /// </remarks>
    private void BuildTiffMetadata(TiffReviewPayload review)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        TiffMetadata.Clear();

        Add("OutputFile", Strings.Session_TiffLabelOutputFile, review.FileName);
        Add("OutputPath", Strings.Session_TiffLabelOutputPath, review.OutputPath);
        Add("Pixels", Strings.Session_TiffLabelPixelDimensions, string.Format(
            culture, Strings.Session_TiffValuePixels, review.PixelWidth, review.PixelHeight));
        Add("Physical", Strings.Session_TiffLabelPhysicalSize, string.Format(
            culture, Strings.Session_TiffValuePhysical, review.PhysicalWidthMm, review.PhysicalHeightMm));
        Add("Resolution", Strings.Session_TiffLabelResolution, string.Format(
            culture, Strings.Session_TiffValueResolution, review.XResolutionDpi, review.YResolutionDpi));

        if (review.EffectiveResolution is { } effective)
        {
            string value = string.Format(
                culture,
                Strings.Session_TiffValueEffectiveDpi,
                effective.EffectiveDpiX,
                effective.EffectiveDpiY,
                effective.SourcePixelWidth,
                effective.SourcePixelHeight);

            if (effective.IsBelowProductionResolution)
            {
                value += string.Format(
                    culture, Strings.Session_TiffValueBelowProduction, effective.OutputDpi);
            }

            Add("EffectiveDpi", Strings.Session_TiffLabelEffectiveDpi, value);

            if (effective.EnlargementAuthorityRequired)
            {
                Add("Enlargement", Strings.Session_TiffLabelEnlargement,
                    effective.EnlargementAuthorised
                        ? Strings.Session_TiffValueEnlargementAuthorised
                        : Strings.Session_TiffValueEnlargementUnauthorised);
            }
        }

        Add("Colour", Strings.Session_TiffLabelColourMode, string.Format(
            culture, Strings.Session_TiffValueColourMode,
            review.ColourMode, review.BitsPerSample, review.InkChannelCount));
        Add("WhiteInk", Strings.Session_TiffLabelWhiteInk, string.Format(
            culture, Strings.Session_TiffValueWhiteInk,
            review.WhiteInkChannelName,
            DisplayNames.WhiteUnderbaseBranch(review.Branch),
            review.WhiteInkSampleCount));

        // The signed configuration this output was made with, which the original acceptance
        // criteria name alongside the geometry: the preset identity and the manifest hash that
        // pins it exactly. Never the manifest's contents, and never a claim that the colour
        // settings were re-verified here.
        //
        // The manifest *version* is deliberately absent. The PrintOutput row persists the preset
        // id and hash and not the version, so a row read back from the database carries the
        // literal "unknown" for it (Mappers.cs) — and a metadata block that printed "unknown"
        // beside a real hash would be stating a non-fact where §25 requires either an
        // authoritative value or nothing. The hash identifies the manifest more exactly than a
        // version string would in any case.
        Add("Preset", Strings.Session_TiffLabelPreset, string.Format(
            culture, Strings.Session_TiffValuePreset,
            review.Preset.PresetId, review.Preset.ManifestSha256.ShortForm));

        Add("Sha256", Strings.Session_TiffLabelHash, Abbreviated(review.Sha256.Value));

        void Add(string key, string label, string value) =>
            TiffMetadata.Add(new TiffMetadataRow(key, label, value));
    }

    /// <summary>
    /// A hash an operator can actually compare, without asking them to compare it (§28).
    /// </summary>
    /// <remarks>
    /// Head and tail with an ellipsis between, because that is the form a person can match
    /// against a support ticket or a file listing at a glance. It is traceability, not
    /// verification: the approval is already bound to the full hash, and nothing an operator
    /// reads here is what makes that true.
    /// </remarks>
    private static string Abbreviated(string hash) =>
        hash.Length <= 20 ? hash : $"{hash[..8]}…{hash[^8..]}";
}
