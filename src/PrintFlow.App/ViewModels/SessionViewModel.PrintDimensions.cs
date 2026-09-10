using PrintFlow.App.Resources;
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

        ShowPreflight(result.IsSuccess ? result.Value : null, isDraft: true);
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
    }

    partial void OnSelectedTargetEdgeChoiceChanged(TargetEdgeChoice? value) => RequestDraftPreflight();
    partial void OnCustomMillimetresTextChanged(string? value) => RequestDraftPreflight();
    partial void OnIsChoosingCustomSizeChanged(bool value) => RequestDraftPreflight();
}
