using System.Collections.Immutable;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

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
    public async Task<OperationResult<MeituStateSnapshot>> InspectStateAsync(
        MeituTarget target, string? expectedWorkingCopyFileName, CancellationToken cancellationToken)
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
            expectedWorkingCopyFileName);

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
            _ => FindDialogControl(target, window.Value.Handle, element),
        };
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
        MeituTarget target, WindowHandle window, MeituControlSignature signature)
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

        if (!_locator.IsAlive(target.Process))
        {
            return OperationResult.Fail<MeituTarget>(
                FailureCode.MeituTargetLost,
                $"Meitu process {target.Process.ProcessId} is no longer running; no input was sent.");
        }

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
        OperationResult<ExternalWindowRef> refreshed = _locator.Refresh(target.Window.Handle);
        if (refreshed.IsFailure)
        {
            return refreshed;
        }

        if (refreshed.Value.OwningProcessId != target.Process.ProcessId)
        {
            // A handle can be reused by an entirely different application after its original
            // window is destroyed. Re-checking ownership is what stops PrintFlow addressing the
            // successor as though it were Meitu.
            return OperationResult.Fail<ExternalWindowRef>(
                FailureCode.MeituTargetLost,
                $"Window {target.Window.Handle} now belongs to process " +
                $"{refreshed.Value.OwningProcessId}, not the verified Meitu process " +
                $"{target.Process.ProcessId}.");
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
