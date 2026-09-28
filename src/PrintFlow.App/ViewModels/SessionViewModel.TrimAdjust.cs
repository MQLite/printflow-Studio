using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>A preview-payload rectangle, for display-only cropping of an already decoded preview.</summary>
public readonly record struct PayloadRect(int X, int Y, int Width, int Height);

/// <summary>
/// Adjusting the trim under review with edge and corner handles (SCRUM-11147).
/// </summary>
/// <remarks>
/// Everything here is transient screen state: opening, dragging, nudging, zooming, comparing,
/// restoring and cancelling reach no service and leave no trace. The draft is a rectangle in the
/// pixels of the pre-trim source the workflow layer named, never of the cropped result on review.
/// The only thing that is sent is <see cref="WorkflowCommand.AdjustTrimFromReview"/>, once, from
/// Use this trim, carrying the exact result and source the draft was drawn for.
/// </remarks>
public sealed partial class SessionViewModel
{
    /// <summary>What the open adjustment was drawn for; null when none is open.</summary>
    private TrimAdjustmentView? _trimAdjustTarget;

    /// <summary>The preview generation whose panes have been published.</summary>
    private int _publishedPreviewGeneration = -1;

    /// <summary>Whether the trim review is being adjusted rather than reviewed.</summary>
    [ObservableProperty]
    private bool _isAdjustingTrim;

    /// <summary>Whether the adjustment shows Compare rather than the handles.</summary>
    [ObservableProperty]
    private bool _isComparingTrim;

    /// <summary>Set when a handle position or nudge was refused; the previous boundary was kept.</summary>
    [ObservableProperty]
    private bool _isTrimAdjustInvalid;

    /// <summary>What the open adjustment was drawn for.</summary>
    public TrimAdjustmentView? TrimAdjustTarget => _trimAdjustTarget;

    /// <summary>Whether Adjust trim edges is offered.</summary>
    public bool CanAdjustTrim => _session?.TrimAdjustment is not null && !IsCropping && !IsBusy && _finalSaveOperation is null;

    public string TrimAdjustBeginLabel => Strings.Session_TrimAdjustBegin;
    public string TrimAdjustHeading => Strings.Session_TrimAdjustHeading;
    public string UseThisTrimLabel => Strings.Session_TrimAdjustUse;
    public string RestoreSuggestionLabel => Strings.Session_TrimAdjustRestore;
    public string CancelTrimAdjustLabel => Strings.Session_TrimAdjustCancel;
    public string TrimAdjustViewLabel => Strings.Session_TrimAdjustAdjustView;
    public string TrimCompareViewLabel => Strings.Session_TrimAdjustCompareView;
    public string TrimAdjustCurrentLabel => Strings.Session_TrimAdjustCurrent;
    public string TrimAdjustProposedLabel => Strings.Session_TrimAdjustProposed;
    public string TrimAdjustKeyboardHint => Strings.Session_TrimAdjustKeyboard;
    public string TrimAdjustInvalidNotice => Strings.Session_TrimAdjustInvalid;
    public string TrimAdjustUnchangedNotice => Strings.Session_TrimAdjustUnchanged;
    public string TrimAdjustSourceUnavailableNotice => Strings.Session_TrimAdjustSourceUnavailable;

    /// <summary>
    /// What the operator is asked to do. Trimming changes only the image edges; the customer-design
    /// route adds that print size comes later, and the asset route says nothing about print size.
    /// </summary>
    public string TrimAdjustInstructions => _session?.WorkflowType == WorkflowType.PrepareCustomerDesign
        ? $"{Strings.Session_TrimAdjustInstructions} {Strings.Session_TrimAdjustPrintSizeLater}"
        : Strings.Session_TrimAdjustInstructions;

    /// <summary>The kept area in source pixels.</summary>
    public string TrimAdjustKeptSummary => CropSelection is { } kept
        ? string.Format(CultureInfo.CurrentCulture, Strings.Session_TrimAdjustKept, ManualBounds(kept))
        : string.Empty;

    /// <summary>The automatic suggestion, or a plain statement that there is none.</summary>
    public string TrimAdjustSuggestionSummary => _trimAdjustTarget?.AutomaticSuggestion is { } suggestion
        ? string.Format(CultureInfo.CurrentCulture, Strings.Session_TrimAdjustSuggestion, ManualBounds(suggestion))
        : Strings.Session_TrimAdjustNoSuggestion;

    /// <summary>The unchanged Failed/RetryRequired crop controls (draw, margins, Apply).</summary>
    public bool ShowsManualCropControls => IsCropping && !IsAdjustingTrim;

    /// <summary>The image surface with the outline, shown unless Compare is up.</summary>
    public bool ShowsCropSurface => IsCropping && !IsComparingTrim;

    /// <summary>The side-by-side current result and proposed trim.</summary>
    public bool ShowsTrimCompare => IsAdjustingTrim && IsComparingTrim;

    public bool IsTrimDraftUnchanged => IsAdjustingTrim && CropSelection is { } kept && kept == _trimAdjustTarget?.CurrentBounds;

    public bool CanRestoreAutomaticSuggestion => IsAdjustingTrim && !IsBusy && _trimAdjustTarget?.AutomaticSuggestion is not null;

    public bool CanUseThisTrim => IsAdjustingTrim && !IsBusy && _trimAdjustTarget is { } target &&
        CropSelection is { IsEmpty: false } kept && kept.FitsWithin(target.SourcePixelWidth, target.SourcePixelHeight) &&
        TrimAdjustSourcePane is not null;

    /// <summary>Input identity for Use this trim: a gesture begun elsewhere never submits.</summary>
    public string? UseThisTrimIdentity => IsAdjustingTrim && _trimAdjustTarget is { } t && _session is { } session
        ? $"trim-adjust|{session.Id}|{t.ResultRevisionId}|{t.ResultSha256.Value}|{t.PreTrimRevisionId}|{t.PreTrimSha256.Value}"
        : null;

    /// <summary>The pre-trim source pane, found by exact Revision id and pixel size, or null.</summary>
    public ArtefactPreviewPane? TrimAdjustSourcePane => _trimAdjustTarget is { } t
        ? PreviewPanes.FirstOrDefault(p => p.HasImage && p.RevisionId == t.PreTrimRevisionId &&
            p.SourcePixelWidth == t.SourcePixelWidth && p.SourcePixelHeight == t.SourcePixelHeight)
        : null;

    /// <summary>The result under review, for Compare.</summary>
    public ArtefactPreviewPane? TrimAdjustCurrentPane => _trimAdjustTarget is { } t
        ? PreviewPanes.FirstOrDefault(p => p.HasImage && p.RevisionId == t.ResultRevisionId)
        : null;

    /// <summary>
    /// The previews for this state have arrived and the exact source is not among them. Said
    /// plainly, and Use this trim stays disabled; no other image is substituted.
    /// </summary>
    public bool IsTrimSourceUnavailable => IsAdjustingTrim && _publishedPreviewGeneration == _previewGeneration &&
        TrimAdjustSourcePane is null;

    /// <summary>
    /// The draft mapped into the source preview's payload pixels, rounded outward, for the
    /// display-only proposed view. Never written anywhere.
    /// </summary>
    public PayloadRect? TrimProposedPayloadRect
    {
        get
        {
            if (!IsAdjustingTrim || CropSelection is not { } kept || TrimAdjustSourcePane is not { } pane ||
                pane.PayloadPixelWidth <= 0 || pane.PayloadPixelHeight <= 0)
            {
                return null;
            }

            double perX = (double)pane.SourcePixelWidth / pane.PayloadPixelWidth;
            double perY = (double)pane.SourcePixelHeight / pane.PayloadPixelHeight;
            int left = Math.Clamp((int)Math.Floor(kept.Left / perX), 0, pane.PayloadPixelWidth - 1);
            int top = Math.Clamp((int)Math.Floor(kept.Top / perY), 0, pane.PayloadPixelHeight - 1);
            int right = Math.Clamp((int)Math.Ceiling(kept.RightExclusive / perX), left + 1, pane.PayloadPixelWidth);
            int bottom = Math.Clamp((int)Math.Ceiling(kept.BottomExclusive / perY), top + 1, pane.PayloadPixelHeight);
            return new PayloadRect(left, top, right - left, bottom - top);
        }
    }

    /// <summary>The accessible name of a handle.</summary>
    public static string TrimHandleName(CropHandle handle) => handle switch
    {
        CropHandle.Left => Strings.Session_TrimHandleLeft,
        CropHandle.Top => Strings.Session_TrimHandleTop,
        CropHandle.Right => Strings.Session_TrimHandleRight,
        CropHandle.Bottom => Strings.Session_TrimHandleBottom,
        CropHandle.TopLeft => Strings.Session_TrimHandleTopLeft,
        CropHandle.TopRight => Strings.Session_TrimHandleTopRight,
        CropHandle.BottomLeft => Strings.Session_TrimHandleBottomLeft,
        _ => Strings.Session_TrimHandleBottomRight,
    };

    // --- Commands ------------------------------------------------------------------------

    /// <summary>Opens the editor over the pre-trim source with the current boundary. Sends nothing.</summary>
    [RelayCommand]
    private void BeginTrimAdjust()
    {
        if (!CanAdjustTrim || _session?.TrimAdjustment is not { } target)
        {
            return;
        }

        ClearCropState();
        _trimAdjustTarget = target;
        IsAdjustingTrim = true;
        IsCropping = true;
        CropSelection = target.CurrentBounds;
        NotifyTrimAdjustState();
    }

    /// <summary>Puts the boundary exactly on the stored automatic suggestion. Sends nothing.</summary>
    [RelayCommand]
    private void RestoreAutomaticSuggestion()
    {
        if (!CanRestoreAutomaticSuggestion)
        {
            return;
        }

        // The stored applied rectangle already includes the automatic trim's own margin; the
        // adjustment's margin is fixed at tight, so nothing is applied twice.
        ResetManualCropAdjustment();
        CropSelection = _trimAdjustTarget!.AutomaticSuggestion;
        IsTrimAdjustInvalid = false;
    }

    /// <summary>Discards the draft. The review is exactly as it was; no attempt is cancelled.</summary>
    [RelayCommand]
    private void CancelTrimAdjust()
    {
        if (!IsAdjustingTrim || IsBusy)
        {
            return;
        }

        ClearCropState();
    }

    /// <summary>The one toggle's label: what pressing it will show.</summary>
    public string TrimCompareToggleLabel => IsComparingTrim ? TrimAdjustViewLabel : TrimCompareViewLabel;

    [RelayCommand]
    private void ToggleTrimCompare()
    {
        if (IsAdjustingTrim) IsComparingTrim = !IsComparingTrim;
    }

    [RelayCommand]
    private void ShowTrimAdjustView() => IsComparingTrim = false;

    [RelayCommand]
    private void ShowTrimCompareView()
    {
        if (IsAdjustingTrim) IsComparingTrim = true;
    }

    /// <summary>
    /// Sends the one command this editor has, for the exact result and source it was drawn for.
    /// It neither approves nor saves: the new result waits for its own review.
    /// </summary>
    [RelayCommand]
    private async Task UseThisTrimAsync(CancellationToken cancellationToken)
    {
        if (!CanUseThisTrim || _trimAdjustTarget is not { } target || CropSelection is not { } kept)
        {
            return;
        }

        await RunAsync(new WorkflowCommand.AdjustTrimFromReview(
            target.ResultRevisionId, target.ResultSha256, target.PreTrimRevisionId, target.PreTrimSha256, kept),
            cancellationToken).ConfigureAwait(true);

        // Said only when a new result from this source really is the one now under review.
        if (Notice is null && _session is { CurrentStep: { Step: StepKind.Trim, State: StepState.ReviewRequired } } session &&
            session.CurrentArtefact is { IsCurrentStepResult: true } shown && shown.RevisionId != target.ResultRevisionId &&
            session.UpstreamArtefact?.RevisionId == target.PreTrimRevisionId)
        {
            Notice = Strings.Session_TrimAdjustReady;
        }
    }

    // --- Gestures ------------------------------------------------------------------------

    /// <summary>
    /// Applies a handle drag. A refused position shows the boundary the gesture began with.
    /// </summary>
    public bool TryMoveTrimHandle(CropSurfaceLayout layout, TrimBounds start, CropHandle handle, double deltaX, double deltaY)
    {
        if (!IsAdjustingTrim || IsComparingTrim || IsBusy)
        {
            return false;
        }

        bool valid = CropHandleGesture.TryDrag(layout, start, handle, deltaX, deltaY, out TrimBounds moved);
        CropSelection = moved;
        IsTrimAdjustInvalid = !valid;
        return valid;
    }

    /// <summary>Returns to the boundary a gesture began with (lost capture, Esc).</summary>
    public void RestoreTrimDraft(TrimBounds start)
    {
        if (IsAdjustingTrim)
        {
            CropSelection = start;
        }
    }

    /// <summary>Moves a handle by whole source pixels, or to the picture border.</summary>
    public bool TryNudgeTrimHandle(CropHandle handle, int directionX, int directionY, bool bigStep, bool toBorder)
    {
        if (!IsAdjustingTrim || IsComparingTrim || IsBusy || CropSelection is not { } start || _trimAdjustTarget is not { } target)
        {
            return false;
        }

        int step = bigStep ? 10 : 1;
        bool valid = toBorder
            ? CropHandleGesture.TrySnapToBorder(start, handle, directionX, directionY,
                target.SourcePixelWidth, target.SourcePixelHeight, out TrimBounds moved)
            : CropHandleGesture.TryNudge(start, handle, directionX * step, directionY * step,
                target.SourcePixelWidth, target.SourcePixelHeight, out moved);
        CropSelection = moved;
        IsTrimAdjustInvalid = !valid;
        return valid;
    }

    // --- Plumbing ------------------------------------------------------------------------

    private void ClearTrimAdjustState()
    {
        _trimAdjustTarget = null;
        IsComparingTrim = false;
        IsTrimAdjustInvalid = false;
        IsAdjustingTrim = false;
    }

    partial void OnIsAdjustingTrimChanged(bool value)
    {
        NotifyTrimAdjustState();

        // Every consequential action reads IsAdjustingTrim.
        OnPropertyChanged(nameof(CanConfirmOriginal));
        OnPropertyChanged(nameof(CanRunStep));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanReject));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanSkip));
        OnPropertyChanged(nameof(CanHandOff));
        OnPropertyChanged(nameof(CanKeepOriginalExtent));
        KeepOriginalExtentCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanManualCrop));
        OnPropertyChanged(nameof(CanApplyManualCrop));
        OnPropertyChanged(nameof(CanReturnToStep));
        OnPropertyChanged(nameof(CanBeginReturn));
        OnPropertyChanged(nameof(CanSetTrimParameters));
        OnPropertyChanged(nameof(CanSubmitManualResult));
        OnPropertyChanged(nameof(CanOpenErrorDetails));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanAddAnotherSize));
        OnPropertyChanged(nameof(CanSelectWhiteUnderbase));
        OnPropertyChanged(nameof(CanConfirmWhiteUnderbase));
        OnPropertyChanged(nameof(CropPane));
        NotifyFinalSaveChanged();
        NotifyOperatorStatusChanged();
    }

    partial void OnIsComparingTrimChanged(bool value) => NotifyTrimAdjustState();

    private void NotifyTrimAdjustPanes()
    {
        OnPropertyChanged(nameof(CropPane));
        OnPropertyChanged(nameof(TrimAdjustSourcePane));
        OnPropertyChanged(nameof(TrimAdjustCurrentPane));
        OnPropertyChanged(nameof(IsTrimSourceUnavailable));
        OnPropertyChanged(nameof(TrimProposedPayloadRect));
        OnPropertyChanged(nameof(CanUseThisTrim));
    }

    private void NotifyTrimAdjustState()
    {
        OnPropertyChanged(nameof(TrimAdjustTarget));
        OnPropertyChanged(nameof(CanAdjustTrim));
        OnPropertyChanged(nameof(ShowsManualCropControls));
        OnPropertyChanged(nameof(ShowsCropSurface));
        OnPropertyChanged(nameof(ShowsTrimCompare));
        OnPropertyChanged(nameof(TrimCompareToggleLabel));
        OnPropertyChanged(nameof(TrimAdjustInstructions));
        OnPropertyChanged(nameof(TrimAdjustKeptSummary));
        OnPropertyChanged(nameof(TrimAdjustSuggestionSummary));
        OnPropertyChanged(nameof(IsTrimDraftUnchanged));
        OnPropertyChanged(nameof(CanRestoreAutomaticSuggestion));
        OnPropertyChanged(nameof(UseThisTrimIdentity));
        NotifyTrimAdjustPanes();
    }
}
