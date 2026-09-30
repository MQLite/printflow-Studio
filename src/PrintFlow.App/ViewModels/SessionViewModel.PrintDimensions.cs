using PrintFlow.App.Resources;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>A localized fact with a stable identity for assistive technology.</summary>
public sealed record PrintDimensionsPreflightRow(string Key, string Label, string Value)
{
    public string AutomationId => "Session.PrintDimensions." + Key;
    public string AccessibleName => $"{Label}: {Value}";
}

public sealed partial class SessionViewModel
{
    private int _preflightGeneration;
    private bool _draftCustomSizePreviewFailed;

    public PrintDimensionsPreflight? Preflight { get; private set; }
    public IReadOnlyList<PrintDimensionsPreflightRow> PreflightRows { get; private set; } = [];
    public bool HasPrintDimensionsPreflight => Preflight is not null;
    public bool IsDraftPreflight { get; private set; }
    public string PreflightHeading => Strings.Session_PreflightHeading;
    public string PreflightDraftHint => Strings.Session_PreflightDraftHint;

    /// <summary>The latest read-only proposal query, also used by deterministic UI tests.</summary>
    public Task PreflightLoaded { get; private set; } = Task.CompletedTask;

    private void ShowCommittedPreflight(SessionView session)
    {
        // A response for a previous size or upstream artwork cannot relabel the new view.
        _preflightGeneration++;
        _draftCustomSizePreviewFailed = false;
        NotifyCustomSizeInputHint();
        PreflightLoaded = Task.CompletedTask;
        ShowPreflight(session.Preflight, isDraft: false);
    }

    private void RequestDraftPreflight()
    {
        if (_session is not { CanSetMaximumBounds: true } session)
        {
            return;
        }

        int generation = ++_preflightGeneration;
        _draftCustomSizePreviewFailed = false;
        NotifyCustomSizeInputHint();
        ShowPreflight(null, isDraft: true);
        WorkflowCommand? command = IsChoosingCustomSize
            ? ReadCustomSizeCommand()
            : ReadMaximumBoundsCommand();

        PreflightLoaded = command is null
            ? Task.CompletedTask
            : LoadDraftPreflightAsync(session, command, generation);
    }

    private async Task LoadDraftPreflightAsync(SessionView session, WorkflowCommand command, int generation)
    {
        OperationResult<PrintDimensionsPreflight> result = await _sessions
            .PreviewPrintDimensionsAsync(session.Id, command, CancellationToken.None)
            .ConfigureAwait(true);
        if (generation != _preflightGeneration || !ReferenceEquals(session, _session))
        {
            return;
        }

        _draftCustomSizePreviewFailed = IsChoosingCustomSize && result.IsFailure;
        ShowPreflight(result.IsSuccess ? result.Value : null, isDraft: true);
        NotifyCustomSizeInputHint();
    }

    private void ShowPreflight(PrintDimensionsPreflight? preflight, bool isDraft)
    {
        Preflight = preflight;
        IsDraftPreflight = isDraft && preflight is not null;
        List<PrintDimensionsPreflightRow> rows = [];
        if (preflight is { } facts)
        {
            string Pixels(int width, int height) => string.Format(
                OperatorCulture.Current, Strings.Session_TiffValuePixels, width, height);
            void Add(string key, string label, string value) => rows.Add(new(key, label, value));

            Add("SourcePixels", Strings.Session_PreflightSourcePixels,
                Pixels(facts.SourcePixelWidth, facts.SourcePixelHeight));
            if (facts.ArtworkBounds is { } artwork)
            {
                Add("GraphicBounds", facts.GraphicBoundsKind == GraphicBoundsKind.ManualCrop
                        ? Strings.Session_PreflightSelectedArtwork : Strings.Session_PreflightArtworkContent,
                    Pixels(artwork.Width, artwork.Height));
            }
            else
            {
                Add("GraphicBounds", Strings.Session_PreflightGraphicBounds, facts.GraphicBoundsKind switch
                {
                    GraphicBoundsKind.FullOriginalCanvas => Strings.Session_PreflightFullOriginalCanvas,
                    GraphicBoundsKind.NoRelevantGeometry => Strings.Session_PreflightNoCrop,
                    _ => Strings.Session_PreflightGeometryUnavailable,
                });
            }

            Add("FinalCanvas", Strings.Session_PreflightFinalCanvas,
                Pixels(facts.FinalCanvasBounds?.Width ?? facts.SourcePixelWidth,
                    facts.FinalCanvasBounds?.Height ?? facts.SourcePixelHeight));
            Add("PrintSize", Strings.Session_PreflightPrintSize, string.Format(
                OperatorCulture.Current, Strings.Session_TiffValuePhysical, facts.PhysicalWidthMm, facts.PhysicalHeightMm));
            Add("OutputPixels", Strings.Session_PreflightOutputPixels, Pixels(facts.OutputPixelWidth, facts.OutputPixelHeight));
            Add("EffectiveDpi", Strings.Session_TiffLabelEffectiveDpi, string.Format(
                OperatorCulture.Current, Strings.Session_PreflightPpi, facts.EffectiveSourcePpiX, facts.EffectiveSourcePpiY));
            Add("OutputDpi", Strings.Session_PreflightOutputDpi, string.Format(
                OperatorCulture.Current, Strings.Session_PreflightOutputPpi, facts.ProductionOutputPpi));
            Add("EnlargementStatus", Strings.Session_PreflightStatus, !facts.RequiresEnlargement
                ? Strings.Session_PreflightNoEnlargement
                : facts.EnlargementAuthorised ? Strings.Session_PreflightEnlargementAuthorised
                : Strings.Session_PreflightEnlargementRequired);
        }

        PreflightRows = rows;
        OnPropertyChanged(nameof(Preflight));
        OnPropertyChanged(nameof(PreflightRows));
        OnPropertyChanged(nameof(HasPrintDimensionsPreflight));
        OnPropertyChanged(nameof(IsDraftPreflight));
        OnPropertyChanged(nameof(PrintSizeSummary));
        OnPropertyChanged(nameof(PreflightEnlargementNote));
        OnPropertyChanged(nameof(HasPreflightEnlargementNote));
        OnPropertyChanged(nameof(EnlargementPlainWarning));
    }

    /// <summary>Rebuilds the technical rows in the current language from the preflight already shown.</summary>
    /// <remarks>No query, no command and no new generation: a pending draft query stays current.</remarks>
    private void RefreshPreflightLanguage()
    {
        ShowPreflight(Preflight, IsDraftPreflight);
        NotifyCustomSizeInputHint();
    }

    // --- Beginner guidance (SCRUM-11150) -------------------------------------------------
    //
    // Every sentence below is formatted from facts the screen already holds: the preflight the
    // session service projected from the one preparation plan, and the existing enlargement
    // state. Nothing here fits an image, picks an edge or converts millimetres.

    /// <summary>When a preset is the right choice, and that its size is a maximum.</summary>
    public string PresetModeHelp => Strings.Session_SizeHelpPreset;

    /// <summary>When a custom size is the right choice, naming the existing edge choices.</summary>
    public string CustomModeHelp => string.Format(
        OperatorCulture.Current,
        Strings.Session_SizeHelpCustom,
        CustomSizeLabel,
        DisplayNames.TargetEdge(TargetEdge.Width),
        DisplayNames.TargetEdge(TargetEdge.Height),
        DisplayNames.TargetEdge(TargetEdge.LongEdge));

    /// <summary>Heading of the collapsed pixel and PPI facts.</summary>
    public string SizeTechnicalDetailsLabel => Strings.Session_SizeTechnicalDetails;

    /// <summary>
    /// The shown preflight in words: its physical size, what decided it, and that proportions
    /// are kept. Empty while no preflight is shown, so a pending or invalid draft shows nothing.
    /// </summary>
    public string PrintSizeSummary
    {
        get
        {
            if (Preflight is not { } facts)
            {
                return string.Empty;
            }

            List<string> sentences =
            [
                string.Format(
                    OperatorCulture.Current,
                    IsDraftPreflight ? Strings.Session_SizeSummaryDraft : Strings.Session_SizeSummaryCurrent,
                    facts.PhysicalWidthMm,
                    facts.PhysicalHeightMm),
            ];
            string? governor = GovernorSentence(facts);
            if (governor is not null)
            {
                sentences.Add(governor);
            }

            sentences.Add(Strings.Session_SizeSummaryProportions);
            return JoinSentences(sentences);
        }
    }

    /// <summary>English sentences take a space between them; Chinese ones, ending in "。", do not.</summary>
    private static string JoinSentences(IReadOnlyList<string> sentences) =>
        string.Concat(sentences.Select((sentence, i) => i > 0 && !sentences[i - 1].EndsWith('。') ? " " + sentence : sentence));

    /// <summary>A proposed size that would need enlarging, said before anything is confirmed.</summary>
    public string PreflightEnlargementNote =>
        Preflight is { RequiresEnlargement: true } && IsDraftPreflight
            ? Strings.Session_SizeDraftEnlargement
            : string.Empty;

    public bool HasPreflightEnlargementNote => PreflightEnlargementNote.Length > 0;

    /// <summary>
    /// The existing enlargement offer in plain words. Shown by the same condition as before and
    /// beside the same two buttons; the scale and PPI figures stay in the technical details.
    /// </summary>
    public string EnlargementPlainWarning => NeedsEnlargementAuthority
        ? string.Format(
            OperatorCulture.Current,
            Strings.Session_SizeEnlargementPlain,
            ChangeSizeLabel,
            ContinueWithSizeLabel,
            RunStepLabel)
        : string.Empty;

    /// <summary>
    /// The existing custom-size validation, beside the input while typed text is not yet a usable
    /// size. The same rule as Confirm: <see cref="ReadCustomSizeCommand"/>.
    /// </summary>
    public string CustomSizeInputHint =>
        !IsChoosingCustomSize || string.IsNullOrWhiteSpace(CustomMillimetresText)
            ? string.Empty
            : ReadCustomSizeCommand() is null
                ? Strings.Session_TargetSizeInvalid
                : _draftCustomSizePreviewFailed
                    ? Strings.Session_TargetSizeUnavailable
                    : string.Empty;

    public bool HasCustomSizeInputHint => CustomSizeInputHint.Length > 0;

    private static string? GovernorSentence(PrintDimensionsPreflight facts) => facts.Governor switch
    {
        PrintSizeGovernor.WithinLimits => Strings.Session_SizeGovernorWithinLimits,
        PrintSizeGovernor.LimitReached => facts.GoverningEdge switch
        {
            LimitingEdge.Width => Strings.Session_SizeGovernorLimitWidth,
            LimitingEdge.Height => Strings.Session_SizeGovernorLimitHeight,
            _ => null,
        },
        PrintSizeGovernor.SelectedEdge => (facts.SelectedTargetEdge, facts.GoverningEdge) switch
        {
            (TargetEdge.Width, _) => Strings.Session_SizeGovernorEdgeWidth,
            (TargetEdge.Height, _) => Strings.Session_SizeGovernorEdgeHeight,
            (TargetEdge.LongEdge, LimitingEdge.Width) => Strings.Session_SizeGovernorLongEdgeWidth,
            (TargetEdge.LongEdge, LimitingEdge.Height) => Strings.Session_SizeGovernorLongEdgeHeight,
            _ => null,
        },
        _ => null,
    };

    private void NotifyCustomSizeInputHint()
    {
        OnPropertyChanged(nameof(CustomSizeInputHint));
        OnPropertyChanged(nameof(HasCustomSizeInputHint));
    }

    partial void OnSelectedTargetEdgeChoiceChanged(TargetEdgeChoice? value)
    {
        NotifyCustomSizeInputHint();
        RequestDraftPreflight();
    }

    partial void OnCustomMillimetresTextChanged(string? value)
    {
        NotifyCustomSizeInputHint();
        RequestDraftPreflight();
    }

    partial void OnIsChoosingCustomSizeChanged(bool value)
    {
        NotifyCustomSizeInputHint();
        RequestDraftPreflight();
    }
}
