using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed partial class AiPromptWorkbenchViewModel
{
    private AiAuthoringDraft? conversionSource;
    private MemorialReviewState? memorialReview;
    private bool memorialTaskRunning;
    private DecodedPreview? comparisonImage;
    private string? comparisonLabel;

    public MemorialRenderResult? MemorialResult { get; private set; }
    public DecodedPreview? MemorialResultImage { get; private set; }
    public IReadOnlyList<MemorialResultChoice> MemorialResults { get; private set; } = [];
    public IReadOnlyList<MemorialResultChoice> MemorialSourceResults { get; private set; } = [];
    public IReadOnlyList<MemorialCheckRow> MemorialChecks { get; private set; } = [];
    public DecodedPreview? MemorialComparisonImage => comparisonImage ?? Photos.FirstOrDefault(p => p.Preview is not null)?.Preview;
    public string MemorialComparisonLabel => comparisonLabel ?? L("SourceReference");
    public IReadOnlyList<MemorialProofSource> MemorialProofSources =>
        (comparisonImage is null ? Array.Empty<MemorialProofSource>() : [new(MemorialComparisonLabel, comparisonImage)])
        .Concat(Photos.Where(p => p.Preview is not null).Select(p => new MemorialProofSource(p.Role + " · " + p.FileName, p.Preview!))).ToArray();
    public double MemorialProofSafeRatio => MemorialResult is { } result && IsCurrentResult(result) &&
        Draft?.Layout?.Canvas is { SafeDiameterMm: { } safe, OuterDiameterMm: > 0 } canvas ? safe / canvas.OuterDiameterMm.Value : 0;
    public bool HasMemorialBadgeGuide => MemorialProofSafeRatio > 0;
    public string MemorialResultLabel => MemorialResult is { } result ? ResultLabel(result) : string.Empty;
    public bool IsMemorialResultVisible => IsDetailed && (IsStep5 || IsStep6) && MemorialResultImage is not null;
    public bool IsMemorialResultHistorical => MemorialResult is { } result && !IsCurrentResult(result);
    public bool IsMemorialResultAccepted => IsCurrentMemorialReview && memorialReview!.Accepted;
    public bool IsCurrentMemorialReview => MemorialResult is { File: { Purpose: MemorialRenderPurpose.DesignMaterial } } result &&
        IsCurrentResult(result) && memorialReview?.Render == result;
    public bool CanCheckMemorialResult => CanEdit && IsCurrentMemorialReview && !IsMemorialResultAccepted;
    public bool CanAcceptMemorialResult => CanCheckMemorialResult && MemorialChecks.Count == 5 && MemorialChecks.All(r => r.Checked);
    public bool CanExportMemorialPng => CanEdit && IsMemorialResultAccepted;
    // Design review is independent from the original AI result-to-session capability.
    public bool CanImportMemorialResult => false;
    public bool IsMemorialTaskRunning
    {
        get => memorialTaskRunning;
        private set => SetProperty(ref memorialTaskRunning, value);
    }
    public bool IsMemorialConversionPending => conversionSource is not null;
    public bool CanBeginMemorialConversion => CanEdit && IsMemorialLayoutAvailable && !HasUnsavedChanges && conversionSource is null;
    public bool HasMemorialParent => Draft?.ParentDraftId is not null && Draft.ParentDraftRevision is not null;
    public string? MemorialConversionLabel => conversionSource is { } source ? string.Format(CultureInfo.CurrentCulture, L("ConversionDetails"),
        source.Revision, CanvasLabel(source.Layout?.Canvas ?? MemorialLayoutEditor.Preset(source.Product)!),
        CanvasLabel(MemorialLayoutEditor.Preset(OtherProduct(source.Product))!)) : null;

    public IAsyncRelayCommand RenderMemorialPreviewCommand { get; private set; } = null!;
    public IAsyncRelayCommand RenderMemorialMaterialCommand { get; private set; } = null!;
    public IRelayCommand CancelMemorialTaskCommand { get; private set; } = null!;
    public IAsyncRelayCommand RefreshMemorialResultsCommand { get; private set; } = null!;
    public IAsyncRelayCommand<MemorialResultChoice> OpenMemorialResultCommand { get; private set; } = null!;
    public IAsyncRelayCommand<MemorialResultChoice> CompareMemorialSourceCommand { get; private set; } = null!;
    public IAsyncRelayCommand<MemorialCheckRow> ToggleMemorialCheckCommand { get; private set; } = null!;
    public IAsyncRelayCommand AcceptMemorialResultCommand { get; private set; } = null!;
    public IAsyncRelayCommand ExportMemorialPngCommand { get; private set; } = null!;
    public IRelayCommand BeginMemorialConversionCommand { get; private set; } = null!;
    public IRelayCommand CancelMemorialConversionCommand { get; private set; } = null!;
    public IAsyncRelayCommand ConfirmMemorialConversionCommand { get; private set; } = null!;

    private void InitializeMemorialResultCommands()
    {
        RenderMemorialPreviewCommand = new AsyncRelayCommand(ct => RenderMemorialAsync(MemorialRenderPurpose.Preview, ct));
        RenderMemorialMaterialCommand = new AsyncRelayCommand(ct => RenderMemorialAsync(MemorialRenderPurpose.DesignMaterial, ct));
        CancelMemorialTaskCommand = new RelayCommand(() =>
        {
            RenderMemorialPreviewCommand.Cancel(); RenderMemorialMaterialCommand.Cancel(); ConfirmMemorialConversionCommand.Cancel();
        });
        RefreshMemorialResultsCommand = new AsyncRelayCommand(RefreshMemorialResultsAsync);
        OpenMemorialResultCommand = new AsyncRelayCommand<MemorialResultChoice>(OpenMemorialResultAsync);
        CompareMemorialSourceCommand = new AsyncRelayCommand<MemorialResultChoice>(CompareMemorialSourceAsync);
        ToggleMemorialCheckCommand = new AsyncRelayCommand<MemorialCheckRow>(ToggleMemorialCheckAsync);
        AcceptMemorialResultCommand = new AsyncRelayCommand(AcceptMemorialResultAsync);
        ExportMemorialPngCommand = new AsyncRelayCommand(ExportMemorialPngAsync);
        BeginMemorialConversionCommand = new RelayCommand(() =>
        {
            if (!CanBeginMemorialConversion) return;
            conversionSource = Draft; NotifyMemorial();
        });
        CancelMemorialConversionCommand = new RelayCommand(ClearMemorialConversion);
        ConfirmMemorialConversionCommand = new AsyncRelayCommand(ConfirmMemorialConversionAsync);
    }

    private async Task RenderMemorialAsync(MemorialRenderPurpose purpose, CancellationToken ct)
    {
        if (memorial is null || !CanPreviewLayout || Draft is not { } source) return;
        long capturedEpoch = epoch, generation = editGeneration;
        await RunAsync(async () =>
        {
            if (!IsCurrent(capturedEpoch) || Draft != source || generation != editGeneration || HasUnsavedChanges) return;
            IsMemorialTaskRunning = true;
            try
            {
                var result = await memorial.Composition.RenderAsync(source.Id, source.Revision, purpose, ct);
                if (!IsCurrent(capturedEpoch) || Draft != source || generation != editGeneration) return;
                if (ct.IsCancellationRequested || result.IsFailure)
                { Error = L(ct.IsCancellationRequested || result.IsFailure && result.Failure.Code == FailureCode.Cancelled ? "Cancelled" : "RenderFailed"); return; }
                if (await PresentMemorialResultAsync(result.Value, capturedEpoch, generation, ct))
                {
                    if (purpose == MemorialRenderPurpose.DesignMaterial) DetailedStep = 6;
                    Status = L("RenderReady");
                }
            }
            finally { IsMemorialTaskRunning = false; }
        }, ct);
    }

    private async Task<bool> PresentMemorialResultAsync(MemorialRenderResult result, long capturedEpoch, long generation, CancellationToken ct)
    {
        var loaded = await memorial!.Preview.LoadAsync(result, ct);
        if (!IsCurrent(capturedEpoch) || Draft?.Id != RenderReference(result).DraftId || generation != editGeneration || ct.IsCancellationRequested) return false;
        if (loaded.IsFailure) { Error = L("ResultUnavailable"); return false; }
        MemorialReviewState? checkedResult = null;
        if (RenderReference(result).Purpose == MemorialRenderPurpose.DesignMaterial && IsCurrentResult(result))
        {
            var review = await memorial.Reviews.ReadAsync(result, ct);
            if (!IsCurrent(capturedEpoch) || Draft?.Id != RenderReference(result).DraftId || generation != editGeneration || ct.IsCancellationRequested) return false;
            if (review.IsSuccess) checkedResult = review.Value;
            else Error = L("ReviewUnavailable");
        }
        MemorialResult = result; MemorialResultImage = loaded.Value;
        PresentMemorialChecks(checkedResult);
        return true;
    }

    private async Task RefreshMemorialResultsAsync()
    {
        if (memorial is null || !CanEdit || Draft is not { } source) return;
        long capturedEpoch = epoch;
        await RunAsync(async () =>
        {
            if (!IsCurrent(capturedEpoch) || Draft != source) return;
            var list = await memorial.Renders.ListAsync(source.Id, default);
            if (!IsCurrent(capturedEpoch) || Draft != source) return;
            if (list.IsFailure) { Error = L("ResultUnavailable"); return; }
            MemorialResults = list.Value.Select(r => new MemorialResultChoice(r, ResultLabel(r))).ToArray();
            OnPropertyChanged(nameof(MemorialResults));
            if (source.ParentDraftId is not { } parentId || source.ParentDraftRevision is not { } parentRevision) return;
            var parents = await memorial.Renders.ListAsync(parentId, default);
            if (!IsCurrent(capturedEpoch) || Draft != source) return;
            if (parents.IsFailure) { Error = L("SourceUnavailable"); return; }
            MemorialSourceResults = parents.Value.Where(r => r.DraftRevision == parentRevision)
                .Select(r => new MemorialResultChoice(r, ResultLabel(r))).ToArray();
            OnPropertyChanged(nameof(MemorialSourceResults));
        }, default);
    }

    private async Task OpenMemorialResultAsync(MemorialResultChoice? choice)
    {
        if (memorial is null || !CanEdit || choice is null || !MemorialResults.Contains(choice) || Draft?.Id != RenderReference(choice.Result).DraftId) return;
        long capturedEpoch = epoch, generation = editGeneration;
        await RunAsync(async () =>
        {
            if (!IsCurrent(capturedEpoch) || !MemorialResults.Contains(choice) || generation != editGeneration) return;
            if (await PresentMemorialResultAsync(choice.Result, capturedEpoch, generation, default)) DetailedStep = 6;
        }, default);
    }

    private async Task CompareMemorialSourceAsync(MemorialResultChoice? choice)
    {
        if (memorial is null || !CanEdit || choice is null || !MemorialSourceResults.Contains(choice) ||
            Draft?.ParentDraftId != RenderReference(choice.Result).DraftId || Draft.ParentDraftRevision != choice.Result.DraftRevision) return;
        var source = Draft;
        long capturedEpoch = epoch;
        await RunAsync(async () =>
        {
            if (!IsCurrent(capturedEpoch) || Draft != source) return;
            var loaded = await memorial.Preview.LoadAsync(choice.Result, default);
            if (!IsCurrent(capturedEpoch) || Draft != source) return;
            if (loaded.IsFailure) { Error = L("SourceUnavailable"); return; }
            comparisonImage = loaded.Value; comparisonLabel = L("SourceVersion") + " · " + choice.Label;
            NotifyMemorial();
        }, default);
    }

    private Task ToggleMemorialCheckAsync(MemorialCheckRow? row) => row is null || !MemorialChecks.Contains(row) || !CanCheckMemorialResult
        ? Task.CompletedTask : UpdateMemorialReviewAsync(row);

    private Task AcceptMemorialResultAsync() => !CanAcceptMemorialResult ? Task.CompletedTask : UpdateMemorialReviewAsync(null);

    private async Task UpdateMemorialReviewAsync(MemorialCheckRow? row)
    {
        if (memorial is null || memorialReview is not { } before) return;
        long capturedEpoch = epoch, generation = editGeneration;
        await RunAsync(async () =>
        {
            if (!IsCurrent(capturedEpoch) || generation != editGeneration || memorialReview != before || !IsCurrentMemorialReview) return;
            var saved = row is null ? await memorial.Reviews.AcceptAsync(before.Render, before.Revision, default) :
                await memorial.Reviews.SetCheckAsync(before.Render, before.Revision, row.Item, !row.Checked, default);
            if (!IsCurrent(capturedEpoch) || generation != editGeneration || memorialReview != before) return;
            if (saved.IsFailure) { PresentMemorialChecks(before); Error = L("ReviewUnavailable"); return; }
            PresentMemorialChecks(saved.Value);
            if (saved.Value.Accepted) Status = L("Accepted");
        }, default);
    }

    private async Task ExportMemorialPngAsync()
    {
        if (memorial is null || !CanExportMemorialPng || MemorialResult is not { } result) return;
        long capturedEpoch = epoch, generation = editGeneration;
        string? destination = memorial.SavePicker.PickDestination("memorial-" + RenderReference(result).RenderId.ToString("N")[..8] + ".png");
        if (destination is null) return;
        await RunAsync(async () =>
        {
            if (!IsCurrent(capturedEpoch) || generation != editGeneration || MemorialResult != result || !IsMemorialResultAccepted) return;
            var saved = await memorial.Exporter.ExportAsync(result, destination, default);
            if (!IsCurrent(capturedEpoch) || generation != editGeneration) return;
            if (saved.IsFailure) { Error = L("ExportFailed"); return; }
            Status = string.Format(CultureInfo.CurrentCulture, L("Exported"), saved.Value);
        }, default);
    }

    private async Task ConfirmMemorialConversionAsync(CancellationToken ct)
    {
        if (memorial is null || !CanEdit || conversionSource is not { } source || Draft != source || HasUnsavedChanges) return;
        long capturedEpoch = epoch, generation = editGeneration;
        await RunAsync(async () =>
        {
            if (!IsCurrent(capturedEpoch) || generation != editGeneration || Draft != source || conversionSource != source || HasUnsavedChanges) return;
            var priorProof = MemorialResult is { } previous && IsCurrentResult(previous) ? MemorialResultImage : null;
            string? priorLabel = priorProof is null ? null : MemorialResultLabel;
            IsMemorialTaskRunning = true;
            try
            {
                var result = await memorial.Versions.ForkAsync(source.Id, source.Revision, OtherProduct(source.Product), ct);
                if (!IsCurrent(capturedEpoch) || generation != editGeneration || Draft != source) return;
                if (result.IsFailure)
                { Error = L(result.Failure.Code == FailureCode.Cancelled ? "Cancelled" : "ConversionFailed"); return; }
                epoch++; ClearPending(); ClearMemorialConversion();
                History = []; OnPropertyChanged(nameof(History));
                await PresentAsync(result.Value, default);
                comparisonImage = priorProof; comparisonLabel = priorLabel is null ? null : L("SourceVersion") + " · " + priorLabel;
                DetailedStep = AiDetailedReview.GetFirstIncompleteStep(result.Value);
                Status = L("ConversionReady"); NotifyMemorial();
            }
            finally { IsMemorialTaskRunning = false; }
        }, ct);
    }

    private bool IsCurrentResult(MemorialRenderResult result) => Draft?.Id == RenderReference(result).DraftId &&
        Draft.Revision == result.DraftRevision && !HasUnsavedChanges;

    private void PresentMemorialChecks(MemorialReviewState? value)
    {
        memorialReview = value;
        MemorialChecks = value is null ? [] : Enum.GetValues<MemorialReviewItem>().Select(item =>
            new MemorialCheckRow(item, L("Check" + item), value.Checks.GetValueOrDefault(item))).ToArray();
        OnPropertyChanged(nameof(MemorialChecks));
        NotifyMemorial();
    }

    private void ResetMemorialResult()
    {
        MemorialResult = null; MemorialResultImage = null; comparisonImage = null; comparisonLabel = null;
        MemorialResults = []; MemorialSourceResults = [];
        OnPropertyChanged(nameof(MemorialResults)); OnPropertyChanged(nameof(MemorialSourceResults));
        PresentMemorialChecks(null);
        ClearMemorialConversion();
    }

    private void ClearMemorialConversion() { conversionSource = null; NotifyMemorial(); }

    private void NotifyMemorial()
    {
        foreach (string property in new[]
        {
            nameof(IsMemorialLayoutAvailable), nameof(CanPrepareMemorialLayout), nameof(CanSaveMemorialLayout),
            nameof(IsMemorialLayoutEditorStale), nameof(MemorialLayoutEditLabel),
            nameof(CanConfirmMemorialLayout), nameof(CanPreviewLayout), nameof(MemorialCanvasLabel), nameof(MemorialLayoutStatus), nameof(MemorialLayoutIssues),
            nameof(MemorialResult), nameof(MemorialResultImage), nameof(MemorialComparisonImage), nameof(MemorialComparisonLabel), nameof(MemorialResultLabel),
            nameof(IsMemorialResultVisible), nameof(IsMemorialResultHistorical), nameof(IsMemorialResultAccepted), nameof(IsCurrentMemorialReview),
            nameof(MemorialProofSources), nameof(MemorialProofSafeRatio), nameof(HasMemorialBadgeGuide),
            nameof(CanCheckMemorialResult), nameof(CanAcceptMemorialResult), nameof(CanExportMemorialPng),
            nameof(CanBeginMemorialConversion), nameof(IsMemorialConversionPending), nameof(MemorialConversionLabel), nameof(HasMemorialParent),
        }) OnPropertyChanged(property);
    }

    private static AiProductKind OtherProduct(AiProductKind product) => product == AiProductKind.MemorialBadge
        ? AiProductKind.MemorialTshirt : AiProductKind.MemorialBadge;

    private static MemorialRenderRef RenderReference(MemorialRenderResult result) => result.File;

    private static string ResultLabel(MemorialRenderResult result) => string.Format(CultureInfo.CurrentCulture, L("ResultLabel"),
        L(RenderReference(result).Purpose == MemorialRenderPurpose.Preview ? "PreviewKind" : "MaterialKind"), result.DraftRevision,
        result.Facts.PixelWidth, result.Facts.PixelHeight, RenderReference(result).RenderId.ToString("N")[..8]);
}

public sealed record MemorialResultChoice(MemorialRenderResult Result, string Label);
public sealed record MemorialCheckRow(MemorialReviewItem Item, string Label, bool Checked);
public sealed record MemorialProofSource(string Label, DecodedPreview Image);
