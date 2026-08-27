using System.Collections.Immutable;
using System.Globalization;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// The only class in the solution that produces Photoshop input (Epic 11400 Part A §4, §6, §9).
/// </summary>
/// <remarks>
/// Every route below follows the same order, and the order is the safety argument rather than a
/// convention: re-verify the process and window, re-acquire and re-verify the foreground,
/// establish that the surface being driven is owned by the verified process and carries the
/// signed shape, act once, and check what happened. No step is skipped because an earlier call
/// already checked — a window handle stays valid-looking after the window it named is gone, and
/// the foreground can change between any two instructions.
///
/// What this class cannot do is as important as what it does. It runs no Photoshop Action,
/// resizes nothing, converts no colour mode and saves nothing; the Save As surface it raises is
/// read and cancelled, and there is no code path in it that presses that surface's Save control
/// (§13, §14, §15).
///
/// Failure codes from the shared automation seam are translated into the Photoshop vocabulary at
/// this boundary. The shared primitives cannot know which application they are serving, and an
/// operator reading <c>MeituTargetLost</c> after a Photoshop step would be told something
/// untrue (§18).
/// </remarks>
public sealed class GuardedPhotoshopUiDriver : IPhotoshopUiDriver
{
    private readonly IExternalAppWindowLocator _locator;
    private readonly IVerifiedControlSink _controls;
    private readonly IScopedInputSink _input;
    private readonly IAutomationEvidenceSink _evidence;
    private readonly IPhotoshopBaselineProvider _baselines;
    private readonly PhotoshopAutomationOptions _options;
    private readonly TimeProvider _clock;

    public GuardedPhotoshopUiDriver(
        IExternalAppWindowLocator locator,
        IVerifiedControlSink controls,
        IScopedInputSink input,
        IAutomationEvidenceSink evidence,
        IPhotoshopBaselineProvider baselines,
        PhotoshopAutomationOptions options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(baselines);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _locator = locator;
        _controls = controls;
        _input = input;
        _evidence = evidence;
        _baselines = baselines;
        _options = options;
        _clock = clock;
    }

    // -----------------------------------------------------------------------------------
    // Observation
    // -----------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<OperationResult<PhotoshopStateSnapshot>> InspectStateAsync(
        PhotoshopTarget target, string? expectedDocumentFileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        OperationResult<PhotoshopBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<PhotoshopStateSnapshot>(baseline.Failure);
        }

        OperationResult<PhotoshopTarget> verified = await VerifyTargetAsync(target).ConfigureAwait(false);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<PhotoshopStateSnapshot>(verified.Failure);
        }

        OperationResult<PhotoshopObservation> observation =
            Observe(verified.Value, expectedDocumentFileName, observedDocumentFullPath: null);
        return observation.IsFailure
            ? OperationResult.Fail<PhotoshopStateSnapshot>(observation.Failure)
            : OperationResult.Ok(PhotoshopStateClassifier.Classify(baseline.Value, observation.Value));
    }

    /// <summary>Reads one instant of the verified window, without touching anything.</summary>
    private OperationResult<PhotoshopObservation> Observe(
        PhotoshopTarget target, string? expectedDocumentFileName, string? observedDocumentFullPath)
    {
        OperationResult<IReadOnlyList<string>> classes = _controls.LocateVisibleClasses(
            target.Process, target.Window.Handle, _options.ClassSnapshotLimit);
        if (classes.IsFailure)
        {
            return OperationResult.Fail<PhotoshopObservation>(AsPhotoshop(classes.Failure));
        }

        OperationResult<IReadOnlyList<ExternalWindowRef>> dialogs =
            _locator.FindOwnedDialogs(target.Process, target.Window);
        if (dialogs.IsFailure)
        {
            return OperationResult.Fail<PhotoshopObservation>(AsPhotoshop(dialogs.Failure));
        }

        return OperationResult.Ok(new PhotoshopObservation(
            target.Window.Title,
            [.. classes.Value],
            [.. TitledDialogs(dialogs.Value)],
            target.Window.IsEnabled,
            expectedDocumentFileName,
            observedDocumentFullPath));
    }

    /// <summary>
    /// The owned windows that are actually dialogs, rather than Photoshop's own floating chrome.
    /// </summary>
    /// <remarks>
    /// Photoshop keeps <c>OWL.ShadowView</c> and the <c>OWL.Dock</c> it owns as owned top-level
    /// windows. They become visible whenever the frame is restored from minimised and carry no
    /// title at all, so counting every owned visible window as a modal reported a blocking
    /// dialog over a perfectly ordinary editor. A dialog an operator is expected to resolve has
    /// a title; this keeps the refusal pointed at those.
    /// </remarks>
    private static IEnumerable<string> TitledDialogs(IEnumerable<ExternalWindowRef> dialogs) =>
        dialogs.Select(dialog => dialog.Title).Where(title => !string.IsNullOrWhiteSpace(title));

    // -----------------------------------------------------------------------------------
    // Activation
    // -----------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<OperationResult<PhotoshopTarget>> ActivateAsync(
        PhotoshopTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        OperationResult<PhotoshopTarget> verified = await VerifyTargetAsync(target).ConfigureAwait(false);
        if (verified.IsFailure)
        {
            return verified;
        }

        OperationResult<Unit> activated = _locator.Activate(verified.Value.Window);
        if (activated.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(AsPhotoshop(activated.Failure));
        }

        DateTimeOffset deadline = _clock.GetUtcNow() + _options.ActivationTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<ForegroundIdentity> foreground = _locator.ReadForeground();
            if (foreground.IsSuccess && foreground.Value.Handle == verified.Value.Window.Handle)
            {
                return verified;
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                // Windows may refuse a foreground change and PrintFlow may not force one. The
                // honest report is that the target is not addressable right now — never an
                // attempt to send anyway (§16).
                string holder = foreground.IsSuccess ? foreground.Value.ProcessName : "(unreadable)";
                return OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                    FailureCode.PhotoshopTargetLost,
                    $"Photoshop did not take the foreground within {_options.ActivationTimeout.TotalSeconds:0}s; " +
                    $"'{holder}' holds it. No input was produced.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["foregroundProcess"] = holder,
                        ["inputSent"] = "false",
                    }));
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    // -----------------------------------------------------------------------------------
    // Open
    // -----------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<OperationResult<PhotoshopTarget>> OpenManagedDocumentAsync(
        PhotoshopTarget target, string managedAbsolutePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedAbsolutePath);

        OperationResult<PhotoshopBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(baseline.Failure);
        }

        if (baseline.Value.OpenDialog is not { } signature)
        {
            return OperationResult.Fail<PhotoshopTarget>(
                FailureCode.PhotoshopUnknownState,
                "The verified evidence chain carries no signature for the Photoshop Open dialog, so " +
                "PrintFlow has no positively identified control to hand a file to. Nothing was typed " +
                "and nothing was opened.");
        }

        OperationResult<PhotoshopTarget> ready = await ActivateAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready;
        }

        OperationResult<Unit> raised = SendGuarded(ready.Value, KnownShortcut.OpenFile);
        if (raised.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(raised.Failure);
        }

        OperationResult<ExternalWindowRef> dialog = await WaitForOwnedDialogAsync(
            ready.Value, signature.WindowClassName, signature.Title, _options.DialogTimeout,
            cancellationToken).ConfigureAwait(false);
        if (dialog.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(dialog.Failure);
        }

        OperationResult<Unit> driven = await DriveOpenDialogAsync(
            ready.Value, dialog.Value, signature, managedAbsolutePath, cancellationToken)
            .ConfigureAwait(false);
        if (driven.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(driven.Failure);
        }

        // The dialog closing is not the outcome; it is only permission to look. What the caller
        // gets back is a re-verified target, and whether the right document is loaded is decided
        // afterwards by the identity probe — never here (§11, §18).
        return await VerifyTargetAsync(ready.Value).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the exact path into the signed field, reads it back, and presses Open once.
    /// </summary>
    private async Task<OperationResult<Unit>> DriveOpenDialogAsync(
        PhotoshopTarget target,
        ExternalWindowRef dialog,
        PhotoshopOpenDialogSignature signature,
        string managedAbsolutePath,
        CancellationToken cancellationToken)
    {
        OperationResult<VerifiedControlRef> fileName = _controls.Locate(
            target.Process, dialog.Handle, signature.FileNameControlId, signature.FileNameControlClass);
        if (fileName.IsFailure)
        {
            await CancelDialogAsync(
                target, dialog, signature.CancelControlId, signature.CancelControlClass, cancellationToken)
                .ConfigureAwait(false);
            return OperationResult.Fail<Unit>(AsPhotoshop(fileName.Failure));
        }

        OperationResult<VerifiedControlRef> confirm = _controls.Locate(
            target.Process, dialog.Handle, signature.ConfirmControlId, signature.ConfirmControlClass);
        if (confirm.IsFailure)
        {
            await CancelDialogAsync(
                target, dialog, signature.CancelControlId, signature.CancelControlClass, cancellationToken)
                .ConfigureAwait(false);
            return OperationResult.Fail<Unit>(AsPhotoshop(confirm.Failure));
        }

        OperationResult<Unit> written = _controls.WriteText(target.Process, fileName.Value, managedAbsolutePath);
        if (written.IsFailure)
        {
            await CancelDialogAsync(
                target, dialog, signature.CancelControlId, signature.CancelControlClass, cancellationToken)
                .ConfigureAwait(false);
            return OperationResult.Fail<Unit>(AsPhotoshop(written.Failure));
        }

        OperationResult<string> readBack = _controls.ReadText(target.Process, fileName.Value);
        if (readBack.IsFailure)
        {
            await CancelDialogAsync(
                target, dialog, signature.CancelControlId, signature.CancelControlClass, cancellationToken)
                .ConfigureAwait(false);
            return OperationResult.Fail<Unit>(AsPhotoshop(readBack.Failure));
        }

        // Exact, ordinal, whole-string. This is the check that turns "the path was sent to the
        // field" into "the field holds the path": pressing Open without it would act on whatever
        // the dialog already had selected if the write silently did not land (§10).
        if (!string.Equals(readBack.Value, managedAbsolutePath, StringComparison.Ordinal))
        {
            await CancelDialogAsync(
                target, dialog, signature.CancelControlId, signature.CancelControlClass, cancellationToken)
                .ConfigureAwait(false);

            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PhotoshopOpenInputFailed,
                "The Photoshop Open dialog did not read back the exact path PrintFlow wrote, so the " +
                "Open control was not pressed and the dialog was cancelled. Nothing was opened.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["writtenLength"] =
                        managedAbsolutePath.Length.ToString(CultureInfo.InvariantCulture),
                    ["readBackLength"] =
                        readBack.Value.Length.ToString(CultureInfo.InvariantCulture),
                    ["confirmPressed"] = "false",
                }));
        }

        OperationResult<Unit> pressed = _controls.Press(target.Process, confirm.Value);
        if (pressed.IsFailure)
        {
            await CancelDialogAsync(
                target, dialog, signature.CancelControlId, signature.CancelControlClass, cancellationToken)
                .ConfigureAwait(false);
            return OperationResult.Fail<Unit>(AsPhotoshop(pressed.Failure));
        }

        OperationResult<Unit> closed = await AwaitDialogClosedAsync(
            target, dialog.Handle, _options.OpenConfirmationTimeout, cancellationToken).ConfigureAwait(false);
        return closed.IsFailure
            ? OperationResult.Fail<Unit>(closed.Failure)
            : OperationResult.Ok();
    }

    // -----------------------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<OperationResult<PhotoshopDocumentIdentity>> ProbeDocumentIdentityAsync(
        PhotoshopTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        OperationResult<PhotoshopBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<PhotoshopDocumentIdentity>(baseline.Failure);
        }

        if (baseline.Value.DocumentIdentity is not { } signature)
        {
            return OperationResult.Fail<PhotoshopDocumentIdentity>(
                FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                "The verified evidence chain carries no signature that identifies which document " +
                "Photoshop is showing, so PrintFlow cannot confirm the right file is open and will " +
                "not claim that it is.");
        }

        OperationResult<PhotoshopTarget> ready = await ActivateAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return OperationResult.Fail<PhotoshopDocumentIdentity>(ready.Failure);
        }

        // Nothing is probed on a window that is not showing a document. Raising Save As over a
        // start screen would ask Photoshop a question about a document that does not exist.
        if (PhotoshopDocumentIdentityRule.DocumentNameInTitle(signature, ready.Value.Window.Title) is null)
        {
            return OperationResult.Fail<PhotoshopDocumentIdentity>(OperationFailure.Create(
                FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                "Photoshop's window names no document, so there is nothing to identify. No identity " +
                "surface was raised.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["windowTitle"] = ready.Value.Window.Title,
                    ["inputSent"] = "false",
                }));
        }

        OperationResult<Unit> raised = SendGuarded(ready.Value, KnownShortcut.SaveAsProbe);
        if (raised.IsFailure)
        {
            return OperationResult.Fail<PhotoshopDocumentIdentity>(raised.Failure);
        }

        OperationResult<ExternalWindowRef> dialog = await WaitForOwnedDialogAsync(
            ready.Value, signature.DialogClassName, signature.DialogTitle, _options.IdentityDialogTimeout,
            cancellationToken).ConfigureAwait(false);
        if (dialog.IsFailure)
        {
            return OperationResult.Fail<PhotoshopDocumentIdentity>(dialog.Failure);
        }

        // Read both values, then cancel — in that order, and the cancel happens whatever the
        // reads did. A probe that left the Save As surface up on a failure would leave Photoshop
        // modal, which is a worse outcome than the failure being reported (§13).
        OperationResult<PhotoshopDocumentIdentity> identity =
            ReadIdentity(ready.Value, dialog.Value, signature);

        OperationResult<Unit> cancelled = await CancelDialogAsync(
            ready.Value, dialog.Value, signature.CancelControlId, signature.CancelControlClass,
            cancellationToken).ConfigureAwait(false);

        if (identity.IsFailure)
        {
            return identity;
        }

        return cancelled.IsFailure
            ? OperationResult.Fail<PhotoshopDocumentIdentity>(cancelled.Failure)
            : identity;
    }

    /// <summary>Reads the document's own name and folder from the raised identity surface.</summary>
    private OperationResult<PhotoshopDocumentIdentity> ReadIdentity(
        PhotoshopTarget target, ExternalWindowRef dialog, PhotoshopDocumentIdentitySignature signature)
    {
        OperationResult<VerifiedControlRef> fileNameControl = _controls.Locate(
            target.Process, dialog.Handle, signature.FileNameControlId, signature.FileNameControlClass);
        if (fileNameControl.IsFailure)
        {
            return OperationResult.Fail<PhotoshopDocumentIdentity>(AsPhotoshop(fileNameControl.Failure));
        }

        OperationResult<VerifiedControlRef> addressControl = _controls.Locate(
            target.Process, dialog.Handle, signature.AddressControlId, signature.AddressControlClass);
        if (addressControl.IsFailure)
        {
            return OperationResult.Fail<PhotoshopDocumentIdentity>(AsPhotoshop(addressControl.Failure));
        }

        OperationResult<string> fileName = _controls.ReadText(target.Process, fileNameControl.Value);
        if (fileName.IsFailure)
        {
            return OperationResult.Fail<PhotoshopDocumentIdentity>(AsPhotoshop(fileName.Failure));
        }

        OperationResult<string> addressText = _controls.ReadText(target.Process, addressControl.Value);
        if (addressText.IsFailure)
        {
            return OperationResult.Fail<PhotoshopDocumentIdentity>(AsPhotoshop(addressText.Failure));
        }

        OperationResult<string> folder =
            PhotoshopDocumentIdentityRule.FolderFromAddressText(signature, addressText.Value);
        if (folder.IsFailure)
        {
            return OperationResult.Fail<PhotoshopDocumentIdentity>(folder.Failure);
        }

        OperationResult<string> fullPath =
            PhotoshopDocumentIdentityRule.ResolveObservedPath(folder.Value, fileName.Value);
        return fullPath.IsFailure
            ? OperationResult.Fail<PhotoshopDocumentIdentity>(fullPath.Failure)
            : OperationResult.Ok(new PhotoshopDocumentIdentity(
                fileName.Value, folder.Value, fullPath.Value, target.Window.Title));
    }

    // -----------------------------------------------------------------------------------
    // Close
    // -----------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
        PhotoshopTarget target, string expectedAbsolutePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAbsolutePath);

        // The identity is re-proved here rather than taken from the caller, and re-proved
        // immediately before the keystroke. Ctrl+W closes whatever is active *now*, so an
        // identity established a minute ago would be a statement about a document that may no
        // longer be the one in front (§21).
        OperationResult<PhotoshopDocumentIdentity> identity =
            await ProbeDocumentIdentityAsync(target, cancellationToken).ConfigureAwait(false);
        if (identity.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(identity.Failure);
        }

        if (!PhotoshopDocumentIdentityRule.MatchesExpectedDocument(
                expectedAbsolutePath, identity.Value.ObservedFullPath))
        {
            return OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                "The document Photoshop is holding is not the one PrintFlow was asked to close, so " +
                "nothing was closed. An operator's own work is never closed by PrintFlow.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["expectedDocument"] = expectedAbsolutePath,
                    ["observedDocument"] = identity.Value.ObservedFullPath,
                    ["inputSent"] = "false",
                }));
        }

        OperationResult<PhotoshopTarget> ready = await ActivateAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready;
        }

        OperationResult<Unit> sent = SendGuarded(ready.Value, KnownShortcut.CloseActiveDocument);
        if (sent.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(sent.Failure);
        }

        return await AwaitDocumentClosedAsync(ready.Value, identity.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the closed document to stop being the one in front, refusing to touch any
    /// prompt that appears.
    /// </summary>
    private async Task<OperationResult<PhotoshopTarget>> AwaitDocumentClosedAsync(
        PhotoshopTarget target, PhotoshopDocumentIdentity closed, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogCloseTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<PhotoshopTarget> verified = await VerifyTargetAsync(target).ConfigureAwait(false);
            if (verified.IsFailure)
            {
                return verified;
            }

            OperationResult<IReadOnlyList<ExternalWindowRef>> dialogs =
                _locator.FindOwnedDialogs(verified.Value.Process, verified.Value.Window);

            // An unsaved-changes prompt is the one thing that can appear here, and PrintFlow
            // does not answer it. Part A modifies nothing, so its appearance means something
            // outside this attempt changed the document — exactly when a guess would be worst.
            string[] prompts = dialogs.IsSuccess
                ? [.. TitledDialogs(dialogs.Value)]
                : [];

            if (prompts.Length > 0)
            {
                return OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                    FailureCode.PhotoshopBlockingDialog,
                    "Photoshop raised a dialog while closing the document. PrintFlow does not answer " +
                    "prompts it did not raise; the operator must resolve it.",
                    isRetryable: false,
                    context: new Dictionary<string, string>
                    {
                        ["dialogTitles"] = string.Join(" | ", prompts),
                        ["inputSent"] = "false",
                    }));
            }

            if (!string.Equals(
                    verified.Value.Window.Title, closed.WindowTitle, StringComparison.Ordinal))
            {
                return verified;
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                    FailureCode.Timeout,
                    "Photoshop still shows the document PrintFlow asked to close. Nothing further was " +
                    "sent; the document may still be loaded.",
                    isRetryable: true,
                    context: new Dictionary<string, string> { ["windowTitle"] = verified.Value.Window.Title }));
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    // -----------------------------------------------------------------------------------
    // Evidence
    // -----------------------------------------------------------------------------------

    /// <inheritdoc />
    public OperationResult<EvidenceRef> CaptureEvidence(PhotoshopTarget target, string reason)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _evidence.CaptureWindow(target.Window, reason);
    }

    // -----------------------------------------------------------------------------------
    // Guards shared by every route
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Re-reads the process and window and confirms they are still the verified target.
    /// </summary>
    /// <remarks>
    /// Checks four things that can each change independently: the process is still alive under
    /// the same identity, the window still exists, it still belongs to that process, and it is
    /// still visible and enabled. A handle that has been reused by a different window in the
    /// same process passes the first three and fails the ownership comparison below, which is
    /// the case this exists for (§6, §17).
    /// </remarks>
    private Task<OperationResult<PhotoshopTarget>> VerifyTargetAsync(PhotoshopTarget target)
    {
        if (!_locator.IsAlive(target.Process))
        {
            return Task.FromResult(OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                FailureCode.PhotoshopTargetLost,
                $"The verified Photoshop process {target.Process.ProcessId} is no longer running under " +
                "the same identity. No further input was produced.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["processId"] =
                        target.Process.ProcessId.ToString(CultureInfo.InvariantCulture),
                    ["inputSent"] = "false",
                })));
        }

        OperationResult<ExternalWindowRef> refreshed = _locator.Refresh(target.Window.Handle);
        if (refreshed.IsFailure)
        {
            return Task.FromResult(OperationResult.Fail<PhotoshopTarget>(AsPhotoshop(refreshed.Failure)));
        }

        if (refreshed.Value.OwningProcessId != target.Process.ProcessId)
        {
            return Task.FromResult(OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                FailureCode.PhotoshopTargetLost,
                $"Window {target.Window.Handle} now belongs to process " +
                $"{refreshed.Value.OwningProcessId}, not the verified {target.Process.ProcessId}. The " +
                "handle has been reused; no input was produced.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["expectedProcessId"] =
                        target.Process.ProcessId.ToString(CultureInfo.InvariantCulture),
                    ["actualProcessId"] =
                        refreshed.Value.OwningProcessId.ToString(CultureInfo.InvariantCulture),
                    ["inputSent"] = "false",
                })));
        }

        if (!refreshed.Value.IsVisible)
        {
            return Task.FromResult(OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                FailureCode.PhotoshopTargetLost,
                $"Window {target.Window.Handle} is no longer visible. No input was produced.",
                isRetryable: true,
                context: new Dictionary<string, string> { ["inputSent"] = "false" })));
        }

        return Task.FromResult(OperationResult.Ok(target with { Window = refreshed.Value }));
    }

    /// <summary>
    /// Sends one named keystroke, but only to a target that has just been re-verified.
    /// </summary>
    /// <remarks>
    /// The foreground check itself lives inside <see cref="IScopedInputSink"/>, where a caller
    /// cannot skip or reorder it. What this adds is the process/window re-verification
    /// immediately before, and the translation of the shared seam's failure vocabulary into the
    /// Photoshop one.
    /// </remarks>
    private OperationResult<Unit> SendGuarded(PhotoshopTarget target, KnownShortcut shortcut)
    {
        OperationResult<ExternalWindowRef> refreshed = _locator.Refresh(target.Window.Handle);
        if (refreshed.IsFailure)
        {
            return OperationResult.Fail<Unit>(AsPhotoshop(refreshed.Failure));
        }

        if (refreshed.Value.OwningProcessId != target.Process.ProcessId ||
            !refreshed.Value.IsVisible || !refreshed.Value.IsEnabled)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PhotoshopTargetLost,
                "The Photoshop window stopped being an addressable target between verification and " +
                "input. Nothing was sent.",
                isRetryable: true,
                context: new Dictionary<string, string> { ["inputSent"] = "false" }));
        }

        OperationResult<Unit> sent = _input.SendShortcut(target.Window.Handle, shortcut);
        return sent.IsFailure ? OperationResult.Fail<Unit>(AsPhotoshop(sent.Failure)) : sent;
    }

    /// <summary>
    /// Waits for a dialog owned by the verified window that carries the signed class and title.
    /// </summary>
    /// <remarks>
    /// Ownership is what identifies it. The title is compared as corroboration — a window in
    /// another process could carry the same title, and could not be owned by this one.
    /// </remarks>
    private async Task<OperationResult<ExternalWindowRef>> WaitForOwnedDialogAsync(
        PhotoshopTarget target,
        string expectedClassName,
        string expectedTitle,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<PhotoshopTarget> verified = await VerifyTargetAsync(target).ConfigureAwait(false);
            if (verified.IsFailure)
            {
                return OperationResult.Fail<ExternalWindowRef>(verified.Failure);
            }

            OperationResult<IReadOnlyList<ExternalWindowRef>> dialogs =
                _locator.FindOwnedDialogs(verified.Value.Process, verified.Value.Window);
            if (dialogs.IsFailure)
            {
                return OperationResult.Fail<ExternalWindowRef>(AsPhotoshop(dialogs.Failure));
            }

            List<ExternalWindowRef> matches =
            [
                .. dialogs.Value.Where(dialog =>
                    string.Equals(dialog.ClassName, expectedClassName, StringComparison.Ordinal) &&
                    string.Equals(dialog.Title, expectedTitle, StringComparison.Ordinal) &&
                    dialog.IsVisible && dialog.IsEnabled),
            ];

            if (matches.Count == 1)
            {
                return OperationResult.Ok(matches[0]);
            }

            if (matches.Count > 1)
            {
                return OperationResult.Fail<ExternalWindowRef>(OperationFailure.Create(
                    FailureCode.PhotoshopUnknownState,
                    $"{matches.Count} windows owned by Photoshop match the signed dialog signature. " +
                    "PrintFlow will not choose between them; nothing was written or pressed.",
                    isRetryable: false,
                    context: new Dictionary<string, string> { ["inputSent"] = "false" }));
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<ExternalWindowRef>(OperationFailure.Create(
                    FailureCode.PhotoshopUnknownState,
                    $"No window owned by Photoshop matching the signed '{expectedTitle}' signature " +
                    $"appeared within {timeout.TotalSeconds:0}s. Nothing further was sent.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["expectedClass"] = expectedClassName,
                        ["expectedTitle"] = expectedTitle,
                        ["observedOwnedDialogs"] = dialogs.Value.Count.ToString(CultureInfo.InvariantCulture),
                        ["inputSent"] = "false",
                    }));
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Cancels a dialog PrintFlow itself raised, through its signed Cancel control.</summary>
    /// <remarks>
    /// The only surface-dismissal in this driver, and it applies only to a dialog this driver
    /// raised a moment earlier. Nothing here dismisses a dialog PrintFlow found already open.
    /// </remarks>
    private async Task<OperationResult<Unit>> CancelDialogAsync(
        PhotoshopTarget target,
        ExternalWindowRef dialog,
        int cancelControlId,
        string cancelControlClass,
        CancellationToken cancellationToken)
    {
        OperationResult<VerifiedControlRef> cancel = _controls.Locate(
            target.Process, dialog.Handle, cancelControlId, cancelControlClass);
        if (cancel.IsFailure)
        {
            return OperationResult.Fail<Unit>(AsPhotoshop(cancel.Failure));
        }

        OperationResult<Unit> pressed = _controls.Press(target.Process, cancel.Value);
        if (pressed.IsFailure)
        {
            return OperationResult.Fail<Unit>(AsPhotoshop(pressed.Failure));
        }

        return await AwaitDialogClosedAsync(
            target, dialog.Handle, _options.DialogCloseTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until a dialog this driver raised is gone <b>and</b> Photoshop is accepting input
    /// again.
    /// </summary>
    /// <remarks>
    /// Both conditions, and the second one is not belt-and-braces. A modal disables the main
    /// window while it is up, and Photoshop re-enables it a moment after the dialog window
    /// itself disappears. Treating the dialog's disappearance as the end of the sequence meant
    /// the next keystroke could be delivered to a still-disabled window, where Windows discards
    /// it — which is exactly what happened live: an identity probe run immediately after a
    /// previous probe's cancel produced no dialog at all and timed out.
    /// <para>
    /// The fix is a positive observation rather than a sleep. PrintFlow waits until it can see
    /// that Photoshop is ready, so the next input is sent to a window that can receive it, and a
    /// Photoshop that never recovers produces an honest timeout instead of a silently dropped
    /// keystroke.
    /// </para>
    /// </remarks>
    private async Task<OperationResult<Unit>> AwaitDialogClosedAsync(
        PhotoshopTarget target, WindowHandle dialog, TimeSpan timeout, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<ExternalWindowRef> refreshed = _locator.Refresh(dialog);
            if (refreshed.IsFailure || !refreshed.Value.IsVisible)
            {
                OperationResult<ExternalWindowRef> host = _locator.Refresh(target.Window.Handle);
                if (host.IsSuccess && host.Value.IsEnabled)
                {
                    return OperationResult.Ok();
                }

                if (_clock.GetUtcNow() >= deadline)
                {
                    return OperationResult.Fail<Unit>(OperationFailure.Create(
                        FailureCode.PhotoshopUnknownState,
                        $"A Photoshop dialog PrintFlow raised has closed, but Photoshop has not accepted " +
                        $"input again within {timeout.TotalSeconds:0}s. Nothing further was sent.",
                        isRetryable: true,
                        context: new Dictionary<string, string> { ["inputSent"] = "false" }));
                }

                await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<Unit>(OperationFailure.Create(
                    FailureCode.PhotoshopBlockingDialog,
                    $"A Photoshop dialog PrintFlow raised was still open after " +
                    $"{timeout.TotalSeconds:0}s. Nothing further was sent; the operator must resolve it.",
                    isRetryable: false,
                    context: new Dictionary<string, string>
                    {
                        ["dialogTitle"] = refreshed.Value.Title,
                        ["inputSent"] = "false",
                    }));
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Restates a shared-seam failure in the Photoshop vocabulary.
    /// </summary>
    /// <remarks>
    /// The shared automation primitives were written for Meitu and report Meitu-named codes;
    /// they cannot know which application a caller is driving. Translating here — rather than
    /// parameterising the shared classes, or letting a Photoshop step report
    /// <c>MeituTargetLost</c> — keeps the vocabulary honest without touching a proven seam
    /// (§18). The technical detail and context are carried through untouched, so nothing about
    /// the diagnosis is lost in the restatement.
    /// </remarks>
    private static OperationFailure AsPhotoshop(OperationFailure failure)
    {
        FailureCode translated = failure.Code switch
        {
            FailureCode.MeituNotInstalled => FailureCode.PhotoshopNotInstalled,
            FailureCode.MeituLaunchFailed => FailureCode.PhotoshopLaunchFailed,
            FailureCode.MeituWindowNotFound => FailureCode.PhotoshopWindowNotFound,
            FailureCode.MeituTargetLost => FailureCode.PhotoshopTargetLost,
            FailureCode.MeituUnknownState => FailureCode.PhotoshopUnknownState,
            FailureCode.MeituBlockingDialog => FailureCode.PhotoshopBlockingDialog,
            FailureCode.MeituOpenInputFailed => FailureCode.PhotoshopOpenInputFailed,
            _ => failure.Code,
        };

        return translated == failure.Code
            ? failure
            : failure with { Code = translated, MessageKey = $"Failure_{translated}" };
    }
}
