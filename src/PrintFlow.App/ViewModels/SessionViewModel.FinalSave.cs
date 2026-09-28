using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>One result the final-save section can act on: a pending final review or an approved artifact.</summary>
public sealed class FinalSaveTargetRow : ObservableObject
{
    internal FinalSaveTargetRow(FinalSaveTarget target) => Target = target;

    internal FinalSaveTarget Target { get; }

    public string Key => Target.Key;

    public string Label => string.Format(CultureInfo.CurrentCulture,
        Target.PendingReview is not null ? Strings.FinalSave_TargetPending : Strings.FinalSave_TargetApproved,
        Target.KnownFileName ?? Target.FileName);

    internal void Refresh() => OnPropertyChanged(nameof(Label));
}

/// <summary>
/// One recorded save of the selected result. Listing them keeps an older or unresolved save
/// reachable by its own DeliveryId and destination when a newer one is shown by default.
/// </summary>
public sealed class FinalSaveRecordRow : ObservableObject
{
    internal FinalSaveRecordRow(DeliveryState state) => State = state;

    internal DeliveryState State { get; private set; }

    public Guid DeliveryId => State.DeliveryId;

    public string Label => State.Status == "Delivered"
        ? string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_RecordSaved, State.RequestedFileName, State.RequestedFolder,
            State.VerifiedAtUtc is { } verified ? verified.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : string.Empty)
        : string.Format(CultureInfo.CurrentCulture,
            SessionViewModel.IsUnresolved(State) ? Strings.FinalSave_RecordUnverified : Strings.FinalSave_RecordNotSaved,
            State.RequestedFileName, State.RequestedFolder);

    internal void Update(DeliveryState state)
    {
        State = state;
        Refresh();
    }

    internal void Refresh() => OnPropertyChanged(nameof(Label));

    /// <summary>What UI Automation and type-ahead read for a templated list item.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// Screen state for one exact save target, kept by key so a late result is attributed to the
/// item it belongs to and never to whatever happens to be selected when it arrives.
/// </summary>
internal sealed class FinalSaveTarget
{
    public required string Key { get; init; }
    public required ArtifactKind Kind { get; init; }
    public ReviewedResultIdentity? PendingReview { get; set; }
    public ArtifactKey? Artifact { get; set; }
    public bool PngPreparationPending { get; set; }
    public string? KnownFileName { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public long Generation { get; set; }
    public bool NameEdited { get; set; }
    public bool FolderEdited { get; set; }
    public bool FolderFromPreference { get; set; }

    public DeliveryOffer? Offer { get; set; }
    public ArtifactRefusal Refusal { get; set; } = ArtifactRefusal.None;
    public IReadOnlyList<DeliveryState>? History { get; set; }
    public bool HistoryFailed { get; set; }

    /// <summary>History was read before an action that did not finish and has not been read since.</summary>
    public bool HistoryStale { get; set; }

    /// <summary>The recorded save the operator chose to look at; null shows the default record.</summary>
    public Guid? ShownDeliveryId { get; set; }

    /// <summary>The most recent thing this screen learned about the target, in any form.</summary>
    public object? Fact { get; set; }
    public OpenFolderOutcome? LastOpen { get; set; }

    /// <summary>An action that did not finish; the earlier fact is kept and nothing new is claimed.</summary>
    public string? ActionError { get; set; }

    /// <summary>The last captured save intent; a retry of an unchanged draft reuses its RequestId.</summary>
    public (Guid RequestId, string Folder, string FileName, long Generation)? Captured { get; set; }
}

internal enum FinalSaveKind { None, HistoryUnknown, Pending, PngPreparation, Ineligible, ApprovedNotSaved, Failed, Collision, Uncertain, Saved, SavedPreviously, Missing, Changed, Unavailable, Working }

/// <summary>Everything the section shows for the selected target, recomputed on each read.</summary>
internal sealed record FinalSaveDisplay
{
    public FinalSaveKind Kind { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Alert { get; init; }
    public string? Secondary { get; init; }
    public string? SavedPath { get; init; }
    public string? Details { get; init; }
    public Guid? DeliveryId { get; init; }
    public string? Suggestion { get; init; }
    public bool CanConfirmAndSave { get; init; }
    public bool CanSaveApproved { get; init; }
    public bool IsRetry { get; init; }
    public bool CanRetryRecorded { get; init; }
    public bool CanCheckAgain { get; init; }
    public bool CanCheckSaved { get; init; }
    public bool CanOpen { get; init; }
    public bool CanSaveAnotherCopy { get; init; }
    public bool CanCancel { get; init; }
    public bool IsEditable { get; init; }
    public double? Progress { get; init; }
}

public partial class SessionViewModel
{
    private readonly FinalSaveCoordinator? _finalSave;
    private readonly IDeliveryFolderPicker? _folderPicker;
    private readonly IDeliveredFileShell? _fileShell;

    private readonly Dictionary<string, FinalSaveTarget> _finalSaveTargets = [];
    private readonly Dictionary<string, FinalSaveTargetRow> _finalSaveRows = [];
    private readonly Dictionary<Guid, FinalSaveRecordRow> _finalSaveRecordRows = [];
    private SessionView? _finalSaveSession;
    private FinalSaveTargetRow? _selectedFinalSaveTarget;
    private string? _lastOfferedPending;
    private int _finalSaveFactsGeneration;
    private string? _preferredFolder;
    private bool _preferenceFailed;
    private FinalSaveOperation? _finalSaveOperation;

    /// <summary>The in-flight operation; its identity filters every late observation.</summary>
    private sealed class FinalSaveOperation(Guid operationId, string key, CancellationTokenSource cancellation)
    {
        public Guid OperationId { get; } = operationId;
        public string Key { get; } = key;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public FinalSaveStage? Stage { get; set; }
        public DeliveryPhase? Phase { get; set; }
        public string? Folder { get; set; }
        public double? Progress { get; set; }
        public bool IsVerifyingSaved { get; set; }
        public bool IsRecordedAction { get; set; }
    }

    /// <summary>Completes when the offers, history and remembered folder for the last view are applied.</summary>
    public Task FinalSaveFactsLoaded { get; private set; } = Task.CompletedTask;

    public ObservableCollection<FinalSaveTargetRow> FinalSaveTargets { get; } = [];

    public FinalSaveTargetRow? SelectedFinalSaveTarget
    {
        get => _selectedFinalSaveTarget;
        set
        {
            // A list rebuild can make a selector write back null; a save target is never unselected.
            if (value is null || ReferenceEquals(value, _selectedFinalSaveTarget)) return;
            _selectedFinalSaveTarget = value;
            NotifyFinalSaveChanged();
        }
    }

    /// <summary>Every recorded save of the selected result, newest first; shown only when there is more than one.</summary>
    public ObservableCollection<FinalSaveRecordRow> FinalSaveRecords { get; } = [];

    public bool HasFinalSaveRecords => HasFinalSave && FinalSaveRecords.Count > 1;
    public string FinalSaveRecordsLabel => Strings.FinalSave_RecordsLabel;

    /// <summary>
    /// The recorded save the section speaks about. Choosing another one shows that save with its
    /// own destination and actions; nothing is checked, copied or reconciled by the choice.
    /// </summary>
    public FinalSaveRecordRow? SelectedFinalSaveRecord
    {
        get
        {
            Guid? shown = SaveDisplay.DeliveryId ?? (SelectedSave is { } target ? DisplayedRecord(target)?.DeliveryId : null);
            return FinalSaveRecords.FirstOrDefault(r => r.DeliveryId == shown);
        }
        set
        {
            // A list rebuild can make a selector write back null; that is not a choice.
            if (value is null || SelectedSave is not { } target || !SaveDisplay.IsEditable ||
                value.DeliveryId == SelectedFinalSaveRecord?.DeliveryId) return;
            target.ShownDeliveryId = value.DeliveryId;
            // The last outcome on screen described another save.
            target.Fact = null;
            target.LastOpen = null;
            target.ActionError = null;
            AdoptRecordedDraft(target);
            NotifyFinalSaveChanged();
        }
    }

    private FinalSaveTarget? SelectedSave => _selectedFinalSaveTarget?.Target;

    private FinalSaveDisplay SaveDisplay => WhileAdjustingTrim(DescribeFinalSave(SelectedSave));

    /// <summary>
    /// Nothing that approves, saves, copies, retries or edits the save draft is offered while a
    /// trim adjustment is open (SCRUM-11147 §8): a combined confirmation then would approve the
    /// old result. Read-only actions on an already delivered copy — checking it and opening its
    /// folder — stay, and the section says why the rest has gone.
    /// </summary>
    internal FinalSaveDisplay WhileAdjustingTrim(FinalSaveDisplay display) => !IsAdjustingTrim ? display : display with
    {
        Alert = JoinLines(Strings.FinalSave_TrimAdjustOpen, display.Alert),
        CanConfirmAndSave = false,
        CanSaveApproved = false,
        CanRetryRecorded = false,
        CanCheckAgain = false,
        CanSaveAnotherCopy = false,
        CanCancel = false,
        IsEditable = false,
        Suggestion = null,
    };

    public bool HasFinalSave => _finalSave is not null && SelectedSave is not null;
    public bool HasMultipleFinalSaveTargets => FinalSaveTargets.Count > 1;
    public string FinalSaveHeading => Strings.FinalSave_Heading;
    public string FinalSaveTargetLabel => Strings.FinalSave_TargetLabel;
    public string SaveFileNameLabel => Strings.FinalSave_FileNameLabel;
    public string SaveFolderLabel => Strings.FinalSave_FolderLabel;
    public string ChangeLocationLabel => Strings.FinalSave_ChangeLocation;
    public string ConfirmAndSaveLabel => Strings.FinalSave_ConfirmAndSave;
    public string SaveApprovedLabel => SaveDisplay.IsRetry ? Strings.FinalSave_RetrySave : Strings.FinalSave_SaveApproved;
    public string RetryRecordedLabel => Strings.FinalSave_RetrySave;
    public string CheckAgainLabel => Strings.FinalSave_CheckAgain;
    public string CheckSavedLabel => Strings.FinalSave_CheckSaved;
    public string OpenFolderLabel => Strings.FinalSave_OpenFolder;
    public string SaveAnotherCopyLabel => Strings.FinalSave_SaveAnotherCopy;
    public string CancelSaveLabel => Strings.FinalSave_CancelSave;
    public string SavedPathLabel => Strings.FinalSave_FullPathLabel;
    public string FinalSaveDetailsLabel => Strings.FinalSave_DetailsLabel;

    public string UseSuggestionLabel => SaveDisplay.Suggestion is { } suggestion
        ? string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_UseSuggestion, suggestion) : string.Empty;

    /// <summary>Operator-entered external filename; editing never renames the managed file or OutputName.</summary>
    public string SaveFileNameText
    {
        get => SelectedSave?.FileName ?? string.Empty;
        set
        {
            if (SelectedSave is not { } target || !SaveDisplay.IsEditable || value == target.FileName) return;
            target.FileName = value ?? string.Empty;
            target.NameEdited = true;
            target.Generation++;
            NotifyFinalSaveChanged();
        }
    }

    public string SaveFolderText => SelectedSave is { Folder.Length: > 0 } target ? target.Folder : Strings.FinalSave_NoFolderValue;
    public string SaveSummary => SelectedSave is { } target ? Summary(target) : string.Empty;
    /// <summary>An ineligible result or an unfinished PNG preparation offers no draft to edit.</summary>
    public bool ShowsSaveDraft => HasFinalSave && SaveDisplay.Kind is not (FinalSaveKind.Ineligible or FinalSaveKind.PngPreparation);
    public string SaveDraftNotice => ShowsSaveDraft && SelectedSave is { } target ? DraftNotice(target) : string.Empty;
    public bool HasSaveDraftNotice => SaveDraftNotice.Length > 0;
    public string FinalSaveStatus => SaveDisplay.Status;
    public string FinalSaveAlert => SaveDisplay.Alert ?? string.Empty;
    public bool HasFinalSaveAlert => SaveDisplay.Alert is { Length: > 0 };
    public string FinalSaveSecondary => SaveDisplay.Secondary ?? string.Empty;
    public bool HasFinalSaveSecondary => SaveDisplay.Secondary is { Length: > 0 };
    public string FinalSaveSavedPath => SaveDisplay.SavedPath ?? string.Empty;
    public bool HasFinalSaveSavedPath => SaveDisplay.SavedPath is { Length: > 0 };
    public string FinalSaveDetails => SaveDisplay.Details ?? string.Empty;
    public bool HasFinalSaveDetails => SaveDisplay.Details is { Length: > 0 };
    public string FinalSaveOpenNotice => SelectedSave?.LastOpen is { } open ? OpenNotice(open) : string.Empty;
    public bool HasFinalSaveOpenNotice => FinalSaveOpenNotice.Length > 0;
    public bool IsSaveDraftEditable => SaveDisplay.IsEditable;
    public bool IsFinalSaveWorking => SaveDisplay.Kind == FinalSaveKind.Working;
    public double FinalSaveProgress => SaveDisplay.Progress ?? 0;
    public bool HasFinalSaveProgress => SaveDisplay.Progress is not null;
    public bool CanConfirmAndSave => SaveDisplay.CanConfirmAndSave;
    public bool CanSaveApproved => SaveDisplay.CanSaveApproved;
    public bool CanRetryRecordedSave => SaveDisplay.CanRetryRecorded;
    public bool CanCheckSaveAgain => SaveDisplay.CanCheckAgain;
    public bool CanCheckSavedFile => SaveDisplay.CanCheckSaved;
    public bool CanOpenContainingFolder => SaveDisplay.CanOpen && _fileShell is not null;
    public bool CanSaveAnotherCopy => SaveDisplay.CanSaveAnotherCopy;
    public bool CanUseSuggestedName => SaveDisplay.Suggestion is not null && SaveDisplay.IsEditable;
    public bool CanCancelSave => SaveDisplay.CanCancel;
    public bool CanChangeLocation => SaveDisplay.IsEditable && _folderPicker is not null;

    /// <summary>True when the section shows an approved result that is not in the operator's folder.</summary>
    internal bool IsFinalSaveUnsaved => HasFinalSave &&
        SaveDisplay.Kind is FinalSaveKind.ApprovedNotSaved or FinalSaveKind.Failed or FinalSaveKind.Collision;

    /// <summary>Input identity for the combined action: the exact review plus the draft generation.</summary>
    public string? ConfirmAndSaveIdentity => ReviewTargetIdentity is { } review && SelectedSave is { PendingReview: not null } target
        ? $"{review}|{target.Key}|{target.Generation}" : null;

    /// <summary>Input identity for actions on one recorded delivery (retry, check again, another copy).</summary>
    public string? RecordedActionIdentity => SaveDisplay.DeliveryId is { } deliveryId
        ? $"recorded|{deliveryId:N}|{SaveDisplay.Kind}|{SelectedSave?.Generation}" : null;

    /// <summary>Input identity for an approved-only save: never null while that action is offered.</summary>
    public string? SaveApprovedIdentity => SelectedSave is { Artifact: { } artifact } target
        ? $"save|{artifact.SessionId}|{artifact.Kind}|{artifact.ArtifactId:N}|{target.Generation}" : null;

    // --- Discovery ------------------------------------------------------------------------

    /// <summary>Rebuilds the save targets from the view the service just returned; drafts survive.</summary>
    private void ShowFinalSave(SessionView session)
    {
        if (_finalSave is null) return;
        if (_finalSaveSession?.Id != session.Id)
        {
            _finalSaveTargets.Clear();
            _finalSaveRows.Clear();
            _finalSaveRecordRows.Clear();
            FinalSaveRecords.Clear();
            FinalSaveTargets.Clear();
            _selectedFinalSaveTarget = null;
            _lastOfferedPending = null;
        }
        _finalSaveSession = session;

        List<FinalSaveTarget> discovered = DiscoverFinalSaveTargets(session);
        string? previous = _selectedFinalSaveTarget?.Key;
        // Row objects are kept per key and the list is touched only when its items change, so an
        // ordinary refresh neither rebuilds the section nor disturbs focus inside it.
        if (!FinalSaveTargets.Select(r => r.Key).SequenceEqual(discovered.Select(t => t.Key)))
        {
            FinalSaveTargets.Clear();
            foreach (FinalSaveTarget target in discovered)
            {
                if (!_finalSaveRows.TryGetValue(target.Key, out FinalSaveTargetRow? row))
                    _finalSaveRows[target.Key] = row = new FinalSaveTargetRow(target);
                FinalSaveTargets.Add(row);
            }
        }

        // An operation in flight keeps its own item selected; otherwise a newly offered pending
        // review is shown first, then the previous choice, then the newest approved result. A
        // refresh never moves the operator away from an item they chose.
        string? keep = _finalSaveOperation?.Key ?? previous;
        FinalSaveTargetRow? pending = FinalSaveTargets.FirstOrDefault(r => r.Target.PendingReview is not null);
        string? offeredPending = pending?.Target.PendingReview?.RevisionId.ToString();
        bool newlyOffered = pending is not null && offeredPending != _lastOfferedPending;
        _lastOfferedPending = offeredPending;
        _selectedFinalSaveTarget =
            (_finalSaveOperation is null && newlyOffered ? pending : null) ??
            FinalSaveTargets.FirstOrDefault(r => r.Key == keep) ?? pending ?? FinalSaveTargets.LastOrDefault();
        NotifyFinalSaveChanged();
        FinalSaveFactsLoaded = LoadFinalSaveFactsAsync(session, ++_finalSaveFactsGeneration);
    }

    private List<FinalSaveTarget> DiscoverFinalSaveTargets(SessionView session)
    {
        List<FinalSaveTarget> found = [];
        bool reviewing = session.State == SessionState.Active && session.CurrentStep is { State: StepState.ReviewRequired } &&
                         session.AvailableCommands.Contains(CommandKind.Approve) &&
                         session.CurrentArtefact is { IsCurrentStepResult: true };
        if (session.WorkflowType == WorkflowType.PrepareAsset)
        {
            SessionStep? trim = session.Steps.SingleOrDefault(s => s.Step == StepKind.Trim);
            SessionStep? export = session.Steps.SingleOrDefault(s => s.Step == StepKind.ApprovedPngExport);
            FinalSaveTarget target = Target("png", ArtifactKind.ApprovedAssetPng);
            target.PngPreparationPending = false;
            if (reviewing && session.CurrentStep!.Step == StepKind.Trim)
            {
                SetPending(target, new ReviewedResultIdentity(session.Id, StepKind.Trim,
                    session.CurrentArtefact!.RevisionId, session.CurrentArtefact.Sha256));
                Seed(target, session.OutputName.Value + ".png");
            }
            else if (export is { State: StepState.Approved, CurrentRevisionId: { } promoted })
                SetArtifact(target, new ArtifactKey(session.Id, ArtifactKind.ApprovedAssetPng, promoted.Value));
            else if (trim is { State: StepState.Approved } && export is not null)
            {
                SetPending(target, null);
                target.PngPreparationPending = true;
            }
            else return found;
            found.Add(target);
            return found;
        }
        if (!session.ProducesPrintOutput) return found;
        foreach (PrintOutputView output in session.Outputs.Where(o =>
                     o.ReviewState == ReviewState.Approved && o.IsValid && !o.IsRecycled))
        {
            FinalSaveTarget target = Target("tiff:" + output.Id.Value.ToString("N"), ArtifactKind.ApprovedPrintTiff);
            target.KnownFileName = output.FileName;
            SetArtifact(target, new ArtifactKey(session.Id, ArtifactKind.ApprovedPrintTiff, output.Id.Value));
            Seed(target, output.FileName);
            found.Add(target);
        }
        if (reviewing && session.CurrentStep!.Step == StepKind.PhotoshopOutput)
        {
            ArtefactView artefact = session.CurrentArtefact!;
            FinalSaveTarget target = Target("tiff:" + artefact.RevisionId.Value.ToString("N"), ArtifactKind.ApprovedPrintTiff);
            SetPending(target, new ReviewedResultIdentity(session.Id, StepKind.PhotoshopOutput, artefact.RevisionId, artefact.Sha256));
            target.KnownFileName = artefact.FileName;
            Seed(target, artefact.FileName);
            found.Add(target);
        }
        return found;

        FinalSaveTarget Target(string key, ArtifactKind kind)
        {
            if (!_finalSaveTargets.TryGetValue(key, out FinalSaveTarget? existing))
                _finalSaveTargets[key] = existing = new FinalSaveTarget { Key = key, Kind = kind };
            return existing;
        }
    }

    /// <summary>
    /// Moves a keyed target to a different exact identity. Facts learned about the previous
    /// identity never describe the new one; a save outcome survives only the pending → approved
    /// transition it was produced by. Drafts are the operator's and are kept.
    /// </summary>
    private static void SetPending(FinalSaveTarget target, ReviewedResultIdentity? pending)
    {
        if (target.PendingReview == pending && target.Artifact is null) return;
        target.PendingReview = pending;
        target.Artifact = null;
        ForgetFacts(target, keepDelivery: false);
    }

    private static void SetArtifact(FinalSaveTarget target, ArtifactKey key)
    {
        if (target.Artifact == key && target.PendingReview is null) return;
        bool approvedFromReview = target.PendingReview is not null && target.Artifact is null;
        target.PendingReview = null;
        target.Artifact = key;
        ForgetFacts(target, keepDelivery: approvedFromReview);
    }

    private static void ForgetFacts(FinalSaveTarget target, bool keepDelivery)
    {
        target.Offer = null;
        target.Refusal = ArtifactRefusal.None;
        target.History = null;
        target.HistoryFailed = false;
        target.HistoryStale = false;
        target.ShownDeliveryId = null;
        target.LastOpen = null;
        if (!keepDelivery || target.Fact is not DeliveryFact)
        {
            target.Fact = null;
            if (!keepDelivery) target.Captured = null;
        }
    }

    private void Seed(FinalSaveTarget target, string fileName)
    {
        if (!target.NameEdited && target.FileName.Length == 0 && fileName.Length > 0)
        {
            target.FileName = fileName;
            target.Generation++;
        }
        // The remembered folder follows the newest successful save until the operator chooses
        // one; it never overrides an explicit choice or a result's own recorded destination.
        // Re-pointing is limited to a draft nothing has happened to yet: a captured attempt, an
        // outcome on screen or any recorded save keeps its own destination (R2).
        bool untouched = target.Captured is null && target.Fact is null && target.History is not { Count: > 0 };
        if (!target.FolderEdited && (target.Folder.Length == 0 && target.Captured is null || target.FolderFromPreference && untouched) &&
            _preferredFolder is { Length: > 0 } remembered && remembered != target.Folder)
        {
            target.Folder = remembered;
            target.FolderFromPreference = true;
            target.Generation++;
        }
    }

    /// <summary>
    /// An untouched draft of a result with recorded saves shows the most recent recorded name and
    /// folder, so reopening never proposes a silent second copy under a different default. The
    /// remembered folder stays the default only for results that have never been saved.
    /// </summary>
    private static void AdoptRecordedDraft(FinalSaveTarget target)
    {
        if (target.NameEdited || target.FolderEdited || target.Captured is not null ||
            DisplayedRecord(target) is not { } shown) return;
        if (target.FileName == shown.RequestedFileName && target.Folder == shown.RequestedFolder) return;
        target.FileName = shown.RequestedFileName;
        target.Folder = shown.RequestedFolder;
        target.FolderFromPreference = false;
        target.Generation++;
    }

    /// <summary>Metadata only: remembered folder, offers and recorded deliveries. No folder scan or reconciliation.</summary>
    private async Task LoadFinalSaveFactsAsync(SessionView session, int generation)
    {
        if (_finalSave is null || _finalSaveOperation is not null) return;
        IApprovedArtifactDeliveryService delivery = _finalSave.Delivery;
        try
        {
            // Re-read on every load: a save that just succeeded changes the default for the next
            // output shown on this same screen.
            OperationResult<DeliveryDestinationPreferenceView?> remembered =
                await Task.Run(() => delivery.GetLastSuccessfulDestinationAsync(CancellationToken.None)).ConfigureAwait(true);
            List<(FinalSaveTarget Target, DeliveryOffer? Offer, ArtifactRefusal Refusal,
                OperationResult<IReadOnlyList<DeliveryState>> History)> loaded = [];
            foreach (FinalSaveTarget target in FinalSaveTargets.Select(r => r.Target).Where(t => t.Artifact is not null).ToList())
            {
                // An operation that has started owns the offer version; a late load must not
                // replace it between the coordinator's offer and its delivery request.
                if (_finalSaveOperation is not null) return;
                ArtifactKey key = target.Artifact!.Value;
                (DeliveryOffer? offer, ArtifactRefusal refusal) =
                    await Task.Run(() => delivery.GetOfferAsync(key, CancellationToken.None)).ConfigureAwait(true);
                OperationResult<IReadOnlyList<DeliveryState>> history =
                    await Task.Run(() => delivery.GetDeliveryStateAsync(session.Id, key, CancellationToken.None)).ConfigureAwait(true);
                loaded.Add((target, offer, refusal, history));
            }
            if (generation != _finalSaveFactsGeneration || _finalSaveSession?.Id != session.Id) return;
            // A draft frozen by an operation that started meanwhile is never changed underneath it.
            if (_finalSaveOperation is not null) return;
            _preferenceFailed = remembered.IsFailure;
            _preferredFolder = remembered.IsSuccess ? remembered.Value?.Folder : null;
            foreach ((FinalSaveTarget target, DeliveryOffer? offer, ArtifactRefusal refusal,
                         OperationResult<IReadOnlyList<DeliveryState>> history) in loaded)
            {
                target.Offer = offer;
                target.Refusal = offer is null ? refusal : ArtifactRefusal.None;
                target.HistoryFailed = history.IsFailure;
                // A failed refresh keeps what was read earlier for this exact result, shown as dated.
                target.History = history.IsSuccess ? history.Value : target.History;
                target.HistoryStale = false;
                if (offer is not null) Seed(target, offer.Artifact.SuggestedFileName);
                AdoptRecordedDraft(target);
            }
            foreach (FinalSaveTargetRow row in FinalSaveTargets) Seed(row.Target, string.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A metadata failure is reported as unreadable history, never as "no earlier saves".
            foreach (FinalSaveTarget target in FinalSaveTargets.Select(r => r.Target)) target.HistoryFailed = true;
        }
        NotifyFinalSaveChanged();
    }

    // --- Commands -------------------------------------------------------------------------

    [RelayCommand]
    private async Task ConfirmAndSaveAsync()
    {
        if (_finalSave is null || _session is null || SelectedSave is not { PendingReview: { } review } target ||
            !CanConfirmAndSave || EffectiveName(target.FileName, target.Kind).Effective is not { } name) return;
        var request = new FinalSaveRequest(Guid.NewGuid(), Guid.NewGuid(), _session.Id, target.Kind, review, null,
            review.DisplayedHash, target.Folder, name, target.Generation);
        target.Captured = (request.RequestId, request.Folder, request.FileName, request.DraftGeneration);
        await RunFinalSaveAsync(target, request.OperationId, request.Folder,
            (progress, token) => _finalSave.ConfirmAndSaveAsync(request, progress, token)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveApprovedAsync()
    {
        if (_finalSave is null || _session is null || SelectedSave is not { Artifact: { } artifact, Offer: { } offer } target ||
            !CanSaveApproved || EffectiveName(target.FileName, target.Kind).Effective is not { } name) return;
        // The same captured intent keeps its RequestId; any accepted change of name or folder is a new request.
        Guid requestId = target.Captured is { } captured && captured.Generation == target.Generation &&
                         captured.Folder == target.Folder && captured.FileName == name
            ? captured.RequestId : Guid.NewGuid();
        var request = new FinalSaveRequest(Guid.NewGuid(), requestId, _session.Id, target.Kind, null, artifact,
            offer.Artifact.ApprovedSha256, target.Folder, name, target.Generation);
        target.Captured = (request.RequestId, request.Folder, request.FileName, request.DraftGeneration);
        await RunFinalSaveAsync(target, request.OperationId, request.Folder,
            (progress, token) => _finalSave.SaveApprovedAsync(request, progress, token)).ConfigureAwait(true);
    }

    /// <summary>Retry or check a recorded save at its own recorded destination.</summary>
    [RelayCommand]
    private Task RetryRecordedSaveAsync() => ReconcileSelectedAsync();

    [RelayCommand]
    private Task CheckSaveAgainAsync() => ReconcileSelectedAsync();

    private async Task ReconcileSelectedAsync()
    {
        if (IsAdjustingTrim || _finalSave is null || SelectedSave is not { } target || SaveDisplay.DeliveryId is not { } deliveryId ||
            _finalSaveOperation is not null || IsBusy) return;
        await RunDeliveryStepAsync(target, RecordedFolder(target, deliveryId), false, async progress =>
            new DeliveryFact(await _finalSave.ReconcileAsync(deliveryId, progress, CancellationToken.None).ConfigureAwait(false))).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CheckSavedFileAsync()
    {
        if (_finalSave is null || SelectedSave is not { } target || SaveDisplay.DeliveryId is not { } deliveryId ||
            _finalSaveOperation is not null || IsBusy) return;
        await RunDeliveryStepAsync(target, null, true, async _ =>
            new ObservationFact(await _finalSave.CheckAsync(deliveryId, CancellationToken.None).ConfigureAwait(false))).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveAnotherCopyAsync()
    {
        if (_finalSave is null || SelectedSave is not { } target || SaveDisplay.DeliveryId is not { } deliveryId ||
            !CanSaveAnotherCopy || _finalSaveOperation is not null || IsBusy) return;
        Guid requestId = Guid.NewGuid();
        await RunDeliveryStepAsync(target, RecordedFolder(target, deliveryId), false, async progress =>
            new DeliveryFact(await _finalSave.SaveAnotherCopyAsync(deliveryId, requestId, progress, CancellationToken.None).ConfigureAwait(false))).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenContainingFolderAsync()
    {
        if (_finalSave is null || _fileShell is null || SelectedSave is not { } target ||
            SaveDisplay.DeliveryId is not { } deliveryId || _finalSaveOperation is not null || IsBusy) return;
        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        IDeliveredFileShell shell = _fileShell;
        // Invoke (not BeginInvoke): the verified lease stays held until the shell call has run.
        ShellDispatchResult Dispatch(IDeliveredSelectionLease lease) =>
            dispatcher is null || dispatcher.CheckAccess() ? shell.SelectInFolder(lease) : dispatcher.Invoke(() => shell.SelectInFolder(lease));
        FinalSaveOperation operation = BeginFinalSaveOperation(target, null);
        operation.IsVerifyingSaved = true;
        try
        {
            target.LastOpen = await Task.Run(() => _finalSave.OpenContainingFolderAsync(deliveryId, Dispatch, CancellationToken.None))
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            target.LastOpen = new OpenFolderOutcome(deliveryId, OpenFolderCode.ShellFailed, null, ex.Message);
        }
        finally { EndFinalSaveOperation(operation); }
        // A metadata load abandoned because this operation started is run again afterwards (T1).
        if (_finalSaveSession is { } current) FinalSaveFactsLoaded = LoadFinalSaveFactsAsync(current, ++_finalSaveFactsGeneration);
    }

    [RelayCommand]
    private void UseSuggestedName()
    {
        if (SelectedSave is not { } target || SaveDisplay.Suggestion is not { } suggestion || !SaveDisplay.IsEditable) return;
        // Edits the draft only. Saving under the suggested name needs another explicit action.
        target.FileName = suggestion;
        target.NameEdited = true;
        target.Generation++;
        NotifyFinalSaveChanged();
    }

    [RelayCommand]
    private void ChangeLocation()
    {
        if (_folderPicker is null || SelectedSave is not { } target || !SaveDisplay.IsEditable) return;
        string? picked = _folderPicker.PickFolder(Strings.FinalSave_PickerTitle,
            target.Folder.Length > 0 ? target.Folder : null);
        // Dismissal changes nothing: draft, approval and remembered folder all stay as they were.
        if (string.IsNullOrWhiteSpace(picked) || !ReferenceEquals(SelectedSave, target) || !SaveDisplay.IsEditable) return;
        if (picked == target.Folder) return;
        target.Folder = picked;
        target.FolderEdited = true;
        target.FolderFromPreference = false;
        target.Generation++;
        NotifyFinalSaveChanged();
    }

    [RelayCommand]
    private void CancelSave()
    {
        if (_finalSaveOperation is { } operation && SaveDisplay.CanCancel) operation.Cancellation.Cancel();
    }

    // --- Operation plumbing ---------------------------------------------------------------

    private FinalSaveOperation BeginFinalSaveOperation(FinalSaveTarget target, string? folder, Guid? operationId = null)
    {
        FinalSaveOperation operation = new(operationId ?? Guid.NewGuid(), target.Key, new CancellationTokenSource()) { Folder = folder };
        _finalSaveOperation = operation;
        target.LastOpen = null;
        target.ActionError = null;
        IsBusy = true;
        NotifyFinalSaveChanged();
        return operation;
    }

    /// <summary>
    /// Lets a metadata load that began before this operation finish first, so it cannot replace
    /// the backend offer version the operation is about to use. Its own failures are already
    /// reported as unreadable history.
    /// </summary>
    private async Task SettleFinalSaveFactsAsync()
    {
        try { await FinalSaveFactsLoaded.ConfigureAwait(true); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    private void EndFinalSaveOperation(FinalSaveOperation operation)
    {
        if (ReferenceEquals(_finalSaveOperation, operation)) _finalSaveOperation = null;
        operation.Cancellation.Dispose();
        IsBusy = false;
        NotifyFinalSaveChanged();
    }

    private async Task RunFinalSaveAsync(FinalSaveTarget target, Guid operationId, string folder,
        Func<IProgress<FinalSaveObservation>, CancellationToken, Task<FinalSaveResult>> run)
    {
        if (_finalSaveOperation is not null || IsBusy) return;
        target.ShownDeliveryId = null; // a new save is what the section speaks about next
        FinalSaveOperation operation = BeginFinalSaveOperation(target, folder, operationId);
        await SettleFinalSaveFactsAsync();
        SessionView? refreshed = null;
        bool uncertain = false;
        IProgress<FinalSaveObservation> progress = new Progress<FinalSaveObservation>(observation =>
        {
            // Posted to this screen's context. Anything from another or finished operation is ignored.
            if (!ReferenceEquals(_finalSaveOperation, operation) || observation.OperationId != operation.OperationId) return;
            operation.Stage = observation.Stage;
            if (observation.Delivery is { } delivered)
            {
                operation.Phase = delivered.Phase;
                operation.Progress = delivered.Phase == DeliveryPhase.Copying && delivered.TotalBytes > 0
                    ? (double)delivered.BytesCopied / delivered.TotalBytes : operation.Progress;
            }
            NotifyFinalSaveChanged();
        });
        try
        {
            FinalSaveResult result = await Task.Run(() => run(progress, operation.Cancellation.Token)).ConfigureAwait(true);
            target.Fact = result.Delivery is { } delivered ? new DeliveryFact(delivered) : new ApprovalFact(result);
            refreshed = result.Session;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Approval, preparation or publication may or may not have happened: nothing is
            // claimed here. The earlier fact stays, and the screen is rebuilt from authority.
            target.ActionError = ex.Message;
            target.Fact = null; // an older outcome no longer describes what just happened (R5)
            target.HistoryStale = true; // history read before this action is dated until read again
            uncertain = true;
        }
        finally { EndFinalSaveOperation(operation); }

        // The screen is rebuilt from authority after approval; the save result stays with its own key.
        if (refreshed is not null && refreshed.Id == _session?.Id) Show(refreshed);
        else if (uncertain) await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        else if (_finalSaveSession is { } current) FinalSaveFactsLoaded = LoadFinalSaveFactsAsync(current, ++_finalSaveFactsGeneration);
    }

    /// <summary>
    /// Runs one action on a recorded delivery. These are not cancellable from the screen: the
    /// backend waits on the session gate and may publish, so a cancelled or failed call must not
    /// turn "may have been saved" into "not saved". A failure keeps the earlier fact.
    /// </summary>
    private async Task RunDeliveryStepAsync(FinalSaveTarget target, string? folder, bool verifying,
        Func<IProgress<DeliveryProgress>, Task<object>> run)
    {
        FinalSaveOperation operation = BeginFinalSaveOperation(target, folder);
        await SettleFinalSaveFactsAsync();
        operation.IsVerifyingSaved = verifying;
        operation.IsRecordedAction = true;
        operation.Stage = FinalSaveStage.Saving;
        IProgress<DeliveryProgress> progress = new Progress<DeliveryProgress>(delivered =>
        {
            if (!ReferenceEquals(_finalSaveOperation, operation)) return;
            operation.Phase = delivered.Phase;
            operation.Progress = delivered.Phase == DeliveryPhase.Copying && delivered.TotalBytes > 0
                ? (double)delivered.BytesCopied / delivered.TotalBytes : operation.Progress;
            NotifyFinalSaveChanged();
        });
        try { target.Fact = await Task.Run(() => run(progress)).ConfigureAwait(true); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { target.ActionError = ex.Message; }
        finally { EndFinalSaveOperation(operation); }
        if (_finalSaveSession is { } current) FinalSaveFactsLoaded = LoadFinalSaveFactsAsync(current, ++_finalSaveFactsGeneration);
    }

    private sealed record DeliveryFact(DeliveryOutcome Outcome);
    private sealed record ObservationFact(DeliveryFileObservation Observation);
    private sealed record ApprovalFact(FinalSaveResult? Result, string? Error = null);

    // --- Presentation ---------------------------------------------------------------------

    private FinalSaveDisplay DescribeFinalSave(FinalSaveTarget? target)
    {
        if (_finalSave is null || target is null) return new FinalSaveDisplay();
        (string? effective, _, _) = EffectiveName(target.FileName, target.Kind);
        bool draftValid = effective is not null && target.Folder.Length > 0;
        bool idle = _finalSaveOperation is null && !IsBusy;

        if (_finalSaveOperation is { } operation && operation.Key == target.Key)
            return Working(operation);
        if (target.PngPreparationPending)
            return new() { Kind = FinalSaveKind.PngPreparation, Status = Strings.FinalSave_PngPreparationPending };
        if (target.PendingReview is not null)
            return new()
            {
                Kind = FinalSaveKind.Pending,
                Status = Format(Strings.FinalSave_AwaitingConfirmation, Strings.FinalSave_ConfirmAndSave),
                Alert = JoinLines(target.Fact is ApprovalFact approval ? ApprovalAlert(approval) : null, ActionIncomplete(target)),
                Secondary = Format(Strings.FinalSave_ApproveOnlyHint, ApproveLabel),
                Details = target.Fact is ApprovalFact detailed ? ApprovalDetails(detailed) : null,
                IsEditable = idle,
                CanConfirmAndSave = idle && draftValid && CanApprove && ReviewTargetIdentity is not null,
            };
        if (target.Refusal != ArtifactRefusal.None)
            return new() { Kind = FinalSaveKind.Ineligible, Status = Format(Strings.FinalSave_Ineligible, RefusalReason(target.Refusal)) };
        if (target.Fact is ApprovalFact { Result.OfferRefusal: not ArtifactRefusal.None } refused)
            return new() { Kind = FinalSaveKind.Ineligible, Status = Format(Strings.FinalSave_Ineligible, RefusalReason(refused.Result!.OfferRefusal)) };

        bool canSave = idle && draftValid && target.Offer is not null;
        FinalSaveDisplay display = target.Fact switch
        {
            DeliveryFact delivery => FromOutcome(target, delivery.Outcome),
            ObservationFact observation => FromObservation(target, observation.Observation),
            ApprovalFact approval => new()
            {
                Kind = FinalSaveKind.ApprovedNotSaved, Status = Strings.FinalSave_ApprovedNotSaved,
                Alert = ApprovalAlert(approval), Details = ApprovalDetails(approval),
            },
            _ => FromHistory(target),
        };
        if (display.Kind == FinalSaveKind.Ineligible) return display;

        // Re-saving the recorded name and folder adds nothing (the backend would coalesce it), so
        // the ordinary save is offered only for a changed draft, a failed intent's retry (same
        // RequestId) or a first save. A different destination beside an unverified save warns.
        string? draftPath = effective is not null && target.Folder.Length > 0 ? DeliveryDraftText.Combine(target.Folder, effective) : null;
        string? recordedPath = display.SavedPath ?? RecordedPath(target, display.DeliveryId);
        bool sameAsRecorded = draftPath is not null && recordedPath is not null &&
                              string.Equals(draftPath, recordedPath, StringComparison.OrdinalIgnoreCase);
        bool unchangedIntent = target.Captured is { } captured && captured.Generation == target.Generation &&
                               captured.FileName == effective && captured.Folder == target.Folder;
        bool offerSave = display.Kind switch
        {
            FinalSaveKind.Failed => true,
            FinalSaveKind.Collision => !unchangedIntent,
            FinalSaveKind.ApprovedNotSaved => !(sameAsRecorded && display.CanRetryRecorded),
            _ => !sameAsRecorded,
        };
        List<string> secondary = [];
        if (display.Secondary is { Length: > 0 } existing) secondary.Add(existing);
        if (display.Kind == FinalSaveKind.Uncertain && !sameAsRecorded && RecordedFolder(target, display.DeliveryId) is { } recorded)
            secondary.Add(Format(Strings.FinalSave_UncertainElsewhere, recorded));
        if (HistoryIsDated(target) && target.Fact is null && display.Kind != FinalSaveKind.HistoryUnknown)
            secondary.Add(Strings.FinalSave_HistoryNotRefreshed);
        else if (target.HistoryFailed && display.Kind != FinalSaveKind.HistoryUnknown) secondary.Add(Strings.FinalSave_HistoryUnavailable);
        return display with
        {
            Alert = JoinLines(display.Alert, ActionIncomplete(target)),
            Secondary = secondary.Count == 0 ? null : string.Join(Environment.NewLine, secondary),
            IsEditable = idle,
            IsRetry = display.Kind == FinalSaveKind.Failed && unchangedIntent,
            CanSaveApproved = canSave && offerSave,
            CanRetryRecorded = idle && display.CanRetryRecorded,
            CanCheckAgain = idle && display.CanCheckAgain,
            CanCheckSaved = idle && display.CanCheckSaved,
            CanOpen = idle && display.CanOpen,
            CanSaveAnotherCopy = idle && display.CanSaveAnotherCopy,
        };
    }

    private FinalSaveDisplay Working(FinalSaveOperation operation)
    {
        bool finishing = operation.Phase is DeliveryPhase.Publishing or DeliveryPhase.Recording;
        string folder = operation.Folder ?? string.Empty;
        string status = operation.IsVerifyingSaved ? Strings.FinalSave_StageVerifyingSaved : operation.Stage switch
        {
            FinalSaveStage.CheckingDraft => Strings.FinalSave_StageChecking,
            FinalSaveStage.Approving => Strings.FinalSave_StageApproving,
            FinalSaveStage.Approved => Strings.FinalSave_StageApproved,
            FinalSaveStage.PreparingPng => Strings.FinalSave_StagePreparingPng,
            _ when finishing => Strings.FinalSave_StageFinishing,
            _ => Format(Strings.FinalSave_StageSaving, folder),
        };
        // Cancellation is offered only while nothing can yet have been published.
        bool cancellable = !operation.IsVerifyingSaved && !operation.IsRecordedAction && operation.Stage == FinalSaveStage.Saving &&
                           operation.Phase is null or DeliveryPhase.Validating or DeliveryPhase.Copying or DeliveryPhase.Verifying;
        return new() { Kind = FinalSaveKind.Working, Status = status, CanCancel = cancellable, Progress = operation.Progress };
    }

    private FinalSaveDisplay FromOutcome(FinalSaveTarget target, DeliveryOutcome outcome)
    {
        string details = Technical(outcome.RequestId, outcome.DeliveryId, outcome.Code.ToString(), outcome.Detail);
        string attempted = target.Captured?.FileName ?? target.FileName;
        string? path = outcome.FinalPath ?? RecordedPath(target, outcome.DeliveryId);
        return outcome.Code switch
        {
            DeliveryCode.Delivered or DeliveryCode.AlreadyDelivered when outcome.FinalPath is { } final => Saved(final, outcome.DeliveryId, details),
            DeliveryCode.Collision => new()
            {
                Kind = FinalSaveKind.Collision, Details = details, Suggestion = outcome.SuggestedFileName,
                Status = Format(Strings.FinalSave_ApprovedNotSavedReason, outcome.SuggestedFileName is { } suggestion
                    ? Format(Strings.FinalSave_Collision, attempted, suggestion)
                    : Format(Strings.FinalSave_CollisionNoSuggestion, attempted)),
                // A recheck that met a collision published nothing under that name but leaves its
                // recorded request unresolved, so Check again stays offered without a reload (T2).
                DeliveryId = UnresolvedRecord(target, outcome.DeliveryId)?.DeliveryId,
                CanCheckAgain = UnresolvedRecord(target, outcome.DeliveryId) is not null,
            },
            DeliveryCode.NeedsReconciliation => new()
            {
                Kind = FinalSaveKind.Uncertain, Details = details, DeliveryId = outcome.DeliveryId,
                Status = Format(Strings.FinalSave_Uncertain, RecordedFolder(target, outcome.DeliveryId) ?? string.Empty),
                CanCheckAgain = outcome.DeliveryId is not null,
            },
            DeliveryCode.DeliveredFileMissing => new()
            {
                Kind = FinalSaveKind.Missing, Details = details, DeliveryId = outcome.DeliveryId,
                Status = Format(Strings.FinalSave_Missing, path ?? string.Empty), CanSaveAnotherCopy = outcome.DeliveryId is not null,
            },
            DeliveryCode.DeliveredFileChanged => new()
            {
                Kind = FinalSaveKind.Changed, Details = details, DeliveryId = outcome.DeliveryId,
                Status = Format(Strings.FinalSave_Changed, path ?? string.Empty),
            },
            DeliveryCode.Unavailable => new()
            {
                Kind = FinalSaveKind.Unavailable, Details = details, DeliveryId = outcome.DeliveryId,
                Status = Format(Strings.FinalSave_Unavailable, path ?? string.Empty), CanCheckSaved = outcome.DeliveryId is not null,
            },
            DeliveryCode.ApprovalEvidenceMissing => Ineligible(ArtifactRefusal.ApprovalEvidenceMissing, details),
            DeliveryCode.IneligibleArtifact => Ineligible(ArtifactRefusal.Obsolete, details),
            DeliveryCode.SourceMissing or DeliveryCode.SourceChanged => new()
            {
                Kind = FinalSaveKind.Ineligible, Details = details,
                Status = Format(Strings.FinalSave_Ineligible, Strings.FinalSave_ReasonSourceChanged),
            },
            // A recorded save that may already have published stays "check again" whatever this
            // attempt to check it returned; only a definite prepublication record says "not saved" (R1).
            _ when outcome.DeliveryId is { } recorded && target.History?.FirstOrDefault(h => h.DeliveryId == recorded) is { } state &&
                   IsUnresolved(state) => new()
            {
                Kind = FinalSaveKind.Uncertain, Details = details, DeliveryId = recorded, CanCheckAgain = true,
                Status = Format(Strings.FinalSave_Uncertain, state.RequestedFolder),
                Alert = Strings.FinalSave_ActionIncomplete,
            },
            _ => new()
            {
                Kind = FinalSaveKind.Failed, Details = details, DeliveryId = outcome.DeliveryId,
                Status = Format(Strings.FinalSave_ApprovedNotSavedReason, FailReason(outcome.Code)),
            },
        };
    }

    private FinalSaveDisplay FromObservation(FinalSaveTarget target, DeliveryFileObservation observed)
    {
        string details = Technical(observed.RequestId, observed.DeliveryId, observed.Availability.ToString(), observed.Detail);
        return observed.Availability switch
        {
            DeliveryAvailability.VerifiedNow => Saved(observed.FinalPath, observed.DeliveryId, details),
            DeliveryAvailability.Missing => new()
            {
                Kind = FinalSaveKind.Missing, Details = details, DeliveryId = observed.DeliveryId,
                Status = Format(Strings.FinalSave_Missing, observed.FinalPath), CanSaveAnotherCopy = true,
            },
            DeliveryAvailability.Changed => new()
            {
                Kind = FinalSaveKind.Changed, Details = details, DeliveryId = observed.DeliveryId,
                Status = Format(Strings.FinalSave_Changed, observed.FinalPath),
            },
            DeliveryAvailability.Ineligible => Ineligible(ArtifactRefusal.Obsolete, details),
            _ => new()
            {
                Kind = FinalSaveKind.Unavailable, Details = details, DeliveryId = observed.DeliveryId,
                Status = Format(Strings.FinalSave_Unavailable, observed.FinalPath.Length > 0 ? observed.FinalPath : RecordedPath(target, observed.DeliveryId) ?? string.Empty),
                CanCheckSaved = true,
            },
        };
    }

    private FinalSaveDisplay FromHistory(FinalSaveTarget target)
    {
        // Unreadable history is not "no earlier saves", and not yet read is not "never saved":
        // nothing is claimed either way.
        if (target.History is null)
            return new()
            {
                Kind = FinalSaveKind.HistoryUnknown,
                Status = target.HistoryFailed ? Strings.FinalSave_HistoryUnavailable : Strings.FinalSave_HistoryLoading,
            };
        // History read before an unfinished action or a failed refresh stays visible as dated
        // history, but its absence of a save proves nothing about the action since (T3).
        bool dated = HistoryIsDated(target);
        if (DisplayedRecord(target) is not { } shown)
            return dated
                ? new() { Kind = FinalSaveKind.HistoryUnknown, Status = Strings.FinalSave_HistoryNotRefreshed }
                : new() { Kind = FinalSaveKind.ApprovedNotSaved, Status = Strings.FinalSave_ApprovedNotSaved };
        string details = Technical(shown.RequestId, shown.DeliveryId, shown.Status + "/" + shown.AttemptState, null);
        if (shown.Status == "Delivered")
            return new()
            {
                Kind = FinalSaveKind.SavedPreviously, Details = details, DeliveryId = shown.DeliveryId, SavedPath = shown.FinalPath,
                Status = Format(Strings.FinalSave_SavedPreviously, DeliveryDraftText.FileNameOf(shown.FinalPath), shown.RequestedFolder),
                CanCheckSaved = true, CanOpen = true,
            };
        // Only a durable ready checkpoint can have been published; earlier attempts cannot have
        // produced a final file, so they are offered as an ordinary retry at the recorded place.
        if (IsUnresolved(shown))
            return new()
            {
                Kind = FinalSaveKind.Uncertain, Details = details, DeliveryId = shown.DeliveryId,
                Status = Format(Strings.FinalSave_Uncertain, shown.RequestedFolder), CanCheckAgain = true,
            };
        return new()
        {
            Kind = dated ? FinalSaveKind.HistoryUnknown : FinalSaveKind.ApprovedNotSaved, Details = details, DeliveryId = shown.DeliveryId,
            Status = dated ? Strings.FinalSave_HistoryNotRefreshed : Strings.FinalSave_ApprovedNotSaved, CanRetryRecorded = true,
        };
    }

    internal static bool IsUnresolved(DeliveryState state) => state.Status != "Delivered" &&
        state.AttemptState is DeliveryAttemptState.ReadyToPublish or DeliveryAttemptState.NeedsReconciliation;

    private static DeliveryState? UnresolvedRecord(FinalSaveTarget target, Guid? deliveryId) => deliveryId is null ? null :
        target.History?.FirstOrDefault(h => h.DeliveryId == deliveryId && IsUnresolved(h));

    private static bool HistoryIsDated(FinalSaveTarget target) =>
        target.History is not null && (target.HistoryFailed || target.HistoryStale);

    /// <summary>
    /// The recorded delivery the section speaks about: the one the operator chose from the
    /// recorded saves, otherwise an unresolved save that may have published ("check this save
    /// again"), then the newest verified delivery, then the newest record. Every other record
    /// stays listed and selectable; none is hidden by a newer one.
    /// </summary>
    private static DeliveryState? DisplayedRecord(FinalSaveTarget target) => target.History is not { } history ? null :
        (target.ShownDeliveryId is { } chosen ? history.FirstOrDefault(h => h.DeliveryId == chosen) : null) ??
        history.FirstOrDefault(IsUnresolved) ?? history.FirstOrDefault(h => h.Status == "Delivered") ?? history.FirstOrDefault();

    private static string? ActionIncomplete(FinalSaveTarget target) =>
        target.ActionError is null ? null : Strings.FinalSave_ActionIncomplete;

    private static string? JoinLines(params string?[] lines)
    {
        string[] present = lines.Where(l => !string.IsNullOrEmpty(l)).Select(l => l!).ToArray();
        return present.Length == 0 ? null : string.Join(Environment.NewLine, present);
    }

    private static FinalSaveDisplay Saved(string finalPath, Guid? deliveryId, string details) => new()
    {
        Kind = FinalSaveKind.Saved, Details = details, DeliveryId = deliveryId, SavedPath = finalPath,
        Status = Format(Strings.FinalSave_Saved, DeliveryDraftText.FileNameOf(finalPath), DeliveryDraftText.FolderOf(finalPath)),
        CanOpen = deliveryId is not null, CanCheckSaved = deliveryId is not null,
    };

    private static FinalSaveDisplay Ineligible(ArtifactRefusal refusal, string? details) => new()
    {
        Kind = FinalSaveKind.Ineligible, Details = details, Status = Format(Strings.FinalSave_Ineligible, RefusalReason(refusal)),
    };

    private static string? ApprovalAlert(ApprovalFact fact) => fact.Result switch
    {
        null => Strings.FinalSave_ActionIncomplete,
        // An already approved result keeps its approval: only the save was refused.
        { DraftRefusal: { } refusedSave, CombinedReview: false } =>
            Format(Strings.FinalSave_ApprovedNotSavedReason, DraftReason(refusedSave)),
        { DraftRefusal: { } draft } => Format(Strings.FinalSave_DraftRefused, DraftReason(draft)),
        { Approval: ApprovalOutcome.Refused } => Strings.FinalSave_ApprovalRefused,
        { Approval: ApprovalOutcome.Unknown } => Strings.FinalSave_ApprovalUnknown,
        { Approval: ApprovalOutcome.NotAttempted, ApprovalFailure: not null } => Strings.FinalSave_ApprovalRefused,
        _ => null,
    };

    private static string? ApprovalDetails(ApprovalFact fact) => fact.Result is { } result
        ? Technical(result.RequestId, result.Delivery?.DeliveryId,
            $"{result.Approval}/{result.PngPreparation}/{result.OfferRefusal}/{result.DraftRefusal?.Refusal}",
            result.ApprovalFailure?.TechnicalDetail ?? result.DraftRefusal?.Detail)
        : fact.Error;

    private static string DraftReason(DeliveryDraftCheck draft) => draft.Refusal switch
    {
        DeliveryCode.InvalidName when draft.EffectiveFileName is { } effective =>
            Format(Strings.FinalSave_NameExtensionAdded, effective),
        { } code => FailReason(code),
        null => Strings.FinalSave_FailOther,
    };

    private static string FailReason(DeliveryCode code) => code switch
    {
        DeliveryCode.Cancelled => Strings.FinalSave_FailCancelled,
        DeliveryCode.DestinationNotWritable => Strings.FinalSave_FailNotWritable,
        DeliveryCode.DestinationUnavailable => Strings.FinalSave_FailUnavailable,
        DeliveryCode.UnsupportedDestination => Strings.FinalSave_FailUnsupported,
        DeliveryCode.ProtectedDestination => Strings.FinalSave_FailProtected,
        DeliveryCode.CopyFailed => Strings.FinalSave_FailCopy,
        _ when DeliveryDraftText.IsCopyMismatch(code) => Strings.FinalSave_FailVerify,
        DeliveryCode.PersistenceFailed => Strings.FinalSave_FailPersistence,
        DeliveryCode.InvalidName => Strings.FinalSave_FailInvalidName,
        DeliveryCode.RequestConflict => Strings.FinalSave_FailConflict,
        _ => Strings.FinalSave_FailOther,
    };

    private static string RefusalReason(ArtifactRefusal refusal) => refusal switch
    {
        ArtifactRefusal.ApprovalEvidenceMissing => Strings.FinalSave_ReasonApprovalEvidenceMissing,
        ArtifactRefusal.PendingReview => Strings.FinalSave_ReasonPending,
        ArtifactRefusal.Rejected => Strings.FinalSave_ReasonRejected,
        ArtifactRefusal.Invalidated => Strings.FinalSave_ReasonInvalidated,
        ArtifactRefusal.Recycled => Strings.FinalSave_ReasonRecycled,
        ArtifactRefusal.Obsolete => Strings.FinalSave_ReasonObsolete,
        ArtifactRefusal.InconsistentLineage => Strings.FinalSave_ReasonInconsistent,
        _ => Strings.FinalSave_ReasonMissing,
    };

    private static string OpenNotice(OpenFolderOutcome open) => open.Code switch
    {
        OpenFolderCode.Dispatched => Format(Strings.FinalSave_OpenDispatched, DeliveryDraftText.FileNameOf(open.FinalPath ?? string.Empty)),
        OpenFolderCode.Missing => Strings.FinalSave_OpenMissing,
        OpenFolderCode.Changed => Strings.FinalSave_OpenChanged,
        OpenFolderCode.Unavailable => Strings.FinalSave_OpenUnavailable,
        OpenFolderCode.Ineligible => Strings.FinalSave_OpenIneligible,
        OpenFolderCode.Uncertain => Strings.FinalSave_OpenUncertain,
        _ => Format(Strings.FinalSave_OpenShellFailed, open.FinalPath ?? string.Empty),
    };

    private string Summary(FinalSaveTarget target)
    {
        string name = EffectiveName(target.FileName, target.Kind).Effective ?? target.FileName;
        string type = target.Kind == ArtifactKind.ApprovedAssetPng ? Strings.FinalSave_TypePng
            : TiffMillimetres(target) is { } size ? Format(Strings.FinalSave_TypeTiff, size.Width, size.Height)
            : Strings.FinalSave_TypeTiffSizePending;
        return string.Join(" · ", Format(Strings.FinalSave_SaveAs, name), type, Format(Strings.FinalSave_FolderValue, SaveFolderText));
    }

    /// <summary>Output-bound physical size from backend metadata only; never draft bounds or another size.</summary>
    private (double Width, double Height)? TiffMillimetres(FinalSaveTarget target)
    {
        if (target.Offer?.Artifact is { PhysicalWidthMm: { } width, PhysicalHeightMm: { } height }) return (width, height);
        if (target.PendingReview is { } review && _tiffReview is { } tiff && tiff.PrintOutputId.Value == review.RevisionId.Value)
            return (tiff.PhysicalWidthMm, tiff.PhysicalHeightMm);
        return null;
    }

    private string DraftNotice(FinalSaveTarget target)
    {
        (string? effective, string? problem, string? notice) = EffectiveName(target.FileName, target.Kind);
        List<string> lines = [];
        if (problem is not null) lines.Add(problem);
        else if (notice is not null) lines.Add(notice);
        if (target.Folder.Length == 0)
            lines.Add(_preferenceFailed ? Strings.FinalSave_PreferenceUnavailable : Strings.FinalSave_NoFolder);
        else if (target.FolderFromPreference && !target.FolderEdited)
            lines.Add(Strings.FinalSave_RememberedFolder);
        _ = effective;
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// The complete effective name shown before confirmation. A missing extension is added
    /// visibly; a wrong one is refused, never converted. The backend revalidates everything.
    /// </summary>
    internal static (string? Effective, string? Problem, string? Notice) EffectiveName(string text, ArtifactKind kind)
    {
        (string? effective, DraftNameProblem problem, bool added) = DeliveryDraftText.EffectiveFileName(text, kind);
        return problem switch
        {
            DraftNameProblem.Invalid => (null, Strings.FinalSave_NameInvalid, null),
            DraftNameProblem.WrongType => (null, Format(Strings.FinalSave_NameWrongType,
                kind == ArtifactKind.ApprovedAssetPng ? "PNG" : "TIFF",
                kind == ArtifactKind.ApprovedAssetPng ? ".png" : ".tif / .tiff"), null),
            _ => (effective, null, added ? Format(Strings.FinalSave_NameExtensionAdded, effective!) : null),
        };
    }

    private static string? RecordedFolder(FinalSaveTarget target, Guid? deliveryId) =>
        target.History?.FirstOrDefault(h => h.DeliveryId == deliveryId)?.RequestedFolder ??
        DisplayedRecord(target)?.RequestedFolder ?? target.Captured?.Folder;

    private static string? RecordedPath(FinalSaveTarget target, Guid? deliveryId) =>
        target.History?.FirstOrDefault(h => h.DeliveryId == deliveryId)?.FinalPath ?? DisplayedRecord(target)?.FinalPath;

    private static string Technical(Guid requestId, Guid? deliveryId, string code, string? detail) =>
        $"RequestId {requestId:D}; DeliveryId {(deliveryId is { } id ? id.ToString("D") : "-")}; {code}" +
        (string.IsNullOrWhiteSpace(detail) ? string.Empty : "; " + detail);

    private static string Format(string format, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);

    private static readonly string[] FinalSaveProperties =
    [
        nameof(HasFinalSave), nameof(HasMultipleFinalSaveTargets), nameof(ShowsSaveDraft), nameof(SelectedFinalSaveTarget), nameof(SaveFileNameText),
        nameof(SaveFolderText), nameof(SaveSummary), nameof(SaveDraftNotice), nameof(HasSaveDraftNotice),
        nameof(FinalSaveStatus), nameof(FinalSaveAlert), nameof(HasFinalSaveAlert), nameof(FinalSaveSecondary),
        nameof(HasFinalSaveSecondary), nameof(FinalSaveSavedPath), nameof(HasFinalSaveSavedPath), nameof(FinalSaveDetails),
        nameof(HasFinalSaveDetails), nameof(FinalSaveOpenNotice), nameof(HasFinalSaveOpenNotice), nameof(IsSaveDraftEditable),
        nameof(IsFinalSaveWorking), nameof(FinalSaveProgress), nameof(HasFinalSaveProgress), nameof(CanConfirmAndSave),
        nameof(CanSaveApproved), nameof(SaveApprovedLabel), nameof(CanRetryRecordedSave), nameof(CanCheckSaveAgain),
        nameof(CanCheckSavedFile), nameof(CanOpenContainingFolder), nameof(CanSaveAnotherCopy), nameof(CanUseSuggestedName),
        nameof(UseSuggestionLabel), nameof(CanCancelSave), nameof(CanChangeLocation), nameof(ConfirmAndSaveIdentity),
        nameof(SaveApprovedIdentity), nameof(RecordedActionIdentity), nameof(HasFinalSaveRecords), nameof(SelectedFinalSaveRecord),
    ];

    /// <summary>Row objects are kept per DeliveryId; the list changes only when the records do.</summary>
    private void SyncFinalSaveRecords()
    {
        IReadOnlyList<DeliveryState> records = SelectedSave?.History ?? [];
        if (!FinalSaveRecords.Select(r => r.DeliveryId).SequenceEqual(records.Select(r => r.DeliveryId)))
        {
            FinalSaveRecords.Clear();
            foreach (DeliveryState record in records)
            {
                if (!_finalSaveRecordRows.TryGetValue(record.DeliveryId, out FinalSaveRecordRow? row))
                    _finalSaveRecordRows[record.DeliveryId] = row = new FinalSaveRecordRow(record);
                FinalSaveRecords.Add(row);
            }
        }
        foreach (DeliveryState record in records) _finalSaveRecordRows[record.DeliveryId].Update(record);
    }

    private void NotifyFinalSaveChanged()
    {
        SyncFinalSaveRecords();
        foreach (string property in FinalSaveProperties) OnPropertyChanged(property);
        foreach (FinalSaveTargetRow row in FinalSaveTargets) row.Refresh();
        OnPropertyChanged(nameof(RecommendedCommand));
        OnPropertyChanged(nameof(NextStepText));
    }
}
