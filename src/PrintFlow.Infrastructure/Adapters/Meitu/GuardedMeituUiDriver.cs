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

        OperationResult<UiElementQuery> query = QueryFor(element);
        return query.IsFailure
            ? OperationResult.Fail<UiElementRef>(query.Failure)
            : _elements.Find(window.Value.Handle, query.Value);
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
    public async Task<OperationResult<Unit>> OpenWorkingCopyAsync(
        MeituTarget target, string workingCopyAbsolutePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingCopyAbsolutePath);

        OperationResult<MeituTarget> verified = await VerifyTargetAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<Unit>(verified.Failure);
        }

        OperationResult<Unit> requested = await RequestOpenDialogAsync(verified.Value, cancellationToken)
            .ConfigureAwait(false);
        if (requested.IsFailure)
        {
            return requested;
        }

        OperationResult<ExternalWindowRef> dialog = await WaitForFileDialogAsync(verified.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dialog.IsFailure)
        {
            return OperationResult.Fail<Unit>(dialog.Failure);
        }

        return FillAndConfirmDialog(verified.Value, dialog.Value, workingCopyAbsolutePath);
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

    /// <summary>Asks Meitu to show its Open dialog, preferring a named element to a keystroke.</summary>
    private async Task<OperationResult<Unit>> RequestOpenDialogAsync(
        MeituTarget target, CancellationToken cancellationToken)
    {
        // Priority 1 (§4): a Windows UI Automation element. It needs no coordinate and no
        // keystroke, so it cannot land anywhere but the control it names.
        OperationResult<UiElementRef> entry = FindKnownElement(target, KnownMeituElement.WelcomeOpenEntry);
        if (entry.IsSuccess)
        {
            OperationResult<Unit> invoked = await InvokeKnownElementAsync(
                target, KnownMeituElement.WelcomeOpenEntry, cancellationToken).ConfigureAwait(false);

            if (invoked.IsSuccess)
            {
                return invoked;
            }

            // A lost target means something changed underneath PrintFlow; that is a stop, not a
            // reason to try harder. Any other invoke failure means the control exposed no
            // automation pattern, so nothing happened at all — and falling through to the
            // shortcut cannot double-act.
            if (invoked.Failure.Code == FailureCode.MeituTargetLost)
            {
                return invoked;
            }
        }

        // Priority 2 (§4): a stable shortcut, and only after the target window has been
        // verified — which SendVerifiedShortcutAsync does, twice.
        return await SendVerifiedShortcutAsync(target, KnownShortcut.OpenFile, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a file dialog that belongs to the verified Meitu process and carries the
    /// Windows common-dialog class.
    /// </summary>
    private async Task<OperationResult<ExternalWindowRef>> WaitForFileDialogAsync(
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
                return OperationResult.Fail<ExternalWindowRef>(windows.Failure);
            }

            foreach (ExternalWindowRef candidate in windows.Value)
            {
                // Both conditions, always: the dialog must be a Windows common dialog *and*
                // owned by the exact Meitu process. A dialog that merely looks right, in some
                // other process, is not touched (§17).
                if (candidate.OwningProcessId == target.Process.ProcessId &&
                    string.Equals(candidate.ClassName, _options.FileDialogClassName, StringComparison.Ordinal))
                {
                    return OperationResult.Ok(candidate);
                }
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<ExternalWindowRef>(
                    FailureCode.MeituOpenInputFailed,
                    $"No file dialog owned by Meitu process {target.Process.ProcessId} appeared within " +
                    $"{_options.DialogTimeout.TotalSeconds:0} s; nothing was typed.");
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Writes the path into the verified dialog's field and confirms it.</summary>
    private OperationResult<Unit> FillAndConfirmDialog(
        MeituTarget target, ExternalWindowRef dialog, string workingCopyAbsolutePath)
    {
        OperationResult<UiElementRef> field = _elements.Find(
            dialog.Handle,
            new UiElementQuery(UiControlKind.Edit, AutomationId: _options.FileDialogFileNameAutomationId));
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

        // Re-read the dialog before confirming: if it has closed or been replaced since the
        // field was written, the Open button found under the old handle is not this dialog's.
        OperationResult<ExternalWindowRef> stillOpen = _locator.Refresh(dialog.Handle);
        if (stillOpen.IsFailure || stillOpen.Value.OwningProcessId != target.Process.ProcessId)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituTargetLost,
                "The file dialog stopped belonging to the verified Meitu process before it was confirmed.");
        }

        OperationResult<UiElementRef> confirm = _elements.Find(
            dialog.Handle,
            new UiElementQuery(UiControlKind.Button, AutomationId: _options.FileDialogOpenButtonAutomationId));
        return confirm.IsFailure
            ? OperationResult.Fail<Unit>(confirm.Failure)
            : _elements.Invoke(confirm.Value);
    }

    /// <summary>Resolves a named element to the query that finds it.</summary>
    private OperationResult<UiElementQuery> QueryFor(KnownMeituElement element)
    {
        switch (element)
        {
            case KnownMeituElement.WelcomeOpenEntry:
            {
                OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
                if (baseline.IsFailure)
                {
                    return OperationResult.Fail<UiElementQuery>(baseline.Failure);
                }

                // The configured entry name must be one the signed clean-start evidence actually
                // records. Without this check the option would be a back door for naming any
                // control at all — which is what §2 rules out.
                if (!baseline.Value.WelcomeMarkers.Contains(_options.WelcomeOpenEntryName, StringComparer.Ordinal))
                {
                    return OperationResult.Fail<UiElementQuery>(
                        FailureCode.MeituUnknownState,
                        $"'{_options.WelcomeOpenEntryName}' is not one of the {baseline.Value.WelcomeMarkers.Length} " +
                        "markers the signed clean-start evidence records, so PrintFlow will not look for it.");
                }

                return OperationResult.Ok(
                    new UiElementQuery(UiControlKind.Any, Name: _options.WelcomeOpenEntryName));
            }

            case KnownMeituElement.FileDialogFileName:
                return OperationResult.Ok(new UiElementQuery(
                    UiControlKind.Edit, AutomationId: _options.FileDialogFileNameAutomationId));

            case KnownMeituElement.FileDialogOpenButton:
                return OperationResult.Ok(new UiElementQuery(
                    UiControlKind.Button, AutomationId: _options.FileDialogOpenButtonAutomationId));

            default:
                return OperationResult.Fail<UiElementQuery>(
                    FailureCode.PreconditionNotMet, $"No query is defined for element '{element}'.");
        }
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
