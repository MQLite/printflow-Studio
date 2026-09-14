using System.Collections.Immutable;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// The production <see cref="IMeituUiDriver"/>: every interaction is preceded by a fresh
/// identity check of the target (Epic 11300 Part A §3, §10, §19).
/// </summary>
/// <remarks>
/// The safety property this class exists to hold is an <i>ordering</i> one, and it is worth
/// stating plainly because it is the whole point of the epic: verify, then act — never act,
/// then verify. <see cref="VerifyTargetAsync"/> re-reads the process, the window and the
/// foreground from the OS, and every input path calls it first and abandons the input on any
/// mismatch. If Explorer takes the foreground mid-sequence, PrintFlow reports
/// <see cref="FailureCode.MeituTargetLost"/> having sent nothing — the failure that a
/// previously observed stray keystroke into an Explorer rename field should have produced.
///
/// The verification and the input that follows it are not atomic; no Windows API makes them so.
/// What the ordering guarantees is that PrintFlow never acts on an assumption it has not just
/// checked, and that the check is inside the seam rather than left to each caller.
/// </remarks>
public sealed class GuardedMeituUiDriver : IMeituUiDriver
{
    private readonly IExternalAppWindowLocator _locator;
    private readonly IUiElementProvider _elements;
    private readonly IScopedInputSink _input;
    private readonly IAutomationEvidenceSink _evidence;
    private readonly IMeituBaselineProvider _baselines;
    private readonly MeituAutomationOptions _options;
    private readonly TimeProvider _clock;

    public GuardedMeituUiDriver(
        IExternalAppWindowLocator locator,
        IUiElementProvider elements,
        IScopedInputSink input,
        IAutomationEvidenceSink evidence,
        IMeituBaselineProvider baselines,
        MeituAutomationOptions options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(baselines);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _locator = locator;
        _elements = elements;
        _input = input;
        _evidence = evidence;
        _baselines = baselines;
        _options = options;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<OperationResult<MeituStateSnapshot>> InspectStateAsync(
        MeituTarget target, string? expectedWorkingCopyFileName, CancellationToken cancellationToken) =>
        InspectStateCoreAsync(
            target, expectedWorkingCopyFileName, observedDocumentIdentity: null, cancellationToken);

    private async Task<OperationResult<MeituStateSnapshot>> InspectStateCoreAsync(
        MeituTarget target,
        string? expectedWorkingCopyFileName,
        string? observedDocumentIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(baseline.Failure);
        }

        // Reading is not input, so the foreground is deliberately not required here: PrintFlow
        // must be able to look at Meitu while the operator is using another application, and
        // demanding focus in order to observe would itself be a screen-stealing side effect.
        // Ownership is still re-checked, because reading the wrong window is its own hazard.
        OperationResult<ExternalWindowRef> window = RefreshOwnedWindow(target);
        if (window.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(window.Failure);
        }

        OperationResult<IReadOnlyList<string>> texts =
            _elements.ReadTextSnapshot(window.Value.Handle, _options.SnapshotItemLimit);
        if (texts.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(texts.Failure);
        }

        OperationResult<IReadOnlyList<ExternalWindowRef>> dialogs =
            _locator.FindOwnedDialogs(target.Process, window.Value);
        if (dialogs.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(dialogs.Failure);
        }

        MeituObservation observation = new(
            window.Value.Title,
            [.. texts.Value],
            [.. dialogs.Value.Select(d => d.Title)],
            window.Value.IsEnabled,
            expectedWorkingCopyFileName,
            observedDocumentIdentity);

        await Task.CompletedTask.ConfigureAwait(false);
        return OperationResult.Ok(MeituStateClassifier.Classify(baseline.Value, observation));
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituTarget>> ActivateAsync(
        MeituTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        OperationResult<ExternalWindowRef> window = RefreshOwnedWindow(target);
        if (window.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(window.Failure);
        }

        OperationResult<Unit> activated = _locator.Activate(window.Value);
        if (activated.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(activated.Failure);
        }

        // Asking is not the same as receiving: Windows may decline a foreground change. Poll
        // until the OS agrees the target is in front, and fail closed if it never does (§10).
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.ActivationTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<ForegroundIdentity> foreground = _locator.ReadForeground();
            if (foreground.IsFailure)
            {
                return OperationResult.Fail<MeituTarget>(foreground.Failure);
            }

            if (foreground.Value.Handle == window.Value.Handle)
            {
                OperationResult<ExternalWindowRef> settled = RefreshOwnedWindow(target);
                return settled.IsFailure
                    ? OperationResult.Fail<MeituTarget>(settled.Failure)
                    : OperationResult.Ok(target with { Window = settled.Value });
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<MeituTarget>(TargetLost(
                    window.Value.Handle, foreground.Value,
                    "Meitu did not come to the foreground within the activation timeout."));
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public OperationResult<UiElementRef> FindKnownElement(MeituTarget target, KnownMeituElement element)
    {
        ArgumentNullException.ThrowIfNull(target);

        OperationResult<ExternalWindowRef> window = RefreshOwnedWindow(target);
        if (window.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(window.Failure);
        }

        return element switch
        {
            KnownMeituElement.WelcomeOpenEntry => FindStartPageCard(target, window.Value.Handle),
            KnownMeituElement.EditorOpenControl => FindEditorOpenControl(target, window.Value.Handle),
            KnownMeituElement.EditorSaveControl => FindEditorSaveControl(target, window.Value.Handle),
            KnownMeituElement.EditorCloseDocumentControl =>
                FindEditorCloseDocumentControl(target, window.Value.Handle),
            KnownMeituElement.EditorEnhancementAction =>
                FindEditorEnhancementAction(target, window.Value.Handle),
            KnownMeituElement.EditorBackgroundRemovalAction =>
                FindEditorBackgroundRemovalAction(target, window.Value.Handle),
            KnownMeituElement.EditorBackgroundRemovalReturn =>
                FindEditorBackgroundRemovalReturn(target, window.Value.Handle),

            // The export controls exist, but not here. Every one of them lives on a surface the
            // editor raises — Meitu's owned Save panel, or the destination dialog — and resolving
            // them against the editor window would search a screen they are not on and refuse for
            // a reason that had nothing to do with why. The export route holds those handles and
            // resolves them itself; this seam says so rather than returning a misleading miss.
            KnownMeituElement.ExportFileNameField or
            KnownMeituElement.ExportFormatField or
            KnownMeituElement.ExportSaveAsControl or
            KnownMeituElement.ExportDestinationFileName or
            KnownMeituElement.ExportDestinationConfirmButton or
            KnownMeituElement.ExportDestinationCancelButton or
            KnownMeituElement.ExportResultCloseControl => OperationResult.Fail<UiElementRef>(
                FailureCode.MeituUnknownState,
                $"'{element}' belongs to a surface the editor raises, not to the editor window, so it " +
                "cannot be resolved from the main target. The export route resolves it against the " +
                "surface it has verified. Nothing was written or invoked."),

            _ => FindDialogControl(target, window.Value.Handle, element),
        };
    }

    private OperationResult<UiElementRef> FindEditorSaveControl(MeituTarget target, WindowHandle window)
    {
        OperationResult<MeituDocumentIdentitySignature> signature = DocumentIdentitySignature();
        return signature.IsFailure
            ? OperationResult.Fail<UiElementRef>(signature.Failure)
            : FindStructuredOwner(
                target, window, signature.Value.SaveMarkerName, signature.Value.SaveControl);
    }

    /// <summary>Locates the empty editor's own open control from its signed signature.</summary>
    private OperationResult<UiElementRef> FindEditorOpenControl(MeituTarget target, WindowHandle window)
    {
        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(baseline.Failure);
        }

        if (baseline.Value.EditorEmpty?.OpenControl is not { } signature)
        {
            return OperationResult.Fail<UiElementRef>(
                FailureCode.MeituUnknownState,
                "The verified evidence chain records no open control for the Meitu editor, so PrintFlow has " +
                "no signed way to raise its picker. Nothing was invoked.");
        }

        return FindSignedControl(target, window, signature);
    }

    /// <summary>
    /// Finds the one element matching a signed control signature beneath a verified window.
    /// </summary>
    /// <remarks>
    /// The search is by name and the decision is by the whole signature. Narrowing the query
    /// first keeps the tree walk cheap; deciding on the full signature afterwards is what stops
    /// a same-named control elsewhere on the screen from being accepted.
    /// </remarks>
    private OperationResult<UiElementRef> FindSignedControl(
        MeituTarget target,
        WindowHandle window,
        MeituControlSignature signature,
        IReadOnlyCollection<UiPatternKind>? additionalRequiredPatterns = null)
    {
        OperationResult<IReadOnlyList<UiElementRef>> found = _elements.FindAll(
            window,
            new UiElementQuery(
                UiControlKind.Any, Name: signature.Name.Length > 0 ? signature.Name : null));
        if (found.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(found.Failure);
        }

        List<UiElementRef> elements = [];
        List<UiElementIdentity> identities = [];

        foreach (UiElementRef candidate in found.Value)
        {
            OperationResult<UiElementIdentity> identity = _elements.Describe(candidate);
            if (identity.IsFailure)
            {
                continue;
            }

            if (additionalRequiredPatterns is not null &&
                additionalRequiredPatterns.Any(pattern => !identity.Value.Supports(pattern)))
            {
                continue;
            }

            elements.Add(candidate);
            identities.Add(identity.Value);
        }

        OperationResult<int> chosen = MeituCardTargetRule.SelectSignedControl(
            signature, target.Process.ProcessId, identities);

        return chosen.IsFailure
            ? OperationResult.Fail<UiElementRef>(chosen.Failure)
            : OperationResult.Ok(elements[chosen.Value]);
    }

    /// <summary>
    /// Resolves the start-page entry to the card that owns the signed marker, never to the
    /// marker itself (Epic 11300 Part B1 §3, §4, §5).
    /// </summary>
    /// <remarks>
    /// The direction of travel is the whole fix. Part A searched the window for an element
    /// <i>named</i> 图片编辑 and invoked what it found, which was the card's title label — a
    /// <c>Text</c> element that advertises <c>InvokePattern</c>, reports success, and does
    /// nothing. Here the named element is only ever an anchor: PrintFlow walks up from it and
    /// invokes the ancestor, and only if that ancestor matches the shape signed evidence
    /// records.
    ///
    /// Every candidate is walked and described before any decision is taken, so "there are two
    /// of these" is a fact the rule can see rather than a first match it silently accepts.
    /// </remarks>
    private OperationResult<UiElementRef> FindStartPageCard(MeituTarget target, WindowHandle window)
    {
        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(baseline.Failure);
        }

        if (baseline.Value.StartPageCard is not { } shape)
        {
            // No signed card shape means no reviewed way to tell a card from its label, and the
            // Part A defect is precisely what happens when that distinction is assumed (§10).
            return OperationResult.Fail<UiElementRef>(
                FailureCode.MeituUnknownState,
                "The verified evidence chain carries no start-page card structure, so PrintFlow has no " +
                "signed way to tell a card from the label that titles it and will not invoke either.");
        }

        string markerName = _options.WelcomeOpenEntryName;

        // Retained from Part A: the configured entry must be one the signed clean-start evidence
        // actually records, so the option cannot become a back door for naming any control.
        if (!baseline.Value.WelcomeMarkers.Contains(markerName, StringComparer.Ordinal))
        {
            return OperationResult.Fail<UiElementRef>(
                FailureCode.MeituUnknownState,
                $"'{markerName}' is not one of the {baseline.Value.WelcomeMarkers.Length} markers the signed " +
                "clean-start evidence records, so PrintFlow will not look for it.");
        }

        OperationResult<IReadOnlyList<UiElementRef>> markers =
            _elements.FindAll(window, new UiElementQuery(UiControlKind.Any, Name: markerName));
        if (markers.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(markers.Failure);
        }

        List<UiElementRef> owners = [];
        List<MeituCardCandidate> candidates = [];

        foreach (UiElementRef marker in markers.Value)
        {
            OperationResult<UiElementIdentity> markerIdentity = _elements.Describe(marker);
            if (markerIdentity.IsFailure)
            {
                // A marker that cannot be read is not a marker that can be trusted to anchor a
                // walk; drop it as a candidate rather than let a partial read decide anything.
                continue;
            }

            OperationResult<UiElementRef> parent = _elements.GetParent(marker);
            OperationResult<UiElementIdentity> ownerIdentity = parent.IsSuccess
                ? _elements.Describe(parent.Value)
                : OperationResult.Fail<UiElementIdentity>(parent.Failure);

            owners.Add(parent.IsSuccess ? parent.Value : marker);
            candidates.Add(new MeituCardCandidate(
                markerIdentity.Value, ownerIdentity.IsSuccess ? ownerIdentity.Value : null));
        }

        OperationResult<int> chosen = MeituCardTargetRule.SelectOwningCard(
            shape, markerName, target.Process.ProcessId, candidates);

        return chosen.IsFailure
            ? OperationResult.Fail<UiElementRef>(chosen.Failure)
            : OperationResult.Ok(owners[chosen.Value]);
    }

    /// <summary>
    /// Resolves a signed text marker to the one parent control whose structural relationship
    /// matches the evidence. Used by the loaded editor's Save control after its shape was
    /// observed independently from the start-page card.
    /// </summary>
    private OperationResult<UiElementRef> FindStructuredOwner(
        MeituTarget target, WindowHandle window, string markerName, MeituCardShape shape)
    {
        OperationResult<IReadOnlyList<UiElementRef>> markers = _elements.FindAll(
            window, new UiElementQuery(UiControlKind.Any, Name: markerName));
        if (markers.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(markers.Failure);
        }

        List<UiElementRef> owners = [];
        List<MeituCardCandidate> candidates = [];
        foreach (UiElementRef marker in markers.Value)
        {
            OperationResult<UiElementIdentity> markerIdentity = _elements.Describe(marker);
            if (markerIdentity.IsFailure)
            {
                continue;
            }

            OperationResult<UiElementRef> parent = _elements.GetParent(marker);
            OperationResult<UiElementIdentity> ownerIdentity = parent.IsSuccess
                ? _elements.Describe(parent.Value)
                : OperationResult.Fail<UiElementIdentity>(parent.Failure);

            owners.Add(parent.IsSuccess ? parent.Value : marker);
            candidates.Add(new MeituCardCandidate(
                markerIdentity.Value, ownerIdentity.IsSuccess ? ownerIdentity.Value : null));
        }

        OperationResult<int> chosen = MeituCardTargetRule.SelectOwningCard(
            shape, markerName, target.Process.ProcessId, candidates);
        return chosen.IsFailure
            ? OperationResult.Fail<UiElementRef>(chosen.Failure)
            : OperationResult.Ok(owners[chosen.Value]);
    }

    private OperationResult<UiElementRef> FindEditorCloseDocumentControl(
        MeituTarget target, WindowHandle window)
    {
        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(baseline.Failure);
        }

        return baseline.Value.CloseDocument is { } signature
            ? FindStructuredOwner(target, window, signature.MarkerName, signature.Control)
            : OperationResult.Fail<UiElementRef>(
                FailureCode.MeituUnknownState,
                "The verified evidence chain records no close-document control, so PrintFlow has no signed " +
                "way to return the editor to its empty state. Nothing was invoked.");
    }

    private OperationResult<UiElementRef> FindEditorEnhancementAction(
        MeituTarget target, WindowHandle window)
    {
        OperationResult<MeituEnhancementSignature> signature = EnhancementSignature();
        return signature.IsFailure
            ? OperationResult.Fail<UiElementRef>(signature.Failure)
            : FindActionOwner(
                target, window, signature.Value.ActionMarkerName, signature.Value.ActionControl);
    }

    private OperationResult<UiElementRef> FindEditorBackgroundRemovalAction(
        MeituTarget target, WindowHandle window)
    {
        OperationResult<MeituBackgroundRemovalSignature> signature = BackgroundRemovalSignature();
        return signature.IsFailure
            ? OperationResult.Fail<UiElementRef>(signature.Failure)
            : FindBackgroundRemovalOwner(
                target,
                window,
                signature.Value.ActionMarkerName,
                signature.Value.ActionControl);
    }

    private OperationResult<UiElementRef> FindEditorBackgroundRemovalReturn(
        MeituTarget target, WindowHandle window)
    {
        OperationResult<MeituBackgroundRemovalSignature> signature = BackgroundRemovalSignature();
        return signature.IsFailure
            ? OperationResult.Fail<UiElementRef>(signature.Failure)
            : FindBackgroundRemovalOwner(
                target,
                window,
                signature.Value.ReturnMarkerName,
                signature.Value.ReturnControl);
    }

    private OperationResult<UiElementRef> FindBackgroundRemovalOwner(
        MeituTarget target,
        WindowHandle window,
        string markerName,
        MeituOwnedControlShape shape)
    {
        if (shape.OwnerAncestorDepth < 0 || shape.OwnerAncestorDepth > 4)
        {
            return OperationResult.Fail<UiElementRef>(
                FailureCode.MeituUnknownState,
                $"The signed Background Removal owner depth {shape.OwnerAncestorDepth} is outside 0..4. " +
                "Nothing was invoked.");
        }

        OperationResult<IReadOnlyList<UiElementRef>> markers = _elements.FindAll(
            window, new UiElementQuery(UiControlKind.Any, Name: markerName));
        if (markers.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(markers.Failure);
        }

        List<UiElementRef> owners = [];
        List<MeituCardCandidate> candidates = [];
        foreach (UiElementRef marker in markers.Value)
        {
            OperationResult<UiElementIdentity> markerIdentity = _elements.Describe(marker);
            if (markerIdentity.IsFailure)
            {
                continue;
            }

            UiElementRef current = marker;
            UiElementIdentity? ownerIdentity = markerIdentity.Value;
            for (int level = 0; level < shape.OwnerAncestorDepth; level++)
            {
                OperationResult<UiElementRef> parent = _elements.GetParent(current);
                if (parent.IsFailure)
                {
                    ownerIdentity = null;
                    break;
                }

                current = parent.Value;
                OperationResult<UiElementIdentity> described = _elements.Describe(current);
                ownerIdentity = described.IsSuccess ? described.Value : null;
                if (ownerIdentity is null)
                {
                    break;
                }
            }

            owners.Add(ownerIdentity is null ? marker : current);
            candidates.Add(new MeituCardCandidate(markerIdentity.Value, ownerIdentity));
        }

        OperationResult<int> chosen = MeituBackgroundRemovalTargetRule.SelectActionOwner(
            shape, markerName, target.Process.ProcessId, candidates);
        return chosen.IsFailure
            ? OperationResult.Fail<UiElementRef>(chosen.Failure)
            : OperationResult.Ok(owners[chosen.Value]);
    }

    /// <summary>
    /// Resolves a signed text marker to the actionable control that owns it, walking exactly the
    /// number of control-view levels the evidence records (Epic 11300 Part B2A §7, §8).
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="FindStructuredOwner"/> for controls whose actionable owner
    /// is not the marker's immediate parent. The depth is read from signed evidence and the walk
    /// stops there — it does not continue upward looking for something that matches, because
    /// "keep going until a candidate passes" would let the rule's own shape checks decide how
    /// far to search, and in Meitu's Qt tree the window itself passes several of them.
    ///
    /// A level that cannot be read ends the walk with no owner, which the rule treats as a
    /// refusal rather than as licence to try the next level up.
    /// </remarks>
    private OperationResult<UiElementRef> FindActionOwner(
        MeituTarget target, WindowHandle window, string markerName, MeituOwnedControlShape shape)
    {
        if (shape.OwnerAncestorDepth <= 0)
        {
            // Depth zero would make the marker its own owner — the Part A defect written into
            // evidence — so it is refused here rather than trusted to fail a later shape check.
            return OperationResult.Fail<UiElementRef>(
                FailureCode.MeituUnknownState,
                $"The signed Enhancement evidence records an owner depth of {shape.OwnerAncestorDepth}, " +
                "which would make the text marker its own action target. Nothing was invoked.");
        }

        OperationResult<IReadOnlyList<UiElementRef>> markers = _elements.FindAll(
            window, new UiElementQuery(UiControlKind.Any, Name: markerName));
        if (markers.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(markers.Failure);
        }

        List<UiElementRef> owners = [];
        List<MeituCardCandidate> candidates = [];

        foreach (UiElementRef marker in markers.Value)
        {
            OperationResult<UiElementIdentity> markerIdentity = _elements.Describe(marker);
            if (markerIdentity.IsFailure)
            {
                continue;
            }

            UiElementRef current = marker;
            UiElementIdentity? ownerIdentity = null;
            for (int level = 0; level < shape.OwnerAncestorDepth; level++)
            {
                OperationResult<UiElementRef> parent = _elements.GetParent(current);
                if (parent.IsFailure)
                {
                    ownerIdentity = null;
                    break;
                }

                current = parent.Value;
                OperationResult<UiElementIdentity> described = _elements.Describe(current);
                ownerIdentity = described.IsSuccess ? described.Value : null;
                if (ownerIdentity is null)
                {
                    break;
                }
            }

            // The element at the signed depth is recorded even when it was unreadable, so the
            // rule sees "no owner" rather than an owner one level short of the evidence.
            owners.Add(ownerIdentity is null ? marker : current);
            candidates.Add(new MeituCardCandidate(markerIdentity.Value, ownerIdentity));
        }

        OperationResult<int> chosen = MeituEnhancementTargetRule.SelectActionOwner(
            shape, markerName, target.Process.ProcessId, candidates);

        return chosen.IsFailure
            ? OperationResult.Fail<UiElementRef>(chosen.Failure)
            : OperationResult.Ok(owners[chosen.Value]);
    }

    /// <summary>The signed Enhancement route, or a refusal when the chain vouches for none.</summary>
    private OperationResult<MeituEnhancementSignature> EnhancementSignature()
    {
        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<MeituEnhancementSignature>(baseline.Failure);
        }

        return baseline.Value.Enhancement is { } signature
            ? OperationResult.Ok(signature)
            : OperationResult.Fail<MeituEnhancementSignature>(
                FailureCode.MeituUnknownState,
                "The verified evidence chain carries no Enhancement signature, so PrintFlow has no signed " +
                "description of the control it would invoke, of what Meitu looks like while it works, or " +
                "of what finishing looks like. Nothing was invoked.");
    }

    private OperationResult<MeituBackgroundRemovalSignature> BackgroundRemovalSignature()
    {
        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<MeituBackgroundRemovalSignature>(baseline.Failure);
        }

        return baseline.Value.BackgroundRemoval is { } signature
            ? OperationResult.Ok(signature)
            : OperationResult.Fail<MeituBackgroundRemovalSignature>(
                FailureCode.MeituUnknownState,
                "The verified evidence chain carries no Background Removal signature. Nothing was invoked.");
    }

    /// <summary>Locates a picker control by the automation id the signed evidence records.</summary>
    /// <remarks>
    /// The control type is verified after the lookup rather than folded into the query, because
    /// the evidence records it as the type a person saw in an inspector and the closed
    /// <see cref="UiControlKind"/> set does not name every one of them. Checking it afterwards
    /// is also the stronger order: the id has to be right <i>and</i> what it found has to be the
    /// kind of thing the evidence says it is.
    /// </remarks>
    private OperationResult<UiElementRef> FindDialogControl(
        MeituTarget target, WindowHandle dialog, KnownMeituElement element)
    {
        OperationResult<MeituFileDialogSignature> signature = FileDialogSignature();
        if (signature.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(signature.Failure);
        }

        (string automationId, string controlType) = element == KnownMeituElement.FileDialogFileName
            ? (signature.Value.FileNameAutomationId, signature.Value.FileNameControlType)
            : (signature.Value.ConfirmAutomationId, signature.Value.ConfirmControlType);

        // Every match, not the first. An automation id is not unique inside a Windows common
        // dialog: the file-name field is an Edit nested inside a ComboBox, and both report id
        // 1148. Taking the first descendant would hand back the ComboBox, so the control type
        // recorded in the evidence is what selects between them — and the selection has to be
        // unique before anything is written.
        OperationResult<IReadOnlyList<UiElementRef>> found = _elements.FindAll(
            dialog, new UiElementQuery(UiControlKind.Any, AutomationId: automationId));
        if (found.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(found.Failure);
        }

        List<UiElementRef> matches = [];
        foreach (UiElementRef candidate in found.Value)
        {
            OperationResult<UiElementIdentity> identity = _elements.Describe(candidate);
            if (identity.IsFailure ||
                !string.Equals(identity.Value.ControlTypeName, controlType, StringComparison.Ordinal))
            {
                continue;
            }

            if (identity.Value.ProcessId != target.Process.ProcessId)
            {
                return OperationResult.Fail<UiElementRef>(
                    FailureCode.MeituTargetLost,
                    $"The picker control '{automationId}' belongs to process {identity.Value.ProcessId}, not " +
                    $"the verified Meitu process {target.Process.ProcessId}. Nothing was written or invoked.");
            }

            matches.Add(candidate);
        }

        return matches.Count == 1
            ? OperationResult.Ok(matches[0])
            : OperationResult.Fail<UiElementRef>(
                FailureCode.MeituOpenInputFailed,
                $"The picker has {matches.Count} control(s) with automation id '{automationId}' of type " +
                $"{controlType}, and the signed evidence describes exactly one. Nothing was written or invoked.");
    }

    /// <summary>The signed picker signature, or a refusal when the chain vouches for none.</summary>
    private OperationResult<MeituFileDialogSignature> FileDialogSignature()
    {
        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<MeituFileDialogSignature>(baseline.Failure);
        }

        return baseline.Value.FileDialog is { } signature
            ? OperationResult.Ok(signature)
            : OperationResult.Fail<MeituFileDialogSignature>(
                FailureCode.MeituUnknownState,
                "The verified evidence chain carries no file-picker signature, so PrintFlow has no signed " +
                "description of the window it would type a path into. Nothing was written.");
    }

    private OperationResult<MeituDocumentIdentitySignature> DocumentIdentitySignature()
    {
        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<MeituDocumentIdentitySignature>(baseline.Failure);
        }

        return baseline.Value.DocumentIdentity is { } signature
            ? OperationResult.Ok(signature)
            : OperationResult.Fail<MeituDocumentIdentitySignature>(
                FailureCode.MeituUnknownState,
                "The verified evidence chain carries no Save-dialog document-identity signature. " +
                "No Save control was invoked.");
    }

    /// <inheritdoc />
    public async Task<OperationResult<Unit>> InvokeKnownElementAsync(
        MeituTarget target, KnownMeituElement element, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        OperationResult<MeituTarget> verified = await VerifyTargetAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<Unit>(verified.Failure);
        }

        OperationResult<UiElementRef> found = FindKnownElement(verified.Value, element);
        if (found.IsFailure)
        {
            return OperationResult.Fail<Unit>(found.Failure);
        }

        // Verified again, deliberately: locating an element walks the automation tree, which
        // takes long enough for the foreground to change underneath it.
        OperationResult<MeituTarget> stillOurs = await VerifyTargetAsync(verified.Value, cancellationToken)
            .ConfigureAwait(false);
        return stillOurs.IsFailure
            ? OperationResult.Fail<Unit>(stillOurs.Failure)
            : _elements.Invoke(found.Value);
    }

    /// <inheritdoc />
    public async Task<OperationResult<Unit>> SendVerifiedShortcutAsync(
        MeituTarget target, KnownShortcut shortcut, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        OperationResult<MeituTarget> verified = await VerifyTargetAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<Unit>(verified.Failure);
        }

        // The sink verifies the foreground once more on its own account. That duplication is
        // intentional: the guard belongs to the primitive as well as to this caller, so a future
        // caller that forgets cannot produce an unguarded keystroke.
        return _input.SendShortcut(verified.Value.Window.Handle, shortcut);
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituTarget>> OpenWorkingCopyAsync(
        MeituTarget target, string workingCopyAbsolutePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingCopyAbsolutePath);

        OperationResult<MeituTarget> editor = await ReachEmptyEditorAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (editor.IsFailure)
        {
            return editor;
        }

        OperationResult<Unit> requested = await InvokeKnownElementAsync(
            editor.Value, KnownMeituElement.EditorOpenControl, cancellationToken).ConfigureAwait(false);
        if (requested.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(requested.Failure);
        }

        OperationResult<ExternalWindowRef> dialog = await WaitForFileDialogAsync(editor.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dialog.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(dialog.Failure);
        }

        OperationResult<Unit> filled = FillAndConfirmDialog(
            editor.Value, dialog.Value, workingCopyAbsolutePath);

        return filled.IsFailure
            ? OperationResult.Fail<MeituTarget>(filled.Failure)
            : OperationResult.Ok(editor.Value);
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituLoadObservation>> ObserveLoadedDocumentAsync(
        MeituTarget target, string expectedWorkingCopyFileName, IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(stop);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedWorkingCopyFileName);

        OperationResult<MeituEnhancementSignature> signature = EnhancementSignature();
        if (signature.IsFailure)
        {
            return OperationResult.Fail<MeituLoadObservation>(signature.Failure);
        }

        // The first look decides how long to keep looking, and the asymmetry is the whole design.
        // Meitu can only auto-start an enhancement when its module is already selected, and a
        // selected module is exactly what the signed completion signature matches — so a first
        // reading of Unobserved means there is nothing that could start by itself and the wait
        // is over before it began. Anything else means work may be in flight or about to be, and
        // is worth the bounded read-only watch below.
        OperationResult<MeituStateSnapshot> first = await AwaitReadableStateAsync(
            target, expectedWorkingCopyFileName, cancellationToken).ConfigureAwait(false);
        if (first.IsFailure)
        {
            return OperationResult.Fail<MeituLoadObservation>(first.Failure);
        }

        MeituEnhancementPhase phaseAtOpen =
            MeituEnhancementRule.Classify(signature.Value, first.Value.Observation);
        if (phaseAtOpen == MeituEnhancementPhase.Unobserved)
        {
            return OperationResult.Ok(new MeituLoadObservation(
                phaseAtOpen, AutoStartedEnhancement: false, Busy: null, Completion: null));
        }

        MeituStateSnapshot? busy = phaseAtOpen == MeituEnhancementPhase.Busy ? first.Value : null;
        if (busy is null)
        {
            // The module is selected but nothing is running yet. Meitu was observed live to begin
            // its unrequested enhancement within half a second of the document appearing, so a
            // short bounded watch either catches it or establishes that it is not coming. What
            // this must never do is decide from the panel alone that work happened: the panel
            // outlives the document it belongs to, and was observed still matching on an editor
            // holding nothing at all (§21).
            OperationResult<MeituStateSnapshot?> watched = await WatchForAutoStartAsync(
                target, expectedWorkingCopyFileName, signature.Value, cancellationToken)
                .ConfigureAwait(false);
            if (watched.IsFailure)
            {
                return OperationResult.Fail<MeituLoadObservation>(watched.Failure);
            }

            busy = watched.Value;
        }

        if (busy is null)
        {
            // Stale selected module, and no work of its own. Reported as an ordinary observation
            // rather than a failure: it is the enhancement route's pre-invoke guard that refuses
            // it, and it refuses with the reason that actually applies — invoking the control
            // now would deselect the module rather than start anything.
            return OperationResult.Ok(new MeituLoadObservation(
                phaseAtOpen, AutoStartedEnhancement: false, Busy: null, Completion: first.Value));
        }

        // Busy was positively seen for this load. The work is Meitu's own, but it is provably
        // this load's work, so it is waited out rather than restarted — §20 forbids invoking the
        // module over an operation already in flight.
        OperationResult<MeituStateSnapshot> completion = await AwaitEnhancementPhaseAsync(
            target,
            expectedWorkingCopyFileName,
            observedDocumentIdentity: null,
            signature.Value,
            MeituEnhancementPhase.Complete,
            _options.EnhancementCompletionTimeout,
            stop,
            cancellationToken).ConfigureAwait(false);

        return completion.IsFailure
            ? OperationResult.Fail<MeituLoadObservation>(completion.Failure)
            : OperationResult.Ok(new MeituLoadObservation(
                phaseAtOpen, AutoStartedEnhancement: true, busy, completion.Value));
    }

    /// <summary>
    /// Reads the state, tolerating the short-lived owned window Meitu leaves behind after an open.
    /// </summary>
    /// <remarks>
    /// The picker closing puts a transient owned pop-up in front of the editor, which classifies
    /// as <see cref="MeituStartingState.KnownModal"/> for a fraction of a second. Reading once
    /// and stopping on it would abandon a perfectly ordinary open, so this retries within the
    /// dialog budget — and still stops if the modal is a real one that stays.
    /// </remarks>
    private async Task<OperationResult<MeituStateSnapshot>> AwaitReadableStateAsync(
        MeituTarget target, string expectedWorkingCopyFileName, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<MeituStateSnapshot> snapshot = await InspectStateAsync(
                target, expectedWorkingCopyFileName, cancellationToken).ConfigureAwait(false);
            if (snapshot.IsFailure || snapshot.Value.State != MeituStartingState.KnownModal)
            {
                return snapshot;
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return snapshot;
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Watches read-only for an enhancement Meitu starts by itself, and gives up quietly.
    /// </summary>
    /// <remarks>
    /// A success carrying <c>null</c> is the ordinary "it did not start anything" answer, not a
    /// failure — which is the opposite convention from <see cref="AwaitEnhancementPhaseAsync"/>,
    /// deliberately. That method waits for something PrintFlow asked for and a timeout means the
    /// request went nowhere; this one waits for something nobody asked for, where nothing
    /// happening is the better of the two outcomes.
    /// </remarks>
    private async Task<OperationResult<MeituStateSnapshot?>> WatchForAutoStartAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        MeituEnhancementSignature signature,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.AutoEnhancementWatchTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<MeituStateSnapshot> snapshot = await InspectStateAsync(
                target, expectedWorkingCopyFileName, cancellationToken).ConfigureAwait(false);
            if (snapshot.IsFailure)
            {
                return OperationResult.Fail<MeituStateSnapshot?>(snapshot.Failure);
            }

            if (MeituEnhancementRule.Classify(signature, snapshot.Value.Observation)
                == MeituEnhancementPhase.Busy)
            {
                return OperationResult.Ok<MeituStateSnapshot?>(snapshot.Value);
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Ok<MeituStateSnapshot?>(null);
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituStateSnapshot>> ConfirmWorkingCopyIdentityAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedWorkingCopyFileName);

        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(baseline.Failure);
        }

        OperationResult<MeituDocumentIdentitySignature> signature = DocumentIdentitySignature();
        if (signature.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(signature.Failure);
        }

        // Positively reacquired rather than merely checked: this route is entered straight
        // after Meitu has closed a window of its own — its picker, or a previous Save surface —
        // and the foreground it hands back is briefly not the editor (§18).
        OperationResult<MeituTarget> verified = await ReacquireForegroundAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(verified.Failure);
        }

        OperationResult<MeituStateSnapshot> beforeSave = await InspectStateCoreAsync(
            verified.Value, expectedWorkingCopyFileName, observedDocumentIdentity: null, cancellationToken)
            .ConfigureAwait(false);
        if (beforeSave.IsFailure)
        {
            return beforeSave;
        }

        MeituDocumentSurfacePhase beforePhase =
            MeituDocumentIdentityRule.ClassifyIdentityProbeSurface(baseline.Value, beforeSave.Value.Observation);
        if (beforePhase is MeituDocumentSurfacePhase.Unknown or MeituDocumentSurfacePhase.AmbiguousResult)
        {
            return OperationResult.Fail<MeituStateSnapshot>(OperationFailure.Create(
                FailureCode.MeituUnknownState,
                "The verified window is not one unambiguous signed loaded-editor, Enhancement-result, or " +
                "Background-Removal-result surface, so Save was not invoked as a document-identity probe.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["surfacePhase"] = beforePhase.ToString(),
                    ["savePurpose"] = "document-identity-probe",
                    ["inputSent"] = "false",
                }));
        }

        OperationResult<Unit> requested = await InvokeKnownElementAsync(
            verified.Value, KnownMeituElement.EditorSaveControl, cancellationToken).ConfigureAwait(false);
        if (requested.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(requested.Failure);
        }

        OperationResult<ExternalWindowRef> dialog = await WaitForIdentityDialogAsync(
            verified.Value, signature.Value, cancellationToken).ConfigureAwait(false);
        if (dialog.IsFailure)
        {
            // A Save action that writes immediately rather than presenting the signed surface
            // is unsafe for identity probing. It is never retried in this call.
            return OperationResult.Fail<MeituStateSnapshot>(dialog.Failure);
        }

        OperationResult<string> observed = ReadIdentityValue(
            verified.Value, dialog.Value, signature.Value);

        // Cancel is the only dialog action in this route. It runs even when the value control
        // is absent or ambiguous, because the dialog itself and its cancel control are signed;
        // Save, Save As and every editable field remain untouched.
        OperationResult<Unit> cancelled = await CancelIdentityDialogAsync(
            verified.Value, dialog.Value, signature.Value, cancellationToken).ConfigureAwait(false);
        if (cancelled.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(cancelled.Failure);
        }

        if (observed.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(observed.Failure);
        }

        OperationResult<MeituStateSnapshot> confirmed = await InspectStateCoreAsync(
            verified.Value, expectedWorkingCopyFileName, observed.Value, cancellationToken)
            .ConfigureAwait(false);
        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        if (confirmed.Value.State == MeituStartingState.KnownEditorWithExpectedWorkingCopy)
        {
            MeituDocumentSurfacePhase afterPhase = MeituDocumentIdentityRule.ClassifyIdentityProbeSurface(
                baseline.Value, confirmed.Value.Observation);
            if (afterPhase == beforePhase)
            {
                return confirmed;
            }

            return OperationResult.Fail<MeituStateSnapshot>(OperationFailure.Create(
                FailureCode.MeituUnknownState,
                $"Meitu changed from the signed '{beforePhase}' surface to '{afterPhase}' while the " +
                "document identity was being probed. The Save surface was canceled and no identity is claimed.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["surfacePhaseBefore"] = beforePhase.ToString(),
                    ["surfacePhaseAfter"] = afterPhase.ToString(),
                    ["savePurpose"] = "document-identity-probe",
                    ["inputSent"] = "cancel-only",
                }));
        }

        // Which of several things went wrong, named rather than assumed. The state can miss
        // for reasons that have nothing to do with the value that was read: Meitu can be
        // computing — it starts an enhancement by itself when a document is opened while a
        // module is still selected — or a dialog can have appeared while the probe ran. Reporting
        // all of those as "the name did not match" sends the operator looking for a filename
        // problem that does not exist, which is exactly what the first B2A smoke did.
        return OperationResult.Fail<MeituStateSnapshot>(OperationFailure.Create(
            confirmed.Value.State == MeituStartingState.KnownModal
                ? FailureCode.MeituBlockingDialog
                : FailureCode.MeituUnknownState,
            confirmed.Value.State switch
            {
                MeituStartingState.Busy =>
                    "Meitu is computing, so the document it is holding cannot be confirmed as settled. " +
                    "The Save surface was canceled and nothing further was invoked.",
                MeituStartingState.KnownModal =>
                    "A dialog owned by Meitu appeared while the document identity was being read. " +
                    "PrintFlow does not dismiss it; the Save surface was canceled and no identity is claimed.",
                _ =>
                    $"The signed Save value did not exactly identify '{expectedWorkingCopyFileName}'. The " +
                    "dialog was canceled and no document identity is claimed.",
            },
            isRetryable: confirmed.Value.State is MeituStartingState.Busy or MeituStartingState.KnownModal,
            context: new Dictionary<string, string>
            {
                ["state"] = confirmed.Value.State.ToString(),
                ["expectedFile"] = expectedWorkingCopyFileName,
                ["observedIdentity"] = observed.Value,
                ["inputSent"] = "cancel-only",
            }));
    }

    private OperationResult<string> ReadIdentityValue(
        MeituTarget target,
        ExternalWindowRef dialog,
        MeituDocumentIdentitySignature signature)
    {
        OperationResult<ExternalWindowRef> verified = VerifyIdentityDialog(target, dialog.Handle, signature);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<string>(verified.Failure);
        }

        OperationResult<UiElementRef> field = FindSignedControl(
            target, dialog.Handle, signature.FileNameControl);
        if (field.IsFailure)
        {
            return OperationResult.Fail<string>(field.Failure);
        }

        return _elements.GetValue(field.Value);
    }

    private async Task<OperationResult<Unit>> CancelIdentityDialogAsync(
        MeituTarget target,
        ExternalWindowRef dialog,
        MeituDocumentIdentitySignature signature,
        CancellationToken cancellationToken)
    {
        OperationResult<ExternalWindowRef> verified = VerifyIdentityDialog(target, dialog.Handle, signature);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<Unit>(verified.Failure);
        }

        OperationResult<UiElementRef> cancel = FindSignedControl(
            target, dialog.Handle, signature.CancelControl);
        if (cancel.IsFailure)
        {
            return OperationResult.Fail<Unit>(cancel.Failure);
        }

        // Re-check after the tree walk and immediately before the one permitted action.
        verified = VerifyIdentityDialog(target, dialog.Handle, signature);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<Unit>(verified.Failure);
        }

        OperationResult<Unit> invoked = _elements.Invoke(cancel.Value);
        if (invoked.IsFailure)
        {
            return invoked;
        }

        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_locator.Refresh(dialog.Handle).IsFailure)
            {
                return OperationResult.Ok();
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.MeituOpenInputFailed,
                    "The signed Cancel control was invoked, but the Save surface did not close within the " +
                    "bounded timeout. No Save or Save As control was invoked.");
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets from wherever Meitu is to its empty editor, and returns that window as a verified
    /// target (Epic 11300 Part B1 §7, §11).
    /// </summary>
    /// <remarks>
    /// Meitu 7.8.7.5 does not raise a file dialog from the start page, which is what Part A
    /// expected. Invoking the 图片编辑 card opens a <i>second top-level window</i> — the editor —
    /// in its empty state, and the picker comes from a control on that window. The sequence
    /// therefore has a step Part A had no reason to model, and the window PrintFlow ends up
    /// interacting with is not the one it started from.
    ///
    /// Which window is the editor is decided by classifying candidates, not by matching a title
    /// here. That keeps one definition of "this is the empty editor" — the signed evidence the
    /// classifier reads — rather than a second one living in the open path that could drift from
    /// it.
    /// </remarks>
    private async Task<OperationResult<MeituTarget>> ReachEmptyEditorAsync(
        MeituTarget target, CancellationToken cancellationToken)
    {
        OperationResult<MeituTarget> verified = await VerifyTargetAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (verified.IsFailure)
        {
            return verified;
        }

        OperationResult<MeituStateSnapshot> state = await InspectStateAsync(
            verified.Value, expectedWorkingCopyFileName: null, cancellationToken).ConfigureAwait(false);
        if (state.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(state.Failure);
        }

        // Already there: an operator who left Meitu on the empty editor does not need the start
        // page driven, and driving it anyway would open a second editor window.
        if (state.Value.State == MeituStartingState.KnownEditorEmpty)
        {
            return verified;
        }

        if (state.Value.State != MeituStartingState.KnownWelcome)
        {
            return OperationResult.Fail<MeituTarget>(
                FailureCode.MeituUnknownState,
                $"Meitu is on '{state.Value.State}', which is neither the signed start page nor the signed " +
                "empty editor, so PrintFlow has no evidence-backed way to reach the picker from here. " +
                "Nothing was invoked.");
        }

        OperationResult<Unit> card = await RequestOpenDialogAsync(verified.Value, cancellationToken)
            .ConfigureAwait(false);
        if (card.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(card.Failure);
        }

        return await WaitForEmptyEditorWindowAsync(verified.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a window of the verified process that classifies as the signed empty editor.
    /// </summary>
    private async Task<OperationResult<MeituTarget>> WaitForEmptyEditorWindowAsync(
        MeituTarget target, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<IReadOnlyList<ExternalWindowRef>> windows =
                _locator.FindTopLevelWindows(target.Process);
            if (windows.IsFailure)
            {
                return OperationResult.Fail<MeituTarget>(windows.Failure);
            }

            foreach (ExternalWindowRef window in windows.Value)
            {
                if (window.OwningProcessId != target.Process.ProcessId)
                {
                    continue;
                }

                MeituTarget candidate = target with { Window = window };
                OperationResult<MeituStateSnapshot> state = await InspectStateAsync(
                    candidate, expectedWorkingCopyFileName: null, cancellationToken).ConfigureAwait(false);

                if (state.IsSuccess && state.Value.State == MeituStartingState.KnownEditorEmpty)
                {
                    // Activated rather than assumed to be in front: it is a new window, and the
                    // input that follows requires the foreground, which is checked again there.
                    return await ActivateAsync(candidate, cancellationToken).ConfigureAwait(false);
                }
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<MeituTarget>(
                    FailureCode.MeituOpenInputFailed,
                    $"No window of Meitu process {target.Process.ProcessId} reached the signed empty-editor " +
                    $"state within {_options.DialogTimeout.TotalSeconds:0} s; nothing further was invoked " +
                    "and nothing was typed.");
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// The refusal for a Stop that arrived before any operation input was produced
    /// (Epic 11300 Part D2A §5), or <c>null</c> when no stop is pending.
    /// </summary>
    /// <remarks>
    /// Both modes produce the same answer here, and that is correct rather than a shortcut:
    /// before the operation has been invoked there is nothing to cancel and nothing to take
    /// over, so "stop safely" and "leave Meitu alone" describe the same action — do not start.
    /// The two are still distinguished in the audit, which is written from the mode by the
    /// workflow layer rather than from this failure.
    /// </remarks>
    private static OperationFailure? StopBeforeOperation(IAutomationStopSignal stop)
    {
        if (stop.RequestedMode is not { } mode)
        {
            return null;
        }

        return OperationFailure.Create(
            FailureCode.Cancelled,
            $"The operator requested '{mode}' before any Meitu operation was invoked. PrintFlow " +
            "stopped its own orchestration; no operation input was produced, nothing was exported " +
            "and Meitu retains nothing from this attempt.",
            isRetryable: true,
            context: new Dictionary<string, string>
            {
                ["stopMode"] = mode.ToString(),
                ["phase"] = ExternalOperationPhase.NotStarted.ToString(),
                ["inputSent"] = "false",
                ["meituCancelInvoked"] = "false",
                ["forceTerminationInvoked"] = "false",
            });
    }

    /// <summary>
    /// Honours a Stop that arrived while <paramref name="operation"/> is positively Busy, and
    /// returns the failure that ends the run — or <c>null</c> when no stop is pending
    /// (Epic 11300 Part D2A §9, §10, §19).
    /// </summary>
    /// <remarks>
    /// The whole of §9 and §10 in one place, and the branch structure is the specification:
    /// <list type="bullet">
    ///   <item>a takeover produces <b>no input whatsoever</b>, from Busy as from anywhere else.
    ///   It does not click the cancel first, because "take over" does not mean "cancel then
    ///   take over" (§19);</item>
    ///   <item>a stop tries the exact signed cancel <b>once</b>, and only if
    ///   <see cref="AutomationStopPolicy"/> permits it for the reported phase;</item>
    ///   <item>a cancel that cannot be proven — missing, ambiguous, disabled, wrong process,
    ///   target lost, blocked by a modal — is <b>not</b> escalated. PrintFlow stops its own
    ///   orchestration and reports that the operation may still be running (§10).</item>
    /// </list>
    /// A cancel that fails to resolve therefore looks, from the caller's side, exactly like a
    /// stop with no cancel available: same failure code, same "no Revision", different audit.
    /// That is deliberate — the difference matters to the operator, not to the control flow.
    /// </remarks>
    private async Task<OperationFailure?> StopDuringBusyAsync(
        IAutomationStopSignal stop,
        MeituTarget target,
        MeituOperation operation,
        string? expectedWorkingCopyFileName,
        CancellationToken cancellationToken)
    {
        if (stop.RequestedMode is not { } mode)
        {
            return null;
        }

        AutomationStopResolution resolution = AutomationStopPolicy.Resolve(mode, stop.Phase);
        if (!resolution.MayProduceAnyInput || !resolution.MayInvokeOperationCancel)
        {
            return OperationFailure.Create(
                FailureCode.Cancelled,
                mode == AutomationStopMode.TakeOver
                    ? "The operator took over Meitu while the operation was running. PrintFlow produced no " +
                      "further input of any kind: nothing was cancelled, dismissed or closed, and Meitu was " +
                      "left exactly as it was. The operation may still be running and the operator owns it."
                    : "The operator stopped this operation, and the current phase does not permit PrintFlow " +
                      "to invoke Meitu's cancel. Orchestration stopped with no further input; the operation " +
                      "may still be running.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["stopMode"] = mode.ToString(),
                    ["phase"] = stop.Phase.ToString(),
                    ["inputSent"] = "false",
                    ["meituCancelInvoked"] = "false",
                    ["forceTerminationInvoked"] = "false",
                });
        }

        OperationResult<MeituCancelOutcome> cancelled = await CancelRunningOperationAsync(
            target, operation, expectedWorkingCopyFileName, cancellationToken).ConfigureAwait(false);

        if (cancelled.IsFailure)
        {
            // §10. Nothing was invoked and nothing is guessed at. The operator is told plainly
            // that Meitu may still be working, with the structural reason preserved.
            Dictionary<string, string> context = new(cancelled.Failure.Context)
            {
                ["stopMode"] = mode.ToString(),
                ["phase"] = stop.Phase.ToString(),
                ["meituCancelInvoked"] = "false",
                ["forceTerminationInvoked"] = "false",
                ["operatorActionRequired"] = "true",
            };

            return OperationFailure.Create(
                FailureCode.Cancelled,
                "The operator stopped this operation, but PrintFlow could not prove Meitu's cancel " +
                $"control and therefore invoked nothing. {cancelled.Failure.TechnicalDetail} The external " +
                "operation may still be running and operator action may be required.",
                isRetryable: true,
                context: context);
        }

        stop.ReportOperationCancelOutcome(cancelled.Value.LeftBusy);

        return OperationFailure.Create(
            FailureCode.Cancelled,
            "The operator stopped this operation. PrintFlow invoked the signed Meitu cancel control " +
            $"exactly once; Meitu {(cancelled.Value.LeftBusy ? "positively left its busy state" : "had not left its busy state when PrintFlow stopped observing")}. " +
            $"The screen was last seen as '{cancelled.Value.StateAfterCancel.State}'. Nothing was exported " +
            "and no Revision was created.",
            isRetryable: true,
            context: new Dictionary<string, string>
            {
                ["stopMode"] = mode.ToString(),
                ["phase"] = stop.Phase.ToString(),
                ["meituCancelInvoked"] = "true",
                ["meituLeftBusy"] = cancelled.Value.LeftBusy ? "true" : "false",
                ["stateAfterCancel"] = cancelled.Value.StateAfterCancel.State.ToString(),
                ["exported"] = "false",
                ["revisionCreated"] = "false",
                ["forceTerminationInvoked"] = "false",
            });
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituCancelOutcome>> CancelRunningOperationAsync(
        MeituTarget target,
        MeituOperation operation,
        string? expectedWorkingCopyFileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<MeituCancelOutcome>(baseline.Failure);
        }

        // 1. Read the screen first, and read it read-only. Everything that follows rests on
        //    this one observation: the operation correlation, the control resolution, and the
        //    "was it Busy before" half of the outcome. Taking it once means the cancel cannot be
        //    authorised by one reading of the screen and aimed by a different one.
        //
        //    It is the fast signed-marker read rather than the full Qt-tree walk, and that is a
        //    correctness requirement rather than an optimisation. A live D2A Stop against 抠图
        //    refused with "current-load Busy correlation absent" while the cutout was genuinely
        //    running: the full walk takes longer than the cutout's whole Busy window, so by the
        //    time it returned the operation had finished. The refusal was correct — PrintFlow
        //    could not establish what was running — but it made Stop unusable for the shorter of
        //    the two operations. Part C1 met the same problem observing Busy and solved it the
        //    same way.
        OperationResult<MeituStateSnapshot> busy = await ReadOperationPhaseSnapshotAsync(
            target, baseline.Value, operation, expectedWorkingCopyFileName, cancellationToken)
            .ConfigureAwait(false);
        if (busy.IsFailure)
        {
            return OperationResult.Fail<MeituCancelOutcome>(busy.Failure);
        }

        // 2. A Meitu-owned modal over a running operation is §10's "state became Unknown" and
        //    §20's takeover scenario. PrintFlow does not read it, dismiss it, or click past it
        //    to reach a cancel that may be underneath it.
        if (busy.Value.State == MeituStartingState.KnownModal)
        {
            return OperationResult.Fail<MeituCancelOutcome>(OperationFailure.Create(
                FailureCode.MeituBlockingDialog,
                "A dialog owned by Meitu is in front of the running operation. PrintFlow does not " +
                "dismiss dialogs it cannot identify, so no cancel was invoked. The external operation " +
                "may still be running and the operator must resolve it.",
                isRetryable: false,
                context: CancelContext(operation, busy.Value, "blocking dialog in front of the operation")));
        }

        // 3. Product eligibility before any tree walk (§7). Refusing here means the walk that
        //    would have found a control never happens, so a successful resolution can never be
        //    mistaken for evidence that this operation was the one running.
        OperationResult<Unit> eligible = MeituBusyCancelRule.Eligible(
            baseline.Value, operation, busy.Value.Observation);
        if (eligible.IsFailure)
        {
            return OperationResult.Fail<MeituCancelOutcome>(eligible.Failure);
        }

        // 4. Foreground, process and window, immediately before the input — the same guard every
        //    other input path in this class takes, and for the same reason.
        OperationResult<MeituTarget> verified = await VerifyTargetAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<MeituCancelOutcome>(verified.Failure);
        }

        OperationResult<UiElementRef> control = FindBusyCancelControl(
            verified.Value, baseline.Value.BusyCancel!);
        if (control.IsFailure)
        {
            return OperationResult.Fail<MeituCancelOutcome>(control.Failure);
        }

        // 5. Verified once more, deliberately: resolving the control walked the automation tree,
        //    which takes long enough for the foreground to change underneath it.
        OperationResult<MeituTarget> stillOurs = await VerifyTargetAsync(verified.Value, cancellationToken)
            .ConfigureAwait(false);
        if (stillOurs.IsFailure)
        {
            return OperationResult.Fail<MeituCancelOutcome>(stillOurs.Failure);
        }

        OperationResult<Unit> invoked = _elements.Invoke(control.Value);
        if (invoked.IsFailure)
        {
            return OperationResult.Fail<MeituCancelOutcome>(invoked.Failure);
        }

        // 6. One invocation has happened. From here nothing may produce further input — not on
        //    timeout, not on an unreadable screen, and not on cancellation of the token. The
        //    only remaining job is to find out what Meitu did (§9, §11).
        return await ObserveAfterCancelAsync(
            stillOurs.Value, baseline.Value, operation, busy.Value, expectedWorkingCopyFileName,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Watches, read-only, for the operation to stop matching its signed Busy signature after
    /// the single cancel invocation (Epic 11300 Part D2A §11).
    /// </summary>
    /// <remarks>
    /// Reports rather than judges. Leaving Busy is recorded as a fact and the resulting screen
    /// is recorded as whatever it classifies as — including <c>Unknown</c>, which this slice
    /// deliberately does not treat as a failure here. §11 forbids assuming a cancelled operation
    /// returns to <c>KnownEditorWithExpectedWorkingCopy</c>, and the honest consequence is that
    /// an unrecognised post-cancel screen is a state to report to the operator, not an error to
    /// raise about a cancel that may well have worked.
    /// <para>
    /// The token is observed but never allowed to produce input. A cancelled token stops the
    /// watching and yields the last state read, because the alternative — throwing — would lose
    /// the record that a cancel was invoked at all, and that record is what the audit needs
    /// most (§29).
    /// </para>
    /// </remarks>
    private async Task<OperationResult<MeituCancelOutcome>> ObserveAfterCancelAsync(
        MeituTarget target,
        MeituBaseline baseline,
        MeituOperation operation,
        MeituStateSnapshot busyBefore,
        string? expectedWorkingCopyFileName,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.CancelSettleTimeout;
        MeituStateSnapshot latest = busyBefore;

        while (true)
        {
            // The same fast signed-marker read the correlation used, for the same reason: the
            // question is whether *this* operation's Busy has stopped matching, and a full walk
            // is both slower than the window being watched and no more informative about it.
            OperationResult<MeituStateSnapshot> snapshot = await ReadOperationPhaseSnapshotAsync(
                target, baseline, operation, expectedWorkingCopyFileName, CancellationToken.None)
                .ConfigureAwait(false);

            if (snapshot.IsSuccess)
            {
                latest = snapshot.Value;
                if (!StillBusy(baseline, operation, latest.Observation))
                {
                    return OperationResult.Ok(new MeituCancelOutcome(
                        operation, busyBefore, LeftBusy: true, latest));
                }
            }

            if (cancellationToken.IsCancellationRequested || _clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Ok(new MeituCancelOutcome(
                    operation, busyBefore, LeftBusy: false, latest));
            }

            await Task.Delay(_options.PollInterval, _clock, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the screen through <b>the same mechanism the named operation's own observation
    /// loop uses</b>, so the cancel correlates on the terms its markers were signed for
    /// (Epic 11300 Part D2A §7).
    /// </summary>
    /// <remarks>
    /// The per-operation split is a correctness requirement, discovered the hard way during the
    /// live D2A Stops, and it is worth stating plainly because it looks like duplication:
    /// <list type="bullet">
    ///   <item><b>Background Removal</b> is observed by Part C1's fast exact-name marker query,
    ///   because its Busy window can be under two seconds and a managed walk of Meitu's whole Qt
    ///   tree does not reliably return inside it. Its signed markers — 智能识别中, 返回结果中,
    ///   图片合成中, 取消 — are the exact automation names Meitu reports.</item>
    ///   <item><b>Enhancement</b> is observed by the full read plus
    ///   <c>MeituEnhancementRule</c>'s substring matching, because its signed markers are
    ///   <i>fragments</i>: Meitu's actual automation names are 变清晰中，请稍候… and the longer
    ///   变清晰时长… sentence, and the evidence records the stable stems rather than the
    ///   full strings. An exact-name query matches neither, so a fast read of the enhancement
    ///   markers finds only 取消 — one marker where the signature requires two — and reports
    ///   "not busy" about an operation that is plainly running.</item>
    /// </list>
    /// A first attempt used the fast read for both. It made 抠图 stoppable and quietly made
    /// 变清晰 unstoppable: every live Stop refused with "current-load Busy correlation absent"
    /// while the enhancement was visibly in flight. The refusals were safe — nothing was
    /// invoked — but they were refusals about PrintFlow's own reading rather than about Meitu.
    /// <para>
    /// D2A does not change either observation loop; §2 preserves the Enhancement and Background
    /// Removal success boundaries, and how each detects Busy is part of them. What this method
    /// does is <i>match</i> them, so the cancel asks the same question the loop just answered.
    /// </para>
    /// </remarks>
    private async Task<OperationResult<MeituStateSnapshot>> ReadOperationPhaseSnapshotAsync(
        MeituTarget target,
        MeituBaseline baseline,
        MeituOperation operation,
        string? expectedWorkingCopyFileName,
        CancellationToken cancellationToken)
    {
        if (operation == MeituOperation.RemoveBackground)
        {
            if (baseline.BackgroundRemoval is not { } removal)
            {
                return OperationResult.Fail<MeituStateSnapshot>(
                    FailureCode.EnvironmentNotVerified,
                    "The verified preset carries no Background Removal signature, so PrintFlow cannot " +
                    "establish that it is running. Nothing was cancelled and no input was sent.");
            }

            return ReadBackgroundRemovalPhaseSnapshot(
                target, expectedWorkingCopyFileName ?? string.Empty, string.Empty, removal);
        }

        if (baseline.Enhancement is null)
        {
            return OperationResult.Fail<MeituStateSnapshot>(
                FailureCode.EnvironmentNotVerified,
                "The verified preset carries no Enhancement signature, so PrintFlow cannot establish " +
                "that it is running. Nothing was cancelled and no input was sent.");
        }

        return await InspectStateCoreAsync(
            target, expectedWorkingCopyFileName, observedDocumentIdentity: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Whether the operation's own signed Busy signature still matches.</summary>
    private static bool StillBusy(
        MeituBaseline baseline, MeituOperation operation, MeituObservation observation) =>
        operation == MeituOperation.Enhance
            ? baseline.Enhancement is { } enhancement && MeituEnhancementRule.IsBusy(enhancement, observation)
            : baseline.BackgroundRemoval is { } removal &&
              MeituBackgroundRemovalRule.Classify(removal, observation) == MeituBackgroundRemovalPhase.Busy;

    /// <summary>
    /// Resolves the one element that may be invoked to cancel, or refuses
    /// (Epic 11300 Part D2A §8).
    /// </summary>
    /// <remarks>
    /// The query is by exact name beneath the one verified window, which is where a name-based
    /// approach would <i>stop</i>; here it is only how candidates are gathered. Every candidate
    /// then goes to <see cref="MeituBusyCancelRule"/> with its walked ancestry, and the rule
    /// refuses unless exactly one survives the full signed structure. The live decoy this
    /// separation exists for is the open picker's own Cancel, whose automation name is exactly
    /// 取消.
    /// </remarks>
    private OperationResult<UiElementRef> FindBusyCancelControl(
        MeituTarget target, MeituBusyCancelSignature signature)
    {
        OperationResult<IReadOnlyList<UiElementRef>> found = _elements.FindAll(
            target.Window.Handle, new UiElementQuery(UiControlKind.Any, Name: signature.Control.Name));
        if (found.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(found.Failure);
        }

        List<UiElementRef> elements = [];
        List<MeituBusyCancelCandidate> candidates = [];

        foreach (UiElementRef element in found.Value)
        {
            OperationResult<UiElementIdentity> identity = _elements.Describe(element);
            if (identity.IsFailure)
            {
                continue;
            }

            ImmutableArray<string>.Builder ancestry = ImmutableArray.CreateBuilder<string>();
            UiElementRef current = element;
            for (int level = 0; level < signature.RequiredAncestorClassNames.Length; level++)
            {
                OperationResult<UiElementRef> parent = _elements.GetParent(current);
                if (parent.IsFailure)
                {
                    break;
                }

                current = parent.Value;
                OperationResult<UiElementIdentity> described = _elements.Describe(current);
                if (described.IsFailure)
                {
                    break;
                }

                ancestry.Add(described.Value.ClassName);
            }

            elements.Add(element);
            candidates.Add(new MeituBusyCancelCandidate(identity.Value, ancestry.ToImmutable()));
        }

        OperationResult<int> chosen = MeituBusyCancelRule.SelectCancelControl(
            signature, target.Process.ProcessId, candidates);

        return chosen.IsFailure
            ? OperationResult.Fail<UiElementRef>(chosen.Failure)
            : OperationResult.Ok(elements[chosen.Value]);
    }

    private static Dictionary<string, string> CancelContext(
        MeituOperation operation, MeituStateSnapshot snapshot, string why) => new()
    {
        ["operation"] = operation.ToString(),
        ["reason"] = why,
        ["state"] = snapshot.State.ToString(),
        ["inputSent"] = "false",
        ["meituCancelInvoked"] = "false",
        ["forceTerminationInvoked"] = "false",
    };

    /// <inheritdoc />
    public OperationResult<EvidenceRef> CaptureEvidence(MeituTarget target, string reason)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Captured from the last known-good description rather than a fresh read: evidence is
        // most valuable exactly when the window has become unreadable, and a capture attempt
        // must never replace the failure it is documenting.
        return _evidence.CaptureWindow(target.Window, reason);
    }

    /// <summary>
    /// Re-establishes, from the OS and not from cache, that the target is still the Meitu
    /// PrintFlow verified and still holds the foreground.
    /// </summary>
    /// <remarks>
    /// This is the guard the whole class is built around. It runs before every input-producing
    /// operation, and its failure is always <see cref="FailureCode.MeituTargetLost"/> — the code
    /// an operator can read as "PrintFlow declined to type into something else".
    /// </remarks>
    private async Task<OperationResult<MeituTarget>> VerifyTargetAsync(
        MeituTarget target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        OperationResult<ExternalWindowRef> window = RefreshOwnedWindow(target);
        if (window.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(window.Failure);
        }

        OperationResult<ForegroundIdentity> foreground = _locator.ReadForeground();
        if (foreground.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(foreground.Failure);
        }

        if (foreground.Value.Handle != window.Value.Handle ||
            foreground.Value.ProcessId != target.Process.ProcessId)
        {
            return OperationResult.Fail<MeituTarget>(TargetLost(
                window.Value.Handle, foreground.Value,
                "The verified Meitu window is not the foreground window; no input was sent."));
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return OperationResult.Ok(target with { Window = window.Value });
    }

    /// <summary>Re-reads the window and re-confirms it still belongs to the expected process.</summary>
    private OperationResult<ExternalWindowRef> RefreshOwnedWindow(MeituTarget target)
    {
        if (!_locator.IsAlive(target.Process))
        {
            return OperationResult.Fail<ExternalWindowRef>(OperationFailure.Create(
                FailureCode.MeituTargetLost,
                $"Meitu process {target.Process.ProcessId} exited after PrintFlow verified it. " +
                "No further input was produced.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["targetLoss"] = "process-exited",
                    ["processId"] = target.Process.ProcessId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["inputSent"] = "false",
                    ["retainedExternalState"] = "gone",
                },
                messageKey: "Failure_MeituClosed"));
        }

        OperationResult<ExternalWindowRef> refreshed = _locator.Refresh(target.Window.Handle);
        if (refreshed.IsFailure)
        {
            return OperationResult.Fail<ExternalWindowRef>(OperationFailure.Create(
                FailureCode.MeituTargetLost,
                $"The verified Meitu window {target.Window.Handle} disappeared. No further input " +
                "was produced.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["targetLoss"] = "window-disappeared",
                    ["windowHandle"] = target.Window.Handle.ToString(),
                    ["inputSent"] = "false",
                    ["retainedExternalState"] = "unknown",
                }));
        }

        if (refreshed.Value.OwningProcessId != target.Process.ProcessId)
        {
            // A handle can be reused by an entirely different application after its original
            // window is destroyed. Re-checking ownership is what stops PrintFlow addressing the
            // successor as though it were Meitu.
            return OperationResult.Fail<ExternalWindowRef>(
                OperationFailure.Create(
                    FailureCode.MeituTargetLost,
                    $"Window {target.Window.Handle} now belongs to process " +
                    $"{refreshed.Value.OwningProcessId}, not the verified Meitu process " +
                    $"{target.Process.ProcessId}.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["targetLoss"] = "handle-reused",
                        ["expectedProcessId"] = target.Process.ProcessId.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ["actualProcessId"] = refreshed.Value.OwningProcessId.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ["inputSent"] = "false",
                    }));
        }

        return refreshed;
    }

    /// <summary>Asks Meitu to show its picker by invoking the structurally resolved card.</summary>
    /// <remarks>
    /// One route, and no fallback. Part A tried the named element first and dropped through to a
    /// verified <c>Ctrl+O</c> when the lookup failed, which was reasonable while "the lookup
    /// failed" meant only "no element of that name is here". Part B1 changed what that failure
    /// means: it is now the structural rule refusing — no card matched the signed shape, or two
    /// did, or one belonged to the wrong process — and §5 requires those to end with no input at
    /// all. A keystroke sent immediately after a refusal would be exactly the "try something
    /// else" behaviour the refusal exists to prevent, so the fallback is gone.
    ///
    /// <see cref="SendVerifiedShortcutAsync"/> remains on the seam, guarded as before; nothing
    /// in the open path calls it.
    /// </remarks>
    private Task<OperationResult<Unit>> RequestOpenDialogAsync(
        MeituTarget target, CancellationToken cancellationToken) =>
        InvokeKnownElementAsync(target, KnownMeituElement.WelcomeOpenEntry, cancellationToken);

    /// <summary>
    /// Waits for a file dialog that belongs to the verified Meitu process and carries the
    /// Windows common-dialog class.
    /// </summary>
    private async Task<OperationResult<ExternalWindowRef>> WaitForFileDialogAsync(
        MeituTarget target, CancellationToken cancellationToken)
    {
        OperationResult<MeituFileDialogSignature> signature = FileDialogSignature();
        if (signature.IsFailure)
        {
            return OperationResult.Fail<ExternalWindowRef>(signature.Failure);
        }

        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<IReadOnlyList<ExternalWindowRef>> windows =
                _locator.FindTopLevelWindows(target.Process);
            if (windows.IsFailure)
            {
                return OperationResult.Fail<ExternalWindowRef>(windows.Failure);
            }

            foreach (ExternalWindowRef candidate in windows.Value)
            {
                // Both conditions, always: the picker must carry the signed window class *and*
                // be owned by the exact Meitu process. A window that merely looks right, in some
                // other process, is not touched (§17, §21).
                if (candidate.OwningProcessId == target.Process.ProcessId &&
                    string.Equals(
                        candidate.ClassName, signature.Value.WindowClassName, StringComparison.Ordinal))
                {
                    return OperationResult.Ok(candidate);
                }
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<ExternalWindowRef>(
                    FailureCode.MeituOpenInputFailed,
                    $"No '{signature.Value.WindowClassName}' picker owned by Meitu process " +
                    $"{target.Process.ProcessId} appeared within {_options.DialogTimeout.TotalSeconds:0} s; " +
                    "nothing was typed.");
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<OperationResult<ExternalWindowRef>> WaitForIdentityDialogAsync(
        MeituTarget target,
        MeituDocumentIdentitySignature signature,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<IReadOnlyList<ExternalWindowRef>> dialogs =
                _locator.FindOwnedDialogs(target.Process, target.Window);
            if (dialogs.IsFailure)
            {
                return OperationResult.Fail<ExternalWindowRef>(dialogs.Failure);
            }

            ExternalWindowRef[] matches = [.. dialogs.Value.Where(candidate =>
                candidate.OwningProcessId == target.Process.ProcessId &&
                string.Equals(candidate.Title, signature.DialogTitle, StringComparison.Ordinal) &&
                string.Equals(candidate.ClassName, signature.DialogClassName, StringComparison.Ordinal))];

            if (matches.Length == 1)
            {
                return OperationResult.Ok(matches[0]);
            }

            if (matches.Length > 1)
            {
                return OperationResult.Fail<ExternalWindowRef>(
                    FailureCode.MeituUnknownState,
                    $"{matches.Length} owned Save surfaces match the signed identity; PrintFlow will not " +
                    "choose between them and no dialog control was used.");
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<ExternalWindowRef>(
                    FailureCode.MeituOpenInputFailed,
                    $"The structurally verified Save control did not present the signed owned surface " +
                    $"'{signature.DialogTitle}'/{signature.DialogClassName} within " +
                    $"{_options.DialogTimeout.TotalSeconds:0} s. The action is not retried; this route cannot " +
                    "be trusted for identity probing if it writes without prompting.");
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private OperationResult<ExternalWindowRef> VerifyIdentityDialog(
        MeituTarget target, WindowHandle dialog, MeituDocumentIdentitySignature signature)
    {
        if (!_locator.IsAlive(target.Process))
        {
            return OperationResult.Fail<ExternalWindowRef>(
                FailureCode.MeituTargetLost,
                $"Meitu process {target.Process.ProcessId} exited while its Save surface was open; no " +
                "dialog control was used.");
        }

        OperationResult<ExternalWindowRef> refreshed = _locator.Refresh(dialog);
        if (refreshed.IsFailure ||
            refreshed.Value.OwningProcessId != target.Process.ProcessId ||
            !string.Equals(refreshed.Value.Title, signature.DialogTitle, StringComparison.Ordinal) ||
            !string.Equals(refreshed.Value.ClassName, signature.DialogClassName, StringComparison.Ordinal))
        {
            return OperationResult.Fail<ExternalWindowRef>(
                FailureCode.MeituTargetLost,
                "The Save surface no longer has the signed title, class and verified Meitu owner; no dialog " +
                "control was used.");
        }

        OperationResult<ForegroundIdentity> foreground = _locator.ReadForeground();
        if (foreground.IsFailure)
        {
            return OperationResult.Fail<ExternalWindowRef>(foreground.Failure);
        }

        return foreground.Value.Handle == dialog && foreground.Value.ProcessId == target.Process.ProcessId
            ? refreshed
            : OperationResult.Fail<ExternalWindowRef>(TargetLost(
                dialog, foreground.Value,
                "The signed Save surface is not the exact foreground window; no dialog control was used."));
    }

    /// <summary>
    /// Re-establishes that the picker is still Meitu's and still in front, before input.
    /// </summary>
    /// <remarks>
    /// The same "verify, then act" ordering as <see cref="VerifyTargetAsync"/>, but the thing
    /// being verified is different and has to be. While the picker is up it — not the editor —
    /// holds the foreground, so requiring the editor window to be foreground would refuse every
    /// legitimate open. What is required instead is that the picker still exists, still belongs
    /// to the verified Meitu process, and that the foreground still belongs to that same
    /// process.
    ///
    /// The process-level foreground check is not about where the input would land: a value
    /// written through a pattern to a named element inside an identified window cannot be
    /// redirected by focus the way a keystroke can. It is about not acting on a machine the
    /// operator has moved on from. If Explorer comes to the front mid-sequence, PrintFlow stops
    /// rather than press Open in a dialog nobody is looking at (§12, §21, §25).
    /// </remarks>
    private OperationResult<ExternalWindowRef> VerifyDialog(MeituTarget target, WindowHandle dialog)
    {
        OperationResult<ExternalWindowRef> refreshed = _locator.Refresh(dialog);
        if (refreshed.IsFailure)
        {
            return OperationResult.Fail<ExternalWindowRef>(
                FailureCode.MeituTargetLost,
                $"The picker {dialog} is no longer available; nothing further was written or invoked.");
        }

        if (refreshed.Value.OwningProcessId != target.Process.ProcessId)
        {
            return OperationResult.Fail<ExternalWindowRef>(
                FailureCode.MeituTargetLost,
                $"The picker {dialog} belongs to process {refreshed.Value.OwningProcessId}, not the verified " +
                $"Meitu process {target.Process.ProcessId}; nothing was written or invoked.");
        }

        OperationResult<ForegroundIdentity> foreground = _locator.ReadForeground();
        if (foreground.IsFailure)
        {
            return OperationResult.Fail<ExternalWindowRef>(foreground.Failure);
        }

        return foreground.Value.ProcessId == target.Process.ProcessId
            ? refreshed
            : OperationResult.Fail<ExternalWindowRef>(TargetLost(
                dialog, foreground.Value,
                "The foreground left the verified Meitu process while its picker was being filled."));
    }

    /// <summary>Writes the path into the verified dialog's field and confirms it.</summary>
    private OperationResult<Unit> FillAndConfirmDialog(
        MeituTarget target, ExternalWindowRef dialog, string workingCopyAbsolutePath)
    {
        OperationResult<ExternalWindowRef> beforeWrite = VerifyDialog(target, dialog.Handle);
        if (beforeWrite.IsFailure)
        {
            return OperationResult.Fail<Unit>(beforeWrite.Failure);
        }

        OperationResult<UiElementRef> field = FindDialogControl(
            target, dialog.Handle, KnownMeituElement.FileDialogFileName);
        if (field.IsFailure)
        {
            return OperationResult.Fail<Unit>(field.Failure);
        }

        // The value pattern, not keystrokes: the path is delivered to a control already shown to
        // live inside a dialog owned by the verified Meitu process, so a focus change part-way
        // through cannot redirect a single character of it elsewhere (§17, §19).
        OperationResult<Unit> written = _elements.SetValue(field.Value, workingCopyAbsolutePath);
        if (written.IsFailure)
        {
            return written;
        }

        // Read the field back before pressing anything. A value pattern that reports success
        // without the text landing is not hypothetical in shell dialogs, and Open acts on
        // whatever the dialog currently has selected — which, if the write was lost, is a file
        // the operator last touched rather than the one PrintFlow prepared (§12, §13).
        OperationResult<string> readBack = _elements.GetValue(field.Value);
        if (readBack.IsFailure)
        {
            return OperationResult.Fail<Unit>(readBack.Failure);
        }

        if (!string.Equals(readBack.Value, workingCopyAbsolutePath, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituOpenInputFailed,
                $"The picker's file-name field reads '{readBack.Value}' after PrintFlow wrote " +
                $"'{workingCopyAbsolutePath}'. Open was not invoked, so no file was opened.");
        }

        // Verified again before the irreversible half. Writing a value changes a field that
        // nothing acts on; invoking Open is what makes Meitu load a file, so the check closest
        // to it matters most — and a tree walk plus a value write takes long enough for the
        // dialog to be closed or the operator to move away.
        OperationResult<ExternalWindowRef> beforeOpen = VerifyDialog(target, dialog.Handle);
        if (beforeOpen.IsFailure)
        {
            return OperationResult.Fail<Unit>(beforeOpen.Failure);
        }

        OperationResult<UiElementRef> confirm = FindDialogControl(
            target, dialog.Handle, KnownMeituElement.FileDialogOpenButton);
        return confirm.IsFailure
            ? OperationResult.Fail<Unit>(confirm.Failure)
            : _elements.Invoke(confirm.Value);
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituEnhancementOutcome>> RunEnhancementAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(stop);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedWorkingCopyFileName);

        // Before anything at all: is there a signed Enhancement route to run? Resolving this
        // first means a chain that vouches for no evidence costs no Save invocation, rather
        // than opening and cancelling the Save surface only to refuse afterwards.
        OperationResult<MeituEnhancementSignature> signature = EnhancementSignature();
        if (signature.IsFailure)
        {
            return OperationResult.Fail<MeituEnhancementOutcome>(signature.Failure);
        }

        // §5. The exact pre-Enhancement identity probe: Save surface, signed value control,
        // exact derived basename, Cancel. Nothing is saved and no file is written.
        OperationResult<MeituStateSnapshot> before = await ConfirmWorkingCopyIdentityAsync(
            target, expectedWorkingCopyFileName, cancellationToken).ConfigureAwait(false);
        if (before.IsFailure)
        {
            return OperationResult.Fail<MeituEnhancementOutcome>(before.Failure);
        }

        if (before.Value.Observation.ObservedDocumentIdentity is not { Length: > 0 } identity)
        {
            return OperationResult.Fail<MeituEnhancementOutcome>(
                FailureCode.MeituUnknownState,
                "The identity probe reported success without a document-derived value. Enhancement was " +
                "not invoked.");
        }

        // §6. The probe put an owned Save surface in front and then closed it, so the editor's
        // foreground is a thing to re-establish rather than to assume.
        OperationResult<MeituTarget> editor = await ReacquireEditorAsync(
            target, expectedWorkingCopyFileName, identity, cancellationToken).ConfigureAwait(false);
        if (editor.IsFailure)
        {
            return OperationResult.Fail<MeituEnhancementOutcome>(editor.Failure);
        }

        // A stop that arrives before the action is invoked is Part D2A §5: cancel PrintFlow's
        // own orchestration, produce no Meitu operation input at all, and leave Meitu holding
        // nothing from this attempt. Checked here rather than only in the wait loop because
        // this is the last moment at which "no operation input" is still true.
        if (StopBeforeOperation(stop) is { } stoppedEarly)
        {
            return OperationResult.Fail<MeituEnhancementOutcome>(stoppedEarly);
        }

        // §10, §11. The final guard and the one irreversible action. The phase moves before the
        // invoke, not after it: if the process dies between the two, the honest record is that
        // an operation may have been requested, never that none was.
        stop.ReportPhase(ExternalOperationPhase.OperationRequested);
        OperationResult<MeituTarget> invoked = await InvokeEnhancementAsync(
            editor.Value, expectedWorkingCopyFileName, identity, signature.Value, cancellationToken)
            .ConfigureAwait(false);
        if (invoked.IsFailure)
        {
            return OperationResult.Fail<MeituEnhancementOutcome>(invoked.Failure);
        }

        // §12, §13. Read-only from here until completion. Foreground is deliberately not
        // required while observing: the operator may legitimately look at something else while
        // Meitu computes, and demanding focus in order to watch would be a side effect of its
        // own (§18).
        OperationResult<MeituStateSnapshot> busy = await AwaitEnhancementPhaseAsync(
            invoked.Value,
            expectedWorkingCopyFileName,
            identity,
            signature.Value,
            MeituEnhancementPhase.Busy,
            _options.EnhancementBusyTimeout,
            stop,
            cancellationToken).ConfigureAwait(false);
        if (busy.IsFailure)
        {
            return OperationResult.Fail<MeituEnhancementOutcome>(busy.Failure);
        }

        // §14. Positive completion. AwaitEnhancementPhaseAsync only ever returns on a positive
        // match, so "Busy stopped showing" cannot end this wait on its own.
        OperationResult<MeituStateSnapshot> complete = await AwaitEnhancementPhaseAsync(
            invoked.Value,
            expectedWorkingCopyFileName,
            identity,
            signature.Value,
            MeituEnhancementPhase.Complete,
            _options.EnhancementCompletionTimeout,
            stop,
            cancellationToken).ConfigureAwait(false);
        if (complete.IsFailure)
        {
            return OperationResult.Fail<MeituEnhancementOutcome>(complete.Failure);
        }

        // §13. Processing has finished and nothing has been exported. From here a Stop means
        // "do not export" and nothing more — the result Meitu is holding stays where it is.
        stop.ReportPhase(ExternalOperationPhase.CompletedBeforeExport);

        // §15. The same signed probe again. It has to be the same route rather than a cheaper
        // re-read, because the claim being made is the same claim: this is exactly the document
        // PrintFlow handed over, established the only way this workstation can establish it.
        OperationResult<MeituTarget> reacquired = await ReacquireEditorAsync(
            invoked.Value, expectedWorkingCopyFileName, identity, cancellationToken).ConfigureAwait(false);
        if (reacquired.IsFailure)
        {
            return OperationResult.Fail<MeituEnhancementOutcome>(reacquired.Failure);
        }

        OperationResult<MeituStateSnapshot> after = await ConfirmWorkingCopyIdentityAsync(
            reacquired.Value, expectedWorkingCopyFileName, cancellationToken).ConfigureAwait(false);
        if (after.IsFailure)
        {
            return OperationResult.Fail<MeituEnhancementOutcome>(after.Failure);
        }

        OperationResult<MeituTarget> settled = await ReacquireEditorAsync(
            reacquired.Value, expectedWorkingCopyFileName, identity, cancellationToken)
            .ConfigureAwait(false);
        if (settled.IsFailure)
        {
            return OperationResult.Fail<MeituEnhancementOutcome>(settled.Failure);
        }

        return OperationResult.Ok(new MeituEnhancementOutcome(
            settled.Value, identity, before.Value, busy.Value, complete.Value, after.Value));
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituBackgroundRemovalOutcome>> RunBackgroundRemovalAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        BackgroundRemovalDecision modeDecision,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(stop);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedWorkingCopyFileName);

        OperationResult<MeituBackgroundRemovalSignature> signature = BackgroundRemovalSignature();
        if (signature.IsFailure)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(signature.Failure);
        }

        if (modeDecision != BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                "PRODUCT DECISION REQUIRED: Background Removal uses the signed " +
                "OPERATOR_OR_REVIEWED_CONTENT_DECISION policy. A reviewed-content decision was not " +
                "supplied, so no Background Removal input was produced.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["modePolicy"] = "OPERATOR_OR_REVIEWED_CONTENT_DECISION",
                    ["modeDecision"] = modeDecision.ToString(),
                    ["inputSent"] = "false",
                }));
        }

        if (signature.Value.ModePolicy !=
                MeituBackgroundRemovalModePolicy.OperatorOrReviewedContentDecision ||
            !signature.Value.AutoStartsOnEntry ||
            !string.Equals(signature.Value.ObservedAutomaticModeName, "自动选择", StringComparison.Ordinal))
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(
                FailureCode.EnvironmentNotVerified,
                "The signed Background Removal mode behaviour does not match the exercised C1 route. " +
                "Nothing was invoked.");
        }

        OperationResult<MeituStateSnapshot> before = await ConfirmWorkingCopyIdentityAsync(
            target, expectedWorkingCopyFileName, cancellationToken).ConfigureAwait(false);
        if (before.IsFailure)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(before.Failure);
        }

        if (before.Value.Observation.ObservedDocumentIdentity is not { Length: > 0 } identity)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(
                FailureCode.MeituUnknownState,
                "The pre-action identity probe returned no document-derived value. Nothing was invoked.");
        }

        OperationResult<MeituTarget> editor = await ReacquireEditorAsync(
            target, expectedWorkingCopyFileName, identity, cancellationToken).ConfigureAwait(false);
        if (editor.IsFailure)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(editor.Failure);
        }

        // §5, as on the Enhancement route: a stop that arrives before the 抠图 page is entered
        // leaves Meitu holding nothing from this attempt.
        if (StopBeforeOperation(stop) is { } stoppedEarly)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(stoppedEarly);
        }

        stop.ReportPhase(ExternalOperationPhase.OperationRequested);
        OperationResult<MeituTarget> invoked = await InvokeBackgroundRemovalAsync(
            editor.Value,
            expectedWorkingCopyFileName,
            identity,
            signature.Value,
            cancellationToken).ConfigureAwait(false);
        if (invoked.IsFailure)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(invoked.Failure);
        }

        OperationResult<MeituStateSnapshot> busy = await AwaitBackgroundRemovalPhaseAsync(
            invoked.Value,
            expectedWorkingCopyFileName,
            identity,
            signature.Value,
            MeituBackgroundRemovalPhase.Busy,
            _options.BackgroundRemovalBusyTimeout,
            stop,
            cancellationToken).ConfigureAwait(false);
        if (busy.IsFailure)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(busy.Failure);
        }

        OperationResult<MeituStateSnapshot> completion = await AwaitBackgroundRemovalPhaseAsync(
            invoked.Value,
            expectedWorkingCopyFileName,
            identity,
            signature.Value,
            MeituBackgroundRemovalPhase.Complete,
            _options.BackgroundRemovalCompletionTimeout,
            stop,
            cancellationToken).ConfigureAwait(false);
        if (completion.IsFailure)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(completion.Failure);
        }

        // §13. The cutout exists inside Meitu and nothing has been written. A stop from here
        // means do not export — the 调整 return below is navigation, not output.
        stop.ReportPhase(ExternalOperationPhase.CompletedBeforeExport);

        OperationResult<MeituTarget> returned = await InvokeBackgroundRemovalReturnAsync(
            invoked.Value, signature.Value, cancellationToken).ConfigureAwait(false);
        if (returned.IsFailure)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(returned.Failure);
        }

        OperationResult<MeituTarget> reacquired = await ReacquireEditorAsync(
            returned.Value, expectedWorkingCopyFileName, identity, cancellationToken).ConfigureAwait(false);
        if (reacquired.IsFailure)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(reacquired.Failure);
        }

        OperationResult<MeituStateSnapshot> after = await ConfirmWorkingCopyIdentityAsync(
            reacquired.Value, expectedWorkingCopyFileName, cancellationToken).ConfigureAwait(false);
        if (after.IsFailure)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(after.Failure);
        }

        if (!string.Equals(
                after.Value.Observation.ObservedDocumentIdentity,
                identity,
                StringComparison.Ordinal))
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(OperationFailure.Create(
                FailureCode.MeituUnknownState,
                "The document identity changed between Background Removal action and completion. " +
                "No completion claim is returned.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["identityBefore"] = identity,
                    ["identityAfter"] = after.Value.Observation.ObservedDocumentIdentity ?? "(missing)",
                    ["exported"] = "false",
                    ["revisionCreated"] = "false",
                }));
        }

        OperationResult<MeituTarget> settled = await ReacquireEditorAsync(
            reacquired.Value, expectedWorkingCopyFileName, identity, cancellationToken).ConfigureAwait(false);
        if (settled.IsFailure)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(settled.Failure);
        }

        return OperationResult.Ok(new MeituBackgroundRemovalOutcome(
            settled.Value,
            identity,
            modeDecision,
            signature.Value.ObservedAutomaticModeName,
            before.Value,
            busy.Value,
            completion.Value,
            after.Value));
    }

    private async Task<OperationResult<MeituTarget>> InvokeBackgroundRemovalAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        string observedDocumentIdentity,
        MeituBackgroundRemovalSignature signature,
        CancellationToken cancellationToken)
    {
        OperationResult<MeituTarget> verified = await VerifyTargetAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (verified.IsFailure)
        {
            return verified;
        }

        OperationResult<MeituStateSnapshot> state = await InspectStateCoreAsync(
            verified.Value,
            expectedWorkingCopyFileName,
            observedDocumentIdentity,
            cancellationToken).ConfigureAwait(false);
        if (state.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(state.Failure);
        }

        if (state.Value.State != MeituStartingState.KnownEditorWithExpectedWorkingCopy)
        {
            return OperationResult.Fail<MeituTarget>(OperationFailure.Create(
                FailureCode.MeituUnknownState,
                $"Immediately before Background Removal the editor classified as '{state.Value.State}'. " +
                "Nothing was invoked.",
                isRetryable: false,
                context: new Dictionary<string, string> { ["inputSent"] = "false" }));
        }

        MeituBackgroundRemovalPhase phase = MeituBackgroundRemovalRule.Classify(
            signature, state.Value.Observation);
        if (phase != MeituBackgroundRemovalPhase.Unobserved)
        {
            return OperationResult.Fail<MeituTarget>(OperationFailure.Create(
                FailureCode.MeituUnknownState,
                phase == MeituBackgroundRemovalPhase.Busy
                    ? "Background Removal is already processing; a second invocation is refused."
                    : "A Background Removal result panel is already present; it is not proof of work on " +
                      "this load and the page entry will not be invoked.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["phase"] = phase.ToString(),
                    ["inputSent"] = "false",
                }));
        }

        OperationResult<UiElementRef> action = FindKnownElement(
            verified.Value, KnownMeituElement.EditorBackgroundRemovalAction);
        if (action.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(action.Failure);
        }

        OperationResult<MeituTarget> stillOurs = await VerifyTargetAsync(
            verified.Value, cancellationToken).ConfigureAwait(false);
        if (stillOurs.IsFailure)
        {
            return stillOurs;
        }

        OperationResult<Unit> sent = _elements.Invoke(action.Value);
        return sent.IsFailure
            ? OperationResult.Fail<MeituTarget>(sent.Failure)
            : OperationResult.Ok(stillOurs.Value);
    }

    private async Task<OperationResult<MeituTarget>> InvokeBackgroundRemovalReturnAsync(
        MeituTarget target,
        MeituBackgroundRemovalSignature signature,
        CancellationToken cancellationToken)
    {
        OperationResult<MeituTarget> verified = await ReacquireForegroundAsync(
            target, cancellationToken).ConfigureAwait(false);
        if (verified.IsFailure)
        {
            return verified;
        }

        OperationResult<MeituStateSnapshot> snapshot = await InspectStateCoreAsync(
            verified.Value, expectedWorkingCopyFileName: null, observedDocumentIdentity: null,
            cancellationToken).ConfigureAwait(false);
        if (snapshot.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(snapshot.Failure);
        }

        if (snapshot.Value.State == MeituStartingState.KnownModal ||
            MeituBackgroundRemovalRule.Classify(signature, snapshot.Value.Observation) !=
                MeituBackgroundRemovalPhase.Complete)
        {
            return OperationResult.Fail<MeituTarget>(
                FailureCode.MeituUnknownState,
                "The positive Background Removal completion surface is no longer present, so the signed " +
                "return action was not invoked.");
        }

        OperationResult<UiElementRef> action = FindKnownElement(
            verified.Value, KnownMeituElement.EditorBackgroundRemovalReturn);
        if (action.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(action.Failure);
        }

        OperationResult<MeituTarget> stillOurs = await VerifyTargetAsync(
            verified.Value, cancellationToken).ConfigureAwait(false);
        if (stillOurs.IsFailure)
        {
            return stillOurs;
        }

        OperationResult<Unit> sent = _elements.Invoke(action.Value);
        return sent.IsFailure
            ? OperationResult.Fail<MeituTarget>(sent.Failure)
            : OperationResult.Ok(stillOurs.Value);
    }

    private async Task<OperationResult<MeituStateSnapshot>> AwaitBackgroundRemovalPhaseAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        string observedDocumentIdentity,
        MeituBackgroundRemovalSignature signature,
        MeituBackgroundRemovalPhase wanted,
        TimeSpan timeout,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + timeout;
        MeituBackgroundRemovalPhase last = MeituBackgroundRemovalPhase.Unobserved;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OperationResult<MeituStateSnapshot> snapshot = ReadBackgroundRemovalPhaseSnapshot(
                target,
                expectedWorkingCopyFileName,
                observedDocumentIdentity,
                signature);
            if (snapshot.IsFailure)
            {
                return snapshot;
            }

            if (snapshot.Value.State == MeituStartingState.KnownModal)
            {
                // §20. Recorded before the refusal, so a takeover requested a moment later
                // resolves against the blocked screen rather than the last happy phase.
                stop.ReportPhase(ExternalOperationPhase.UnknownOrBlocked);
                return OperationResult.Fail<MeituStateSnapshot>(
                    FailureCode.MeituBlockingDialog,
                    "A Meitu-owned modal appeared during Background Removal. It was not dismissed and no " +
                    "further input was produced.");
            }

            last = MeituBackgroundRemovalRule.Classify(signature, snapshot.Value.Observation);

            if (last == MeituBackgroundRemovalPhase.Busy)
            {
                stop.ReportPhase(ExternalOperationPhase.Busy);
            }

            // §9, checked while Busy is still true — the only phase in which the signed cancel
            // is eligible.
            if (await StopDuringBusyAsync(
                    stop, target, MeituOperation.RemoveBackground, expectedWorkingCopyFileName,
                    cancellationToken).ConfigureAwait(false) is { } stopped)
            {
                return OperationResult.Fail<MeituStateSnapshot>(stopped);
            }

            if (last == wanted)
            {
                if (wanted == MeituBackgroundRemovalPhase.Busy)
                {
                    return snapshot;
                }

                // Completion controls persist, unlike the short-lived progress messages. Once
                // the fast signed-marker read sees them, take the ordinary full observation so
                // the returned evidence still includes the editor state and modal checks.
                OperationResult<MeituStateSnapshot> full = await InspectStateCoreAsync(
                    target,
                    expectedWorkingCopyFileName,
                    observedDocumentIdentity,
                    cancellationToken).ConfigureAwait(false);
                if (full.IsFailure)
                {
                    return full;
                }

                if (MeituBackgroundRemovalRule.Classify(signature, full.Value.Observation) == wanted)
                {
                    return full;
                }

                last = MeituBackgroundRemovalRule.Classify(signature, full.Value.Observation);
            }

            if (last == MeituBackgroundRemovalPhase.Unobserved)
            {
                // The fast operation-specific read intentionally sees only Busy/result markers.
                // Distinguish an ordinary, still-recognised editor (which may legitimately take
                // a moment to show Busy) from a genuinely Unknown screen, which must stop now.
                OperationResult<MeituStateSnapshot> full = await InspectStateCoreAsync(
                    target,
                    expectedWorkingCopyFileName,
                    observedDocumentIdentity,
                    cancellationToken).ConfigureAwait(false);
                if (full.IsFailure)
                {
                    return full;
                }

                if (full.Value.State == MeituStartingState.KnownModal)
                {
                    return OperationResult.Fail<MeituStateSnapshot>(
                        FailureCode.MeituBlockingDialog,
                        "A Meitu-owned modal appeared during Background Removal. It was not " +
                        "dismissed and no further input was produced.");
                }

                if (full.Value.State == MeituStartingState.Unknown)
                {
                    return OperationResult.Fail<MeituStateSnapshot>(OperationFailure.Create(
                        FailureCode.MeituUnknownState,
                        "Meitu changed to an unrecognised editor state during Background Removal. " +
                        "PrintFlow stopped without navigation, export or further input.",
                        isRetryable: true,
                        context: new Dictionary<string, string>
                        {
                            ["phase"] = last.ToString(),
                            ["inputSent"] = "false",
                            ["exported"] = "false",
                            ["operatorActionRequired"] = "true",
                        }));
                }
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<MeituStateSnapshot>(OperationFailure.Create(
                    FailureCode.Timeout,
                    $"Meitu did not reach the signed Background Removal '{wanted}' state within " +
                    $"{timeout.TotalSeconds:0} s; last phase was '{last}'. No export or Revision exists.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["wantedPhase"] = wanted.ToString(),
                        ["lastPhase"] = last.ToString(),
                        ["exported"] = "false",
                        ["revisionCreated"] = "false",
                        ["retainedExternalState"] = wanted == MeituBackgroundRemovalPhase.Complete
                            ? "possibly-busy"
                            : "unknown",
                    }));
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private OperationResult<MeituStateSnapshot> ReadBackgroundRemovalPhaseSnapshot(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        string observedDocumentIdentity,
        MeituBackgroundRemovalSignature signature)
    {
        OperationResult<ExternalWindowRef> window = RefreshOwnedWindow(target);
        if (window.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(window.Failure);
        }

        OperationResult<IReadOnlyList<ExternalWindowRef>> dialogs =
            _locator.FindOwnedDialogs(target.Process, window.Value);
        if (dialogs.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(dialogs.Failure);
        }

        string[] markers =
        [
            .. signature.Busy.RequiredMarkers,
            .. signature.Completion.RequiredMarkers,
        ];
        OperationResult<IReadOnlyList<string>> texts =
            _elements.ReadMatchingTextSnapshot(window.Value.Handle, markers);
        if (texts.IsFailure)
        {
            return OperationResult.Fail<MeituStateSnapshot>(texts.Failure);
        }

        MeituObservation observation = new(
            window.Value.Title,
            [.. texts.Value],
            [.. dialogs.Value.Select(dialog => dialog.Title)],
            window.Value.IsEnabled,
            expectedWorkingCopyFileName,
            observedDocumentIdentity);
        MeituBackgroundRemovalPhase phase = MeituBackgroundRemovalRule.Classify(signature, observation);
        MeituStartingState state = !window.Value.IsEnabled || dialogs.Value.Count > 0
            ? MeituStartingState.KnownModal
            : phase == MeituBackgroundRemovalPhase.Busy
                ? MeituStartingState.Busy
                : MeituStartingState.Unknown;
        return OperationResult.Ok(new MeituStateSnapshot(state, [], observation));
    }

    /// <summary>
    /// Brings the target back to the foreground and verifies it got there, retrying for a
    /// bounded time (Epic 11300 Part B2A §18).
    /// </summary>
    /// <remarks>
    /// The difference between this and <see cref="VerifyTargetAsync"/> is who is expected to
    /// have put Meitu in front. <see cref="VerifyTargetAsync"/> asks "is it in front?" and is
    /// the guard immediately before input. This asks Windows to <i>put</i> it in front and then
    /// asks the same question — which is what §18 means by positively reacquiring, and it is
    /// needed because Meitu itself displaces its own editor: the picker closing after an open,
    /// and the Save surface closing after Cancel, both hand the foreground to a short-lived
    /// Meitu window rather than back to the editor.
    ///
    /// Part B1.1 saw exactly that twice, and reported it as a refusal with nothing sent. The
    /// refusal was correct; requiring a caller to have won a race it cannot see was not. Note
    /// what this does <b>not</b> do: it never sends input, and it never proceeds on a target it
    /// has not just seen hold the foreground. A window that will not come forward within the
    /// budget is still a <see cref="FailureCode.MeituTargetLost"/> with nothing sent.
    /// </remarks>
    private async Task<OperationResult<MeituTarget>> ReacquireForegroundAsync(
        MeituTarget target, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.ActivationTimeout + _options.DialogTimeout;
        OperationFailure? last = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<MeituTarget> activated = await ActivateAsync(target, cancellationToken)
                .ConfigureAwait(false);
            if (activated.IsSuccess)
            {
                OperationResult<MeituTarget> verified = await VerifyTargetAsync(
                    activated.Value, cancellationToken).ConfigureAwait(false);
                if (verified.IsSuccess)
                {
                    return verified;
                }

                last = verified.Failure;
            }
            else
            {
                last = activated.Failure;
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<MeituTarget>(last);
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Re-establishes the editor after something else held the foreground, and re-confirms it is
    /// still showing the expected document (Epic 11300 Part B2A §6, §18).
    /// </summary>
    /// <param name="observedDocumentIdentity">
    /// The value the signed Save surface exposed moments ago.
    /// </param>
    /// <remarks>
    /// The carried identity is the one part of this worth explaining. Re-classifying with it,
    /// rather than probing again, is not a shortcut: a fresh probe means another Save
    /// invocation, and calling this method after every probe would then recurse. What the
    /// carried value lets the classifier state is precise and still worth stating — the editor
    /// is the same window of the same process, it is enabled, unblocked and in front, its signed
    /// markers still match, and the identity read from it a moment ago was exact. §15 is what
    /// re-establishes the identity itself, through the probe, after the work is done.
    /// </remarks>
    private async Task<OperationResult<MeituTarget>> ReacquireEditorAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        string observedDocumentIdentity,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.ActivationTimeout + _options.DialogTimeout;
        OperationFailure? last = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<MeituTarget> verified =
                await ReacquireForegroundAsync(target, cancellationToken).ConfigureAwait(false);
            if (verified.IsSuccess)
            {
                OperationResult<MeituStateSnapshot> state = await InspectStateCoreAsync(
                    verified.Value, expectedWorkingCopyFileName, observedDocumentIdentity,
                    cancellationToken).ConfigureAwait(false);

                if (state.IsSuccess &&
                    state.Value.State == MeituStartingState.KnownEditorWithExpectedWorkingCopy)
                {
                    return OperationResult.Ok(verified.Value);
                }

                last = state.IsFailure
                    ? state.Failure
                    : OperationFailure.Create(
                        FailureCode.MeituUnknownState,
                        $"The Meitu editor is on '{state.Value.State}' rather than showing the expected " +
                        "working copy on a signed settled document surface. No editor input was produced.",
                        isRetryable: true,
                        context: new Dictionary<string, string>
                        {
                            ["state"] = state.Value.State.ToString(),
                            ["expectedFile"] = expectedWorkingCopyFileName,
                            ["inputSent"] = "false",
                        });
            }
            else
            {
                last = verified.Failure;
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<MeituTarget>(last);
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The final guard and the single Enhancement invocation (Epic 11300 Part B2A §10, §11).
    /// </summary>
    /// <remarks>
    /// Every check §10 lists happens here, in this order, and the ones that can go stale during
    /// a tree walk happen twice — once to decide whether to look for the control, once after it
    /// has been found and immediately before it is invoked. Nothing between the last check and
    /// the invocation reads the automation tree.
    ///
    /// There is exactly one <see cref="IUiElementProvider.Invoke"/> call and no retry. An
    /// Enhancement that was invoked but whose effect was not observed is a state to report, not
    /// a reason to invoke it a second time over work that may already be running.
    /// </remarks>
    private async Task<OperationResult<MeituTarget>> InvokeEnhancementAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        string observedDocumentIdentity,
        MeituEnhancementSignature signature,
        CancellationToken cancellationToken)
    {
        // Process id and start time, window ownership, and exact foreground.
        OperationResult<MeituTarget> verified = await VerifyTargetAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (verified.IsFailure)
        {
            return verified;
        }

        // No blocking modal, and the document is still the expected one.
        OperationResult<MeituStateSnapshot> state = await InspectStateCoreAsync(
            verified.Value, expectedWorkingCopyFileName, observedDocumentIdentity, cancellationToken)
            .ConfigureAwait(false);
        if (state.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(state.Failure);
        }

        if (state.Value.State != MeituStartingState.KnownEditorWithExpectedWorkingCopy)
        {
            return OperationResult.Fail<MeituTarget>(OperationFailure.Create(
                FailureCode.MeituUnknownState,
                $"Immediately before Enhancement the Meitu editor classified as " +
                $"'{state.Value.State}', not as the expected working copy. Nothing was invoked.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["state"] = state.Value.State.ToString(),
                    ["expectedFile"] = expectedWorkingCopyFileName,
                    ["inputSent"] = "false",
                }));
        }

        // The Enhancement control is a Qt CheckBox that Meitu treats as a toggle, and Meitu
        // remembers which module is selected across document loads. Both were observed live:
        // opening a new image while the Enhancement module was still selected started an
        // enhancement with no input at all, and invoking the control while it was selected
        // turned the module *off*.
        // So the phase is checked before the invoke as well as after it: the one guarded
        // invocation §11 permits must be the one that starts the work, never one that cancels a
        // module someone else selected.
        MeituEnhancementPhase phase = MeituEnhancementRule.Classify(
            signature, state.Value.Observation);
        if (phase != MeituEnhancementPhase.Unobserved)
        {
            return OperationResult.Fail<MeituTarget>(OperationFailure.Create(
                FailureCode.MeituUnknownState,
                phase == MeituEnhancementPhase.Busy
                    ? "Meitu is already running an Enhancement. PrintFlow will not begin a second one " +
                      "over work in progress. Nothing was invoked."
                    : "The Enhancement module is already selected, so invoking its control would " +
                      "deselect it rather than start the work. Nothing was invoked.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["phase"] = phase.ToString(),
                    ["expectedFile"] = expectedWorkingCopyFileName,
                    ["inputSent"] = "false",
                }));
        }

        // Element ownership, structural signature, enabled and onscreen — all of it inside the
        // signed rule, which refuses rather than picks.
        OperationResult<UiElementRef> action = FindKnownElement(
            verified.Value, KnownMeituElement.EditorEnhancementAction);
        if (action.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(action.Failure);
        }

        // Re-verified after the tree walk, which is the check closest to the irreversible half.
        OperationResult<MeituTarget> stillOurs = await VerifyTargetAsync(verified.Value, cancellationToken)
            .ConfigureAwait(false);
        if (stillOurs.IsFailure)
        {
            return stillOurs;
        }

        OperationResult<Unit> sent = _elements.Invoke(action.Value);
        return sent.IsFailure
            ? OperationResult.Fail<MeituTarget>(sent.Failure)
            : OperationResult.Ok(stillOurs.Value);
    }

    /// <summary>
    /// Polls read-only until the signed evidence positively shows <paramref name="wanted"/>, or
    /// the bounded budget expires (Epic 11300 Part B2A §12–§14, §17).
    /// </summary>
    /// <remarks>
    /// Returns only on a positive match. That is the whole difference between this and a wait
    /// that watches Busy go away: a timeout here is a structured failure naming the last phase
    /// seen, never a success carrying <see cref="MeituEnhancementPhase.Unobserved"/> — the
    /// <c>Ok(Unknown)</c> shape §24 rules out.
    ///
    /// Nothing in this loop produces input, and it deliberately does not require the foreground.
    /// A run that is refused for timeout has still sent nothing beyond the single invocation
    /// that started it, and it does not close, cancel or otherwise nudge Meitu on its way out
    /// (§17).
    /// </remarks>
    private async Task<OperationResult<MeituStateSnapshot>> AwaitEnhancementPhaseAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        string? observedDocumentIdentity,
        MeituEnhancementSignature signature,
        MeituEnhancementPhase wanted,
        TimeSpan timeout,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + timeout;
        MeituEnhancementPhase lastPhase = MeituEnhancementPhase.Unobserved;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<MeituStateSnapshot> snapshot = await InspectStateCoreAsync(
                target, expectedWorkingCopyFileName, observedDocumentIdentity, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot.IsFailure)
            {
                return snapshot;
            }

            // A Meitu-owned modal during processing stops the run rather than being waited
            // through. PrintFlow does not read it, dismiss it or click it (§19).
            if (snapshot.Value.State == MeituStartingState.KnownModal)
            {
                // §20. The phase becomes UnknownOrBlocked before the failure is built, so a
                // takeover requested a moment later resolves against what is actually on screen
                // rather than against the last phase PrintFlow was happy about.
                stop.ReportPhase(ExternalOperationPhase.UnknownOrBlocked);
                return OperationResult.Fail<MeituStateSnapshot>(OperationFailure.Create(
                    FailureCode.MeituBlockingDialog,
                    "A dialog owned by Meitu appeared while Enhancement was being observed. PrintFlow does " +
                    "not dismiss dialogs it cannot identify; the operator must resolve it. No further input " +
                    "was produced and no output was exported.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["dialogTitles"] = snapshot.Value.Observation.OwnedDialogTitles.IsDefaultOrEmpty
                            ? "(none read)"
                            : string.Join(" | ", snapshot.Value.Observation.OwnedDialogTitles),
                        ["phase"] = lastPhase.ToString(),
                        ["inputSent"] = "false",
                    }));
            }

            lastPhase = MeituEnhancementRule.Classify(signature, snapshot.Value.Observation);

            // Reported from what was just read rather than from where the code has got to. This
            // is the value AutomationStopPolicy resolves against, so it has to describe Meitu
            // and not PrintFlow's position in the sequence (§4).
            if (lastPhase == MeituEnhancementPhase.Busy)
            {
                stop.ReportPhase(ExternalOperationPhase.Busy);
            }

            // §9. Checked after the phase is reported and before the wanted-state return, so a
            // Stop that arrives during Busy is honoured while Busy is still true — which is the
            // only phase in which the signed cancel is eligible at all.
            if (await StopDuringBusyAsync(
                    stop, target, MeituOperation.Enhance, expectedWorkingCopyFileName, cancellationToken)
                .ConfigureAwait(false) is { } stopped)
            {
                return OperationResult.Fail<MeituStateSnapshot>(stopped);
            }

            if (lastPhase == wanted)
            {
                return snapshot;
            }

            if (lastPhase == MeituEnhancementPhase.Unobserved &&
                snapshot.Value.State == MeituStartingState.Unknown)
            {
                stop.ReportPhase(ExternalOperationPhase.UnknownOrBlocked);
                return OperationResult.Fail<MeituStateSnapshot>(OperationFailure.Create(
                    FailureCode.MeituUnknownState,
                    "Meitu changed to an unrecognised editor state while Enhancement was being " +
                    "observed. PrintFlow stopped without navigation, export or further input.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["phase"] = lastPhase.ToString(),
                        ["inputSent"] = "false",
                        ["exported"] = "false",
                        ["operatorActionRequired"] = "true",
                    }));
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<MeituStateSnapshot>(OperationFailure.Create(
                    FailureCode.Timeout,
                    $"Meitu did not reach the signed Enhancement '{wanted}' state within " +
                    $"{timeout.TotalSeconds:0} s; the last positively recognised phase was '{lastPhase}'. " +
                    "No output was exported and no Revision was created.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["wantedPhase"] = wanted.ToString(),
                        ["lastPhase"] = lastPhase.ToString(),
                        ["state"] = snapshot.Value.State.ToString(),
                        ["timeoutSeconds"] =
                            timeout.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                        ["exported"] = "false",
                        ["revisionCreated"] = "false",
                        ["retainedExternalState"] = wanted == MeituEnhancementPhase.Complete
                            ? "possibly-busy"
                            : "unknown",
                    }));
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituTarget>> CloseDocumentAsync(
        MeituTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        OperationResult<Unit> closed = await InvokeKnownElementAsync(
            target, KnownMeituElement.EditorCloseDocumentControl, cancellationToken).ConfigureAwait(false);
        if (closed.IsFailure)
        {
            return OperationResult.Fail<MeituTarget>(closed.Failure);
        }

        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<MeituStateSnapshot> state = await InspectStateAsync(
                target, expectedWorkingCopyFileName: null, cancellationToken).ConfigureAwait(false);
            if (state.IsFailure)
            {
                return OperationResult.Fail<MeituTarget>(state.Failure);
            }

            if (state.Value.State == MeituStartingState.KnownEditorEmpty)
            {
                return OperationResult.Ok(target);
            }

            if (state.Value.State == MeituStartingState.KnownModal)
            {
                // Most likely an unsaved-changes prompt. Answering it either way is a decision
                // about the operator's work, and this slice does not build modal automation.
                return OperationResult.Fail<MeituTarget>(OperationFailure.Create(
                    FailureCode.MeituBlockingDialog,
                    "Closing the document raised a dialog owned by Meitu. PrintFlow does not answer it; the " +
                    "operator must. The synthetic workspace is retained because Meitu may still hold the file.",
                    isRetryable: false,
                    context: new Dictionary<string, string>
                    {
                        ["dialogTitles"] = state.Value.Observation.OwnedDialogTitles.IsDefaultOrEmpty
                            ? "(none read)"
                            : string.Join(" | ", state.Value.Observation.OwnedDialogTitles),
                        ["inputSent"] = "close-only",
                    }));
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<MeituTarget>(OperationFailure.Create(
                    FailureCode.MeituUnknownState,
                    $"The signed close control was invoked, but Meitu did not reach the signed empty editor " +
                    $"within {_options.DialogTimeout.TotalSeconds:0} s; it is on '{state.Value.State}'. The " +
                    "document may still be loaded, so the working file must not be deleted.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["state"] = state.Value.State.ToString(),
                        ["inputSent"] = "close-only",
                    }));
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituExportEvidence>> ExportResultAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        string observedDocumentIdentity,
        string destinationAbsolutePath,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(stop);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedWorkingCopyFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(observedDocumentIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationAbsolutePath);

        OperationResult<MeituExportSignature> signature = ExportSignature();
        if (signature.IsFailure)
        {
            return OperationResult.Fail<MeituExportEvidence>(signature.Failure);
        }

        // The destination is taken apart before Meitu is touched. A path PrintFlow would refuse
        // to write to should cost no Save surface and no dialog, and finding that out after the
        // editor has already been driven into a modal would leave the screen in a state the
        // caller then has to unwind.
        OperationResult<MeituExportDestination> destination = MeituExportRule.ResolveDestination(
            destinationAbsolutePath, signature.Value.RequiredFormatValue);
        if (destination.IsFailure)
        {
            return OperationResult.Fail<MeituExportEvidence>(destination.Failure);
        }

        // §12's editor-side guard: the target is still Meitu's, still in front, still holding
        // exactly the document this attempt handed over.
        OperationResult<MeituTarget> editor = await ReacquireEditorAsync(
            target, expectedWorkingCopyFileName, observedDocumentIdentity, cancellationToken)
            .ConfigureAwait(false);
        if (editor.IsFailure)
        {
            return OperationResult.Fail<MeituExportEvidence>(editor.Failure);
        }

        // §13. The last moment at which "no export has started" is still true. A stop here means
        // exactly what §13 says it means — do not export — and the processed result Meitu is
        // holding is left alone rather than discarded.
        if (StopBeforeExport(stop) is { } stoppedBeforeExport)
        {
            return OperationResult.Fail<MeituExportEvidence>(stoppedBeforeExport);
        }

        OperationResult<Unit> raised = await InvokeKnownElementAsync(
            editor.Value, KnownMeituElement.EditorSaveControl, cancellationToken).ConfigureAwait(false);
        if (raised.IsFailure)
        {
            return OperationResult.Fail<MeituExportEvidence>(raised.Failure);
        }

        OperationResult<ExternalWindowRef> surface = await WaitForExportSurfaceAsync(
            editor.Value, signature.Value, cancellationToken).ConfigureAwait(false);
        if (surface.IsFailure)
        {
            return OperationResult.Fail<MeituExportEvidence>(surface.Failure);
        }

        // §14. A save surface is now open and nothing irreversible has happened. From here a
        // stop prefers the signed cancel route this validated export already contains, rather
        // than walking away and leaving a modal blocking the editor.
        stop.ReportPhase(ExternalOperationPhase.ExportPrepared);

        return await DriveExportSurfaceAsync(
            editor.Value, surface.Value, signature.Value, destination.Value, stop, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The refusal for a Stop that arrived after processing finished but before any export
    /// input (Epic 11300 Part D2A §13), or <c>null</c> when no stop is pending.
    /// </summary>
    /// <remarks>
    /// Note what this deliberately does <b>not</b> do: it does not close the document, does not
    /// undo the operation, and does not attempt to discard the processed result. §13 permits
    /// exactly one thing here — not exporting — and the retained result is reported rather than
    /// tidied away, because discarding an operator's processed image is a separate, signed,
    /// explicitly requested action that this slice does not have.
    /// </remarks>
    private static OperationFailure? StopBeforeExport(IAutomationStopSignal stop)
    {
        if (stop.RequestedMode is not { } mode)
        {
            return null;
        }

        return OperationFailure.Create(
            FailureCode.Cancelled,
            $"The operator requested '{mode}' after processing finished and before the export began. " +
            "PrintFlow exported nothing and created no Revision. Meitu may still be holding the " +
            "processed result; PrintFlow has not discarded it and has not saved it.",
            isRetryable: true,
            context: new Dictionary<string, string>
            {
                ["stopMode"] = mode.ToString(),
                ["phase"] = ExternalOperationPhase.CompletedBeforeExport.ToString(),
                ["inputSent"] = "false",
                ["exported"] = "false",
                ["revisionCreated"] = "false",
                ["meituCancelInvoked"] = "false",
                ["forceTerminationInvoked"] = "false",
                ["retainedExternalState"] = RetainedExternalState.ProcessedResultRetained.ToString(),
            });
    }

    /// <summary>
    /// Sets the signed fields, opens the destination dialog and confirms it exactly once
    /// (Epic 11300 Part B2B §8–§12).
    /// </summary>
    /// <remarks>
    /// Every value is written and then read back before the next step, and a mismatch stops the
    /// run before anything is invoked — which is §8's rule. The route is built the way it is
    /// because that rule turned out not to be sufficient on its own for one control, and the
    /// discovery is worth keeping next to the code it shaped: writing the Save surface's
    /// <c>folderEdit</c> succeeds, reads back exactly, and does not move the export. The file
    /// landed in Meitu's remembered folder while the field displayed the controlled path.
    ///
    /// So the directory is never named on this surface at all. What is set here is the base name
    /// and the format, both of which Meitu carries forward into the destination dialog, and the
    /// destination itself is named there as a full path — in a Windows common dialog whose
    /// file-name field is the same shape the open picker already uses.
    /// </remarks>
    private async Task<OperationResult<MeituExportEvidence>> DriveExportSurfaceAsync(
        MeituTarget target,
        ExternalWindowRef surface,
        MeituExportSignature signature,
        MeituExportDestination destination,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        OperationResult<ExternalWindowRef> verified = VerifyExportSurface(target, surface.Handle, signature);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<MeituExportEvidence>(verified.Failure);
        }

        // §14. The save surface is open and nothing has been confirmed. Backing out uses the
        // signed cancel that is already part of this validated route — the same control a
        // read-back failure would use — so the operator is not left with a modal over the
        // editor. This is the one place where a stop legitimately produces input, and it
        // produces the input that writes nothing.
        if (StopBeforeExportConfirm(stop) is { } stoppedAtSurface)
        {
            return await CancelExportSurfaceAsync<MeituExportEvidence>(
                target, surface, stoppedAtSurface, cancellationToken).ConfigureAwait(false);
        }

        // Format first, because it is the check most likely to refuse and the one §11 will not
        // let PrintFlow infer. PNG continues without opening the dropdown. A JPG default uses
        // only the supplemental, signed popup route and still has to survive a fresh read-back.
        OperationResult<string> format = await EnsureExportFormatAsync(
            target, surface, signature, cancellationToken).ConfigureAwait(false);
        if (format.IsFailure)
        {
            return await CancelExportSurfaceAsync<MeituExportEvidence>(
                target, surface, format.Failure, cancellationToken).ConfigureAwait(false);
        }

        OperationResult<string> baseName = await SetAndReadBackAsync(
            target, surface.Handle, signature.FileNameControl, destination.BaseName,
            StringComparison.Ordinal, "output base name", cancellationToken).ConfigureAwait(false);
        if (baseName.IsFailure)
        {
            return await CancelExportSurfaceAsync<MeituExportEvidence>(
                target, surface, baseName.Failure, cancellationToken).ConfigureAwait(false);
        }

        // Re-verified after two tree walks and two writes, immediately before the control that
        // changes what is on screen.
        verified = VerifyExportSurface(target, surface.Handle, signature);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<MeituExportEvidence>(verified.Failure);
        }

        OperationResult<UiElementRef> saveAs = FindSignedControl(
            target, surface.Handle, signature.SaveAsControl);
        if (saveAs.IsFailure)
        {
            return await CancelExportSurfaceAsync<MeituExportEvidence>(
                target, surface, saveAs.Failure, cancellationToken).ConfigureAwait(false);
        }

        OperationResult<Unit> opened = _elements.Invoke(saveAs.Value);
        if (opened.IsFailure)
        {
            return OperationResult.Fail<MeituExportEvidence>(opened.Failure);
        }

        OperationResult<ExternalWindowRef> dialog = await WaitForDestinationDialogAsync(
            target, signature.Destination, cancellationToken).ConfigureAwait(false);
        if (dialog.IsFailure)
        {
            return OperationResult.Fail<MeituExportEvidence>(dialog.Failure);
        }

        return await ConfirmDestinationAsync(
            target, dialog.Value, signature, destination, format.Value, baseName.Value, stop,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The refusal for a Stop that arrived while an export surface or destination dialog is
    /// open and nothing irreversible has happened (Epic 11300 Part D2A §14).
    /// </summary>
    /// <remarks>
    /// Returned to a caller that will back out through the signed cancel for that exact
    /// surface, which is why this method produces only the failure and never the input: keeping
    /// "what to say" separate from "which control to press" is what stops a future edit from
    /// reaching for a cancel on a surface whose signed route has not been established.
    /// </remarks>
    private static OperationFailure? StopBeforeExportConfirm(IAutomationStopSignal stop)
    {
        if (stop.RequestedMode is not { } mode)
        {
            return null;
        }

        return OperationFailure.Create(
            FailureCode.Cancelled,
            $"The operator requested '{mode}' with Meitu's save surface open and before the " +
            "irreversible confirm. PrintFlow backed out through the signed cancel control for that " +
            "exact surface: no confirm was invoked, no file was written and no Revision was created.",
            isRetryable: true,
            context: new Dictionary<string, string>
            {
                ["stopMode"] = mode.ToString(),
                ["phase"] = ExternalOperationPhase.ExportPrepared.ToString(),
                ["confirmInvoked"] = "false",
                ["exported"] = "false",
                ["revisionCreated"] = "false",
                ["meituCancelInvoked"] = "false",
                ["forceTerminationInvoked"] = "false",
                ["retainedExternalState"] = RetainedExternalState.ProcessedResultRetained.ToString(),
            });
    }

    /// <summary>
    /// Names the controlled destination in the signed dialog and invokes its confirm control once
    /// (Epic 11300 Part B2B §10, §12, §13).
    /// </summary>
    private async Task<OperationResult<MeituExportEvidence>> ConfirmDestinationAsync(
        MeituTarget target,
        ExternalWindowRef dialog,
        MeituExportSignature signature,
        MeituExportDestination destination,
        string confirmedFormat,
        string confirmedBaseName,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        MeituExportDestinationSignature shape = signature.Destination;

        OperationResult<ExternalWindowRef> verified = VerifyDestinationDialog(target, dialog.Handle, shape);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<MeituExportEvidence>(verified.Failure);
        }

        // §14 again, on the destination dialog this time. Its signed cancel is part of the same
        // validated route, so a stop that arrives here also leaves the screen unblocked and the
        // filesystem untouched.
        if (StopBeforeExportConfirm(stop) is { } stoppedAtDialog)
        {
            return await CancelDestinationAsync<MeituExportEvidence>(
                target, dialog, shape, stoppedAtDialog, cancellationToken).ConfigureAwait(false);
        }

        OperationResult<UiElementRef> field = FindDestinationControl(
            target, dialog.Handle, shape.FileNameAutomationId, shape.FileNameControlType, "file-name field");
        if (field.IsFailure)
        {
            return await CancelDestinationAsync<MeituExportEvidence>(
                target, dialog, shape, field.Failure, cancellationToken).ConfigureAwait(false);
        }

        OperationResult<Unit> written = _elements.SetValue(field.Value, destination.AbsolutePath);
        if (written.IsFailure)
        {
            return await CancelDestinationAsync<MeituExportEvidence>(
                target, dialog, shape, written.Failure, cancellationToken).ConfigureAwait(false);
        }

        OperationResult<string> readBack = _elements.GetValue(field.Value);
        if (readBack.IsFailure)
        {
            return await CancelDestinationAsync<MeituExportEvidence>(
                target, dialog, shape, readBack.Failure, cancellationToken).ConfigureAwait(false);
        }

        // Case-insensitively, because Windows paths are, and the dialog is entitled to echo a
        // path back in the casing the file system uses rather than the casing PrintFlow wrote.
        if (!string.Equals(readBack.Value, destination.AbsolutePath, StringComparison.OrdinalIgnoreCase))
        {
            return await CancelDestinationAsync<MeituExportEvidence>(
                target, dialog, shape,
                OperationFailure.Create(
                    FailureCode.MeituOpenInputFailed,
                    "The destination dialog's file-name field does not read back the controlled path " +
                    "PrintFlow wrote. Nothing was confirmed, so no file was written anywhere.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["intendedPath"] = destination.AbsolutePath,
                        ["observedValue"] = readBack.Value,
                        ["confirmInvoked"] = "false",
                    }),
                cancellationToken).ConfigureAwait(false);
        }

        verified = VerifyDestinationDialog(target, dialog.Handle, shape);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<MeituExportEvidence>(verified.Failure);
        }

        OperationResult<UiElementRef> confirm = FindDestinationControl(
            target, dialog.Handle, shape.ConfirmAutomationId, shape.ConfirmControlType, "confirm control");
        if (confirm.IsFailure)
        {
            return await CancelDestinationAsync<MeituExportEvidence>(
                target, dialog, shape, confirm.Failure, cancellationToken).ConfigureAwait(false);
        }

        // §14, last chance. A stop that arrived while the confirm control was being located
        // still backs out through the signed cancel rather than confirming: the check is here,
        // immediately before the invocation, because that is the only position from which it
        // can be true that no confirm happened.
        if (StopBeforeExportConfirm(stop) is { } stoppedAtConfirm)
        {
            return await CancelDestinationAsync<MeituExportEvidence>(
                target, dialog, shape, stoppedAtConfirm, cancellationToken).ConfigureAwait(false);
        }

        // This is the irreversible export input. A cancellation observed here must win over a
        // confirm prepared earlier; D1 never issues delayed input after cancellation.
        cancellationToken.ThrowIfCancellationRequested();

        // §15. Reported before the invoke, never after. If PrintFlow dies between the two, the
        // honest record is that a write may have been confirmed — and a later Stop resolving
        // against this phase correctly refuses to claim it can cancel a filesystem write.
        stop.ReportPhase(ExternalOperationPhase.ExportConfirmed);

        OperationResult<Unit> invoked = _elements.Invoke(confirm.Value);
        if (invoked.IsFailure)
        {
            return OperationResult.Fail<MeituExportEvidence>(invoked.Failure);
        }

        OperationResult<Unit> closed = await AwaitDialogClosedAsync(
            dialog.Handle, shape, cancellationToken).ConfigureAwait(false);
        if (closed.IsFailure)
        {
            // A dialog that stays up after its confirm control was invoked is Windows asking
            // something PrintFlow has no signed answer for — most plausibly a confirm-overwrite
            // prompt for a file that was not there when the destination was checked. It is backed
            // out rather than answered, and never confirmed a second time (§12, §33).
            return await CancelDestinationAsync<MeituExportEvidence>(
                target, dialog, shape, closed.Failure, cancellationToken).ConfigureAwait(false);
        }

        return OperationResult.Ok(new MeituExportEvidence(
            confirmedBaseName, confirmedFormat, verified.Value.Title, destination.AbsolutePath));
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> DismissExportResultSurfaceAsync(
        MeituTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        OperationResult<MeituExportSignature> signature = ExportSignature();
        if (signature.IsFailure)
        {
            return OperationResult.Fail<bool>(signature.Failure);
        }

        OperationResult<ExternalWindowRef> refreshed = RefreshOwnedWindow(target);
        if (refreshed.IsFailure)
        {
            return OperationResult.Fail<bool>(refreshed.Failure);
        }

        OperationResult<IReadOnlyList<ExternalWindowRef>> dialogs =
            _locator.FindOwnedDialogs(target.Process, refreshed.Value);
        if (dialogs.IsFailure)
        {
            return OperationResult.Fail<bool>(dialogs.Failure);
        }

        foreach (ExternalWindowRef candidate in dialogs.Value)
        {
            if (candidate.OwningProcessId != target.Process.ProcessId ||
                !string.Equals(
                    candidate.ClassName, signature.Value.SurfaceClassName, StringComparison.Ordinal))
            {
                continue;
            }

            OperationResult<IReadOnlyList<string>> names =
                _elements.ReadTextSnapshot(candidate.Handle, _options.SnapshotItemLimit);
            if (names.IsFailure ||
                !MeituExportRule.ShowsResultSurface(signature.Value.Result, names.Value))
            {
                // Same class, same title, different surface. The Save surface itself reaches
                // here on a run that failed before the export; dismissing it by class alone
                // would be clicking a control PrintFlow has not identified.
                continue;
            }

            OperationResult<UiElementRef> close = FindSignedControl(
                target, candidate.Handle, signature.Value.Result.CloseControl);
            if (close.IsFailure)
            {
                return OperationResult.Fail<bool>(close.Failure);
            }

            OperationResult<Unit> invoked = _elements.Invoke(close.Value);
            if (invoked.IsFailure)
            {
                return OperationResult.Fail<bool>(invoked.Failure);
            }

            // Waited out rather than assumed gone, and it matters more than it looks. This
            // surface disables the editor while it is up, so a close attempted while it is still
            // disappearing finds a blocking modal and reports the document as still loaded — for
            // a close that then succeeds anyway. Observed live: a clean run that had exported,
            // validated and closed correctly reported a cleanup failure it had not had.
            return await AwaitSurfaceClosedAsync(candidate.Handle, cancellationToken).ConfigureAwait(false);
        }

        // Nothing to dismiss. Meitu's save-confirmation surface is a preference the operator can
        // switch off, so requiring it would make cleanup depend on a setting rather than on what
        // is actually on screen.
        return OperationResult.Ok(false);
    }

    /// <summary>Waits for a surface PrintFlow has just dismissed to actually go away.</summary>
    private async Task<OperationResult<bool>> AwaitSurfaceClosedAsync(
        WindowHandle surface, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_locator.Refresh(surface).IsFailure)
            {
                return OperationResult.Ok(true);
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<bool>(
                    FailureCode.MeituUnknownState,
                    $"Meitu's save-confirmation surface was dismissed through its signed close control " +
                    $"but was still present {_options.DialogTimeout.TotalSeconds:0} s later. The editor " +
                    "cannot be returned to a neutral state while it is up.");
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The signed export route, or a refusal when the chain vouches for none.</summary>
    private OperationResult<MeituExportSignature> ExportSignature()
    {
        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<MeituExportSignature>(baseline.Failure);
        }

        return baseline.Value.Export is { } signature
            ? OperationResult.Ok(signature)
            : OperationResult.Fail<MeituExportSignature>(
                FailureCode.MeituUnknownState,
                "The verified evidence chain carries no export signature, so PrintFlow has no signed " +
                "description of the surface it would name a destination on. Nothing was invoked and no " +
                "output was produced.");
    }

    /// <summary>
    /// Confirms PNG without opening the combo when it is already selected, otherwise follows
    /// the separately signed JPG popup route and trusts only the final read-back.
    /// </summary>
    private async Task<OperationResult<string>> EnsureExportFormatAsync(
        MeituTarget target,
        ExternalWindowRef surface,
        MeituExportSignature signature,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        OperationResult<UiElementRef> field = FindSignedControl(
            target, surface.Handle, signature.FormatControl);
        if (field.IsFailure)
        {
            return OperationResult.Fail<string>(field.Failure);
        }

        OperationResult<string> current = _elements.GetValue(field.Value);
        if (current.IsFailure)
        {
            return OperationResult.Fail<string>(current.Failure);
        }

        if (string.Equals(current.Value, signature.RequiredFormatValue, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Ok(current.Value);
        }

        if (signature.FormatSelection is not { } selection)
        {
            return OperationResult.Fail<string>(FormatSelectionFailure(
                $"The Save surface's format reads '{current.Value}', and the verified evidence chain " +
                "contains no signed popup route for changing it."));
        }

        if (!string.Equals(current.Value, selection.InitialFormatValue, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<string>(FormatSelectionFailure(
                $"The Save surface's format reads '{current.Value}', not the signed initial value " +
                $"'{selection.InitialFormatValue}'. The unknown format state is not changed."));
        }

        return await SelectExportFormatAsync(target, surface, signature, selection, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<OperationResult<string>> SelectExportFormatAsync(
        MeituTarget target,
        ExternalWindowRef surface,
        MeituExportSignature export,
        MeituExportFormatSelectionSignature selection,
        CancellationToken cancellationToken)
    {
        OperationResult<ExternalWindowRef> verified = VerifyExportSurface(
            target, surface.Handle, export);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<string>(verified.Failure);
        }

        OperationResult<IReadOnlyList<ExternalWindowRef>> before =
            _locator.FindTopLevelWindows(target.Process);
        if (before.IsFailure)
        {
            return OperationResult.Fail<string>(before.Failure);
        }

        OperationResult<IReadOnlyList<ExternalWindowRef>> unknownBefore =
            FindUnrecognisedExportSiblings(
                target,
                before.Value,
                [target.Window.Handle, surface.Handle]);
        if (unknownBefore.IsFailure)
        {
            return OperationResult.Fail<string>(unknownBefore.Failure);
        }

        if (unknownBefore.Value.Count > 0)
        {
            return OperationResult.Fail<string>(FormatSelectionFailure(
                $"The accepted Meitu process already had {unknownBefore.Value.Count} unrecognised visible " +
                "top-level window(s) before the format combo was opened. The popup route was not entered."));
        }

        OperationResult<UiElementRef> combo = FindSignedControl(
            target,
            surface.Handle,
            selection.FormatControl,
            selection.FormatControlRequiredPatterns);
        if (combo.IsFailure)
        {
            return OperationResult.Fail<string>(combo.Failure);
        }

        verified = VerifyExportSurface(target, surface.Handle, export);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<string>(verified.Failure);
        }

        combo = FindSignedControl(
            target,
            surface.Handle,
            selection.FormatControl,
            selection.FormatControlRequiredPatterns);
        if (combo.IsFailure)
        {
            return OperationResult.Fail<string>(combo.Failure);
        }

        OperationResult<Unit> opened = _elements.Invoke(combo.Value);
        if (opened.IsFailure)
        {
            return OperationResult.Fail<string>(opened.Failure);
        }

        OperationResult<ExternalWindowRef> popup = await WaitForFormatPopupAsync(
            target,
            [.. before.Value.Select(window => window.Handle)],
            selection,
            cancellationToken).ConfigureAwait(false);
        if (popup.IsFailure)
        {
            return OperationResult.Fail<string>(popup.Failure);
        }

        OperationResult<UiElementRef> item = FindFormatPopupItem(target, popup.Value, selection);
        if (item.IsFailure)
        {
            return OperationResult.Fail<string>(item.Failure);
        }

        // Re-check both windows and reacquire the item at the last possible point. The popup is
        // transient and its runtime element can be replaced without its old reference throwing.
        OperationResult<Unit> fresh = VerifyFormatPopupBoundary(
            target, surface, popup.Value, export, selection);
        if (fresh.IsFailure)
        {
            return OperationResult.Fail<string>(fresh.Failure);
        }

        item = FindFormatPopupItem(target, popup.Value, selection);
        if (item.IsFailure)
        {
            return OperationResult.Fail<string>(item.Failure);
        }

        fresh = VerifyFormatPopupBoundary(target, surface, popup.Value, export, selection);
        if (fresh.IsFailure)
        {
            return OperationResult.Fail<string>(fresh.Failure);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (selection.RequiredActivation != MeituExportFormatActivation.RuntimeDerivedClickablePoint)
        {
            return OperationResult.Fail<string>(FormatSelectionFailure(
                "The signed popup route does not require the one supported runtime-derived activation. " +
                "No item was clicked."));
        }

        OperationResult<Unit> clicked = _elements.ClickAtLiveClickablePoint(
            item.Value, target.Process, surface.Handle);
        if (clicked.IsFailure)
        {
            return OperationResult.Fail<string>(clicked.Failure);
        }

        return await WaitForSelectedFormatAsync(
            target, surface, popup.Value, export, selection, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult<ExternalWindowRef>> WaitForFormatPopupAsync(
        MeituTarget target,
        IReadOnlyCollection<WindowHandle> windowsBeforeOpen,
        MeituExportFormatSelectionSignature selection,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;
        HashSet<WindowHandle> previous = [.. windowsBeforeOpen];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OperationResult<IReadOnlyList<ExternalWindowRef>> windows =
                _locator.FindTopLevelWindows(target.Process);
            if (windows.IsFailure)
            {
                return OperationResult.Fail<ExternalWindowRef>(windows.Failure);
            }

            ExternalWindowRef[] appeared =
                [.. windows.Value.Where(window => !previous.Contains(window.Handle))];
            if (appeared.Length > 0)
            {
                if (appeared.Length != 1 || !MatchesFormatPopup(target, appeared[0], selection))
                {
                    return OperationResult.Fail<ExternalWindowRef>(FormatSelectionFailure(
                        $"Opening the signed format combo produced {appeared.Length} new top-level " +
                        "window(s), but not exactly one recognised Meitu format popup. No item was clicked."));
                }

                return OperationResult.Ok(appeared[0]);
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<ExternalWindowRef>(FormatSelectionFailure(
                    $"The signed format combo presented no recognised popup within " +
                    $"{_options.DialogTimeout.TotalSeconds:0} s. No item was clicked."));
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool MatchesFormatPopup(
        MeituTarget target,
        ExternalWindowRef popup,
        MeituExportFormatSelectionSignature selection)
    {
        if (popup.OwningProcessId != target.Process.ProcessId ||
            !popup.IsVisible || popup.IsMinimised || !popup.IsEnabled || popup.Bounds.IsEmpty ||
            !string.Equals(popup.Title, selection.PopupTitle, StringComparison.Ordinal) ||
            !string.Equals(popup.ClassName, selection.PopupWindowClassName, StringComparison.Ordinal))
        {
            return false;
        }

        OperationResult<UiElementIdentity> identity = _elements.DescribeWindow(popup.Handle);
        return identity.IsSuccess &&
            identity.Value.ProcessId == target.Process.ProcessId &&
            identity.Value.IsEnabled && !identity.Value.IsOffscreen && !identity.Value.Bounds.IsEmpty &&
            string.Equals(identity.Value.Name, selection.PopupTitle, StringComparison.Ordinal) &&
            string.Equals(identity.Value.ClassName, selection.PopupUiaClassName, StringComparison.Ordinal) &&
            string.Equals(identity.Value.ControlTypeName, selection.PopupControlType, StringComparison.Ordinal) &&
            selection.PopupRequiredPatterns.All(identity.Value.Supports);
    }

    private OperationResult<UiElementRef> FindFormatPopupItem(
        MeituTarget target,
        ExternalWindowRef popup,
        MeituExportFormatSelectionSignature selection)
    {
        OperationResult<IReadOnlyList<UiElementRef>> found = _elements.FindAll(
            popup.Handle, new UiElementQuery(UiControlKind.ListItem, Name: selection.ItemName));
        if (found.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(found.Failure);
        }

        List<UiElementRef> matches = [];
        foreach (UiElementRef candidate in found.Value)
        {
            OperationResult<UiElementIdentity> item = _elements.Describe(candidate);
            if (item.IsFailure || item.Value.ProcessId != target.Process.ProcessId ||
                !item.Value.IsEnabled || item.Value.IsOffscreen || item.Value.Bounds.IsEmpty ||
                !string.Equals(item.Value.Name, selection.ItemName, StringComparison.Ordinal) ||
                !string.Equals(item.Value.ControlTypeName, selection.ItemControlType, StringComparison.Ordinal) ||
                !string.Equals(item.Value.ClassName, selection.ItemClassName, StringComparison.Ordinal) ||
                !string.Equals(item.Value.AutomationId, selection.ItemAutomationId, StringComparison.Ordinal))
            {
                continue;
            }

            UiElementRef current = candidate;
            bool ancestryMatches = true;
            for (int depth = 1; depth <= selection.ComboAncestorDepth; depth++)
            {
                OperationResult<UiElementRef> parent = _elements.GetParent(current);
                if (parent.IsFailure)
                {
                    ancestryMatches = false;
                    break;
                }

                OperationResult<UiElementIdentity> identity = _elements.Describe(parent.Value);
                if (identity.IsFailure || identity.Value.ProcessId != target.Process.ProcessId)
                {
                    ancestryMatches = false;
                    break;
                }

                bool expected = depth == 1
                    ? string.Equals(identity.Value.ControlTypeName, selection.RequiredParentControlType, StringComparison.Ordinal) &&
                      string.Equals(identity.Value.ClassName, selection.RequiredParentClassName, StringComparison.Ordinal)
                    : depth == selection.ComboAncestorDepth &&
                      string.Equals(identity.Value.ControlTypeName, selection.RequiredComboAncestorControlType, StringComparison.Ordinal) &&
                      string.Equals(identity.Value.ClassName, selection.RequiredComboAncestorClassName, StringComparison.Ordinal);
                if (!expected)
                {
                    ancestryMatches = false;
                    break;
                }

                current = parent.Value;
            }

            if (ancestryMatches)
            {
                matches.Add(candidate);
            }
        }

        return matches.Count == 1
            ? OperationResult.Ok(matches[0])
            : OperationResult.Fail<UiElementRef>(FormatSelectionFailure(
                $"The recognised format popup contains {matches.Count} enabled, visible '{selection.ItemName}' " +
                "item(s) with the signed ancestry; exactly one is required. No item was clicked."));
    }

    private OperationResult<Unit> VerifyFormatPopupBoundary(
        MeituTarget target,
        ExternalWindowRef surface,
        ExternalWindowRef popup,
        MeituExportSignature export,
        MeituExportFormatSelectionSignature selection)
    {
        if (!_locator.IsAlive(target.Process))
        {
            return OperationResult.Fail<Unit>(FormatSelectionFailure(
                "The accepted Meitu process exited while the format popup was open."));
        }

        OperationResult<ExternalWindowRef> currentSurface = _locator.Refresh(surface.Handle);
        if (currentSurface.IsFailure || currentSurface.Value.OwningProcessId != target.Process.ProcessId ||
            !currentSurface.Value.IsVisible || currentSurface.Value.IsMinimised ||
            !currentSurface.Value.IsEnabled || currentSurface.Value.Bounds.IsEmpty ||
            !string.Equals(currentSurface.Value.Title, export.SurfaceTitle, StringComparison.Ordinal) ||
            !string.Equals(currentSurface.Value.ClassName, export.SurfaceClassName, StringComparison.Ordinal) ||
            FindSignedControl(target, surface.Handle, export.FormatControl).IsFailure ||
            FindSignedControl(
                target,
                surface.Handle,
                selection.FormatControl,
                selection.FormatControlRequiredPatterns).IsFailure)
        {
            return OperationResult.Fail<Unit>(FormatSelectionFailure(
                "The signed Save surface or format control was replaced while the popup was open."));
        }

        OperationResult<ExternalWindowRef> currentPopup = _locator.Refresh(popup.Handle);
        if (currentPopup.IsFailure || currentPopup.Value.Handle != popup.Handle ||
            !MatchesFormatPopup(target, currentPopup.Value, selection))
        {
            return OperationResult.Fail<Unit>(FormatSelectionFailure(
                "The recognised format popup disappeared or was replaced before input."));
        }

        OperationResult<ForegroundIdentity> foreground = _locator.ReadForeground();
        if (foreground.IsFailure)
        {
            return OperationResult.Fail<Unit>(foreground.Failure);
        }

        return selection.SaveSurfaceMustRemainForeground &&
               foreground.Value.Handle == surface.Handle &&
               foreground.Value.ProcessId == target.Process.ProcessId
            ? OperationResult.Ok()
            : OperationResult.Fail<Unit>(FormatSelectionFailure(
                "The signed Save surface is no longer the exact same-process foreground window while " +
                "the recognised non-activating format popup is open. No item was clicked."));
    }

    private async Task<OperationResult<string>> WaitForSelectedFormatAsync(
        MeituTarget target,
        ExternalWindowRef surface,
        ExternalWindowRef popup,
        MeituExportSignature export,
        MeituExportFormatSelectionSignature selection,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;
        string observed = selection.InitialFormatValue;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OperationResult<IReadOnlyList<ExternalWindowRef>> windows =
                _locator.FindTopLevelWindows(target.Process);
            if (windows.IsFailure)
            {
                return OperationResult.Fail<string>(FormatSelectionFailure(
                    "The Meitu window set could not be read after the png item was clicked; popup " +
                    "disappearance is therefore unknown."));
            }

            OperationResult<IReadOnlyList<ExternalWindowRef>> unknown =
                FindUnrecognisedExportSiblings(
                    target,
                    windows.Value,
                    [target.Window.Handle, surface.Handle, popup.Handle]);
            if (unknown.IsFailure)
            {
                return OperationResult.Fail<string>(unknown.Failure);
            }

            if (unknown.Value.Count > 0)
            {
                return OperationResult.Fail<string>(FormatSelectionFailure(
                    $"The accepted Meitu process presented {unknown.Value.Count} unrecognised visible " +
                    "top-level window(s) after the png item was clicked."));
            }

            ExternalWindowRef[] currentPopup =
                [.. windows.Value.Where(window => window.Handle == popup.Handle)];
            if (currentPopup.Length > 1 ||
                currentPopup.Length == 1 && !MatchesFormatPopup(target, currentPopup[0], selection))
            {
                return OperationResult.Fail<string>(FormatSelectionFailure(
                    "The recognised format popup was replaced while its selection was settling."));
            }

            bool popupGone = currentPopup.Length == 0;
            if (!selection.PopupMustDisappear || popupGone)
            {
                OperationResult<ExternalWindowRef> verified = VerifyExportSurface(
                    target, surface.Handle, export);
                if (verified.IsSuccess)
                {
                    OperationResult<UiElementRef> field = FindSignedControl(
                        target, surface.Handle, export.FormatControl);
                    if (field.IsSuccess)
                    {
                        OperationResult<string> value = _elements.GetValue(field.Value);
                        if (value.IsSuccess)
                        {
                            observed = value.Value;
                            if (string.Equals(
                                    observed, selection.RequiredFormatValue,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                return OperationResult.Ok(observed);
                            }
                        }
                    }
                }
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<string>(FormatSelectionFailure(
                    $"The recognised png item was clicked, but the popup did not disappear and the " +
                    $"Save format did not freshly read '{selection.RequiredFormatValue}' within " +
                    $"{_options.DialogTimeout.TotalSeconds:0} s (last read '{observed}')."));
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private static OperationFailure FormatSelectionFailure(string detail) =>
        OperationFailure.Create(
            FailureCode.MeituOpenInputFailed,
            detail + " Neither Save nor Save As was invoked, so no file was written.",
            isRetryable: false,
            context: new Dictionary<string, string>
            {
                ["control"] = "format",
                ["exportInvoked"] = "false",
                ["inputRoute"] = "signed-format-popup",
            });

    /// <summary>
    /// Returns visible same-process windows that are neither part of the active export route nor
    /// the independently signed Meitu start page.
    /// </summary>
    /// <remarks>
    /// Meitu can retain its start-page window after opening the separate editor window. That
    /// sibling is an ordinary, positively recognised application surface, not a popup raised by
    /// the format combo. It is accepted only after a fresh read of the full signed welcome-page
    /// rule; an unreadable, minimised, empty, changed or otherwise unknown sibling is still
    /// returned and keeps the export fail-closed. The welcome page may be disabled by Meitu's
    /// application-modal Save surface; that does not make it an input target, and every other
    /// visible top-level window is still judged independently.
    ///
    /// Owned-dialog titles are intentionally empty in the candidate observation. Every visible
    /// top-level window is already present in <paramref name="windows"/> and judged separately.
    /// Feeding the export surface into the generic owned-dialog query—or feeding the disabled
    /// bit caused by that exact surface into the generic classifier—would label the unrelated
    /// start-page sibling modal merely because both windows belong to the same process.
    /// </remarks>
    private OperationResult<IReadOnlyList<ExternalWindowRef>> FindUnrecognisedExportSiblings(
        MeituTarget target,
        IReadOnlyList<ExternalWindowRef> windows,
        IReadOnlyCollection<WindowHandle> routeHandles)
    {
        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<IReadOnlyList<ExternalWindowRef>>(baseline.Failure);
        }

        List<ExternalWindowRef> unknown = [];
        foreach (ExternalWindowRef candidate in windows)
        {
            if (routeHandles.Contains(candidate.Handle))
            {
                continue;
            }

            bool eligible = candidate.OwningProcessId == target.Process.ProcessId &&
                candidate.IsVisible && !candidate.IsMinimised && !candidate.Bounds.IsEmpty;
            OperationResult<IReadOnlyList<string>> texts = eligible
                ? _elements.ReadTextSnapshot(candidate.Handle, _options.SnapshotItemLimit)
                : OperationResult.Fail<IReadOnlyList<string>>(
                    FailureCode.MeituUnknownState, "The sibling window is not readable as a welcome page.");

            bool recognisedWelcome = texts.IsSuccess &&
                MeituStateClassifier.Classify(
                    baseline.Value,
                    new MeituObservation(
                        candidate.Title,
                        [.. texts.Value],
                        [],
                        MainWindowEnabled: true,
                        ExpectedWorkingCopyFileName: null,
                        ObservedDocumentIdentity: null)).State == MeituStartingState.KnownWelcome;

            if (!recognisedWelcome)
            {
                unknown.Add(candidate);
            }
        }

        return OperationResult.Ok<IReadOnlyList<ExternalWindowRef>>(unknown);
    }

    /// <summary>
    /// Writes one signed control's value and confirms it reads back exactly (§8).
    /// </summary>
    private async Task<OperationResult<string>> SetAndReadBackAsync(
        MeituTarget target,
        WindowHandle surface,
        MeituControlSignature control,
        string intended,
        StringComparison comparison,
        string description,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        OperationResult<UiElementRef> field = FindSignedControl(target, surface, control);
        if (field.IsFailure)
        {
            return OperationResult.Fail<string>(field.Failure);
        }

        OperationResult<Unit> written = _elements.SetValue(field.Value, intended);
        if (written.IsFailure)
        {
            return OperationResult.Fail<string>(written.Failure);
        }

        OperationResult<string> readBack = _elements.GetValue(field.Value);
        if (readBack.IsFailure)
        {
            return OperationResult.Fail<string>(readBack.Failure);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return string.Equals(readBack.Value, intended, comparison)
            ? OperationResult.Ok(readBack.Value)
            : OperationResult.Fail<string>(OperationFailure.Create(
                FailureCode.MeituOpenInputFailed,
                $"The Save surface's {description} reads '{readBack.Value}' after PrintFlow wrote " +
                $"'{intended}'. Neither Save nor Save As was invoked, so no file was written.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["control"] = description,
                    ["intended"] = intended,
                    ["observed"] = readBack.Value,
                    ["exportInvoked"] = "false",
                }));
    }

    /// <summary>
    /// Waits for the owned Save surface, identified by its signed shape <b>and</b> its contents.
    /// </summary>
    /// <remarks>
    /// The contents check is not redundant. Meitu's save surface and its post-save confirmation
    /// surface share a window class and a window title on this build, so a wait that matched on
    /// those alone would accept whichever happened to be up — including the confirmation panel
    /// left over from a previous export. What tells them apart is that only one of them carries
    /// the signed file-name field.
    /// </remarks>
    private async Task<OperationResult<ExternalWindowRef>> WaitForExportSurfaceAsync(
        MeituTarget target, MeituExportSignature signature, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<IReadOnlyList<ExternalWindowRef>> dialogs =
                _locator.FindOwnedDialogs(target.Process, target.Window);
            if (dialogs.IsFailure)
            {
                return OperationResult.Fail<ExternalWindowRef>(dialogs.Failure);
            }

            ExternalWindowRef[] matches = [.. dialogs.Value.Where(candidate =>
                candidate.OwningProcessId == target.Process.ProcessId &&
                string.Equals(candidate.Title, signature.SurfaceTitle, StringComparison.Ordinal) &&
                string.Equals(candidate.ClassName, signature.SurfaceClassName, StringComparison.Ordinal) &&
                FindSignedControl(target, candidate.Handle, signature.FileNameControl).IsSuccess)];

            if (matches.Length == 1)
            {
                return OperationResult.Ok(matches[0]);
            }

            if (matches.Length > 1)
            {
                return OperationResult.Fail<ExternalWindowRef>(
                    FailureCode.MeituUnknownState,
                    $"{matches.Length} owned Save surfaces carry the signed export shape; PrintFlow will " +
                    "not choose between them and nothing was written or invoked.");
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<ExternalWindowRef>(
                    FailureCode.MeituOpenInputFailed,
                    $"The structurally verified Save control did not present the signed export surface " +
                    $"'{signature.SurfaceTitle}'/{signature.SurfaceClassName} carrying its file-name field " +
                    $"within {_options.DialogTimeout.TotalSeconds:0} s. No export was attempted and no " +
                    "output was produced.");
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Waits for the signed destination dialog, owned by the verified process.</summary>
    private async Task<OperationResult<ExternalWindowRef>> WaitForDestinationDialogAsync(
        MeituTarget target, MeituExportDestinationSignature shape, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<IReadOnlyList<ExternalWindowRef>> windows =
                _locator.FindTopLevelWindows(target.Process);
            if (windows.IsFailure)
            {
                return OperationResult.Fail<ExternalWindowRef>(windows.Failure);
            }

            ExternalWindowRef[] matches = [.. windows.Value.Where(candidate =>
                candidate.OwningProcessId == target.Process.ProcessId &&
                string.Equals(candidate.ClassName, shape.WindowClassName, StringComparison.Ordinal) &&
                shape.AcceptedTitles.Contains(candidate.Title, StringComparer.Ordinal))];

            if (matches.Length == 1)
            {
                return OperationResult.Ok(matches[0]);
            }

            if (matches.Length > 1)
            {
                return OperationResult.Fail<ExternalWindowRef>(
                    FailureCode.MeituUnknownState,
                    $"{matches.Length} owned destination dialogs match the signed identity; PrintFlow will " +
                    "not choose between them and nothing was written or invoked.");
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<ExternalWindowRef>(
                    FailureCode.MeituOpenInputFailed,
                    $"The signed Save As control did not present the destination dialog " +
                    $"{shape.WindowClassName} within {_options.DialogTimeout.TotalSeconds:0} s, so " +
                    "PrintFlow never named a destination. No file was written to a controlled path.");
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Re-establishes that the export surface is still Meitu's and still in front.</summary>
    private OperationResult<ExternalWindowRef> VerifyExportSurface(
        MeituTarget target, WindowHandle surface, MeituExportSignature signature) =>
        VerifyOwnedSurface(
            target, surface, signature.SurfaceClassName,
            title => string.Equals(title, signature.SurfaceTitle, StringComparison.Ordinal),
            "Save surface");

    /// <summary>Re-establishes that the destination dialog is still Meitu's and still in front.</summary>
    private OperationResult<ExternalWindowRef> VerifyDestinationDialog(
        MeituTarget target, WindowHandle dialog, MeituExportDestinationSignature shape) =>
        VerifyOwnedSurface(
            target, dialog, shape.WindowClassName,
            title => shape.AcceptedTitles.Contains(title, StringComparer.Ordinal),
            "destination dialog");

    /// <summary>
    /// The guard both export surfaces share: process alive, window still itself, still owned,
    /// and the exact foreground (Epic 11300 Part B2B §12).
    /// </summary>
    /// <remarks>
    /// The exact-handle foreground requirement is stronger than the process-level one the open
    /// picker uses, and it is the right one here. A value written through a pattern cannot be
    /// redirected by focus, but the confirm control on this dialog is the single irreversible
    /// action of the whole slice, and requiring the dialog itself to be the foreground window
    /// means PrintFlow never presses it against a surface something else has covered.
    /// </remarks>
    private OperationResult<ExternalWindowRef> VerifyOwnedSurface(
        MeituTarget target,
        WindowHandle surface,
        string expectedClassName,
        Func<string, bool> titleAccepted,
        string description)
    {
        if (!_locator.IsAlive(target.Process))
        {
            return OperationResult.Fail<ExternalWindowRef>(OperationFailure.Create(
                FailureCode.MeituTargetLost,
                $"Meitu process {target.Process.ProcessId} exited while its {description} was open; " +
                "nothing further was written or invoked.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["targetLoss"] = "process-exited",
                    ["inputSent"] = "false",
                    ["retainedExternalState"] = "gone",
                },
                messageKey: "Failure_MeituClosed"));
        }

        OperationResult<ExternalWindowRef> refreshed = _locator.Refresh(surface);
        if (refreshed.IsFailure ||
            refreshed.Value.OwningProcessId != target.Process.ProcessId ||
            !string.Equals(refreshed.Value.ClassName, expectedClassName, StringComparison.Ordinal) ||
            !titleAccepted(refreshed.Value.Title))
        {
            return OperationResult.Fail<ExternalWindowRef>(
                FailureCode.MeituTargetLost,
                $"The {description} no longer has the signed title, class and verified Meitu owner; " +
                "nothing further was written or invoked.");
        }

        OperationResult<ForegroundIdentity> foreground = _locator.ReadForeground();
        if (foreground.IsFailure)
        {
            return OperationResult.Fail<ExternalWindowRef>(foreground.Failure);
        }

        return foreground.Value.Handle == surface && foreground.Value.ProcessId == target.Process.ProcessId
            ? refreshed
            : OperationResult.Fail<ExternalWindowRef>(TargetLost(
                surface, foreground.Value,
                $"The signed {description} is not the exact foreground window; nothing further was " +
                "written or invoked."));
    }

    /// <summary>
    /// Locates one destination-dialog control by the automation id and control type the signed
    /// evidence records, requiring the match to be unique.
    /// </summary>
    /// <remarks>
    /// The same shape as the open picker's resolver, and it exists separately for the same
    /// reason that one does: an automation id is not unique inside a Windows common dialog. The
    /// save dialog's file-name field is an <c>Edit</c> nested in a <c>ComboBox</c> and both
    /// report the same id, so the control type is what selects between them — and the selection
    /// has to be the only one before a path is written into it.
    /// </remarks>
    private OperationResult<UiElementRef> FindDestinationControl(
        MeituTarget target,
        WindowHandle dialog,
        string automationId,
        string controlTypeName,
        string description)
    {
        OperationResult<IReadOnlyList<UiElementRef>> found = _elements.FindAll(
            dialog, new UiElementQuery(UiControlKind.Any, AutomationId: automationId));
        if (found.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(found.Failure);
        }

        List<UiElementRef> matches = [];
        foreach (UiElementRef candidate in found.Value)
        {
            OperationResult<UiElementIdentity> identity = _elements.Describe(candidate);
            if (identity.IsFailure ||
                !string.Equals(identity.Value.ControlTypeName, controlTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (identity.Value.ProcessId != target.Process.ProcessId)
            {
                return OperationResult.Fail<UiElementRef>(
                    FailureCode.MeituTargetLost,
                    $"The destination dialog's {description} belongs to process " +
                    $"{identity.Value.ProcessId}, not the verified Meitu process " +
                    $"{target.Process.ProcessId}. Nothing was written or invoked.");
            }

            matches.Add(candidate);
        }

        return matches.Count == 1
            ? OperationResult.Ok(matches[0])
            : OperationResult.Fail<UiElementRef>(
                FailureCode.MeituOpenInputFailed,
                $"The destination dialog has {matches.Count} control(s) with automation id " +
                $"'{automationId}' of type {controlTypeName}, and the signed evidence describes exactly " +
                "one. Nothing was written or invoked.");
    }

    /// <summary>Waits for a dialog PrintFlow has just confirmed to go away.</summary>
    private async Task<OperationResult<Unit>> AwaitDialogClosedAsync(
        WindowHandle dialog, MeituExportDestinationSignature shape, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<ExternalWindowRef> refreshed = _locator.Refresh(dialog);
            if (refreshed.IsFailure ||
                !string.Equals(refreshed.Value.ClassName, shape.WindowClassName, StringComparison.Ordinal))
            {
                // Gone, or no longer the dialog PrintFlow confirmed. Either way this wait is
                // over — and it says nothing at all about whether a file exists, which the
                // caller establishes against the file system (§13).
                return OperationResult.Ok();
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<Unit>(OperationFailure.Create(
                    FailureCode.MeituOpenInputFailed,
                    $"The destination dialog was confirmed once but was still open " +
                    $"{_options.DialogTimeout.TotalSeconds:0} s later. Windows is asking something " +
                    "PrintFlow has no signed answer for — most plausibly a confirm-overwrite prompt. " +
                    "The confirm control was not invoked again.",
                    isRetryable: false,
                    context: new Dictionary<string, string>
                    {
                        ["dialogTitle"] = refreshed.Value.Title,
                        ["confirmInvoked"] = "once",
                    }));
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Backs out of the Save surface through its signed cancel control, writing nothing.</summary>
    /// <remarks>
    /// The identity probe's cancel control is reused deliberately: it is the same surface and the
    /// same control, already signed, already the only action Part B1.1 permits on it. A second
    /// signature for the same button would be a second thing to keep in step with the evidence.
    /// </remarks>
    private async Task<OperationResult<T>> CancelExportSurfaceAsync<T>(
        MeituTarget target,
        ExternalWindowRef surface,
        OperationFailure failure,
        CancellationToken cancellationToken)
    {
        OperationResult<MeituDocumentIdentitySignature> identity = DocumentIdentitySignature();
        if (identity.IsSuccess)
        {
            // Best effort, and the original failure is what survives either way: a screen that
            // could not be tidied must not become the reported problem when the reported problem
            // is that PrintFlow refused to export (§26).
            await CancelIdentityDialogAsync(target, surface, identity.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        return OperationResult.Fail<T>(failure);
    }

    /// <summary>Backs out of the destination dialog through its signed cancel control.</summary>
    private async Task<OperationResult<T>> CancelDestinationAsync<T>(
        MeituTarget target,
        ExternalWindowRef dialog,
        MeituExportDestinationSignature shape,
        OperationFailure failure,
        CancellationToken cancellationToken)
    {
        if (_locator.Refresh(dialog.Handle).IsSuccess)
        {
            OperationResult<UiElementRef> cancel = FindDestinationControl(
                target, dialog.Handle, shape.CancelAutomationId, shape.CancelControlType, "cancel control");
            if (cancel.IsSuccess)
            {
                _elements.Invoke(cancel.Value);
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return OperationResult.Fail<T>(failure);
    }

    private static OperationFailure TargetLost(
        WindowHandle expected, ForegroundIdentity actual, string message) =>
        OperationFailure.Create(
            FailureCode.MeituTargetLost,
            $"{message} Expected {expected}; foreground is {actual.Handle} owned by " +
            $"'{actual.ProcessName}' (process {actual.ProcessId}).",
            isRetryable: true,
            context: new Dictionary<string, string>
            {
                ["expectedWindow"] = expected.ToString(),
                ["actualWindow"] = actual.Handle.ToString(),
                ["actualProcess"] = actual.ProcessName,
                ["actualProcessId"] = actual.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["inputSent"] = "false",
            });
}
