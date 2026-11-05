using System.Collections.Immutable;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

public sealed partial class GuardedMeituUiDriver
{
    private async Task<OperationResult<MeituTarget>> WaitForBackgroundRemovalReturnAsync(
        MeituTarget target, string fileName, string identity, MeituBackgroundRemovalSignature signature,
        IAutomationStopSignal stop, CancellationToken cancellationToken)
    {
        var baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure) return OperationResult.Fail<MeituTarget>(baseline.Failure);
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StopBeforeExport(stop) is { } stopped) return OperationResult.Fail<MeituTarget>(stopped);
            var read = await InspectStateCoreAsync(target, fileName, identity, cancellationToken).ConfigureAwait(false);
            if (read.IsFailure) return OperationResult.Fail<MeituTarget>(read.Failure);
            if (StopBeforeExport(stop) is { } stoppedDuringRead) return OperationResult.Fail<MeituTarget>(stoppedDuringRead);
            var observation = read.Value.Observation;
            MeituBackgroundRemovalPhase phase = MeituBackgroundRemovalRule.Classify(signature, observation);
            if (read.Value.State == MeituStartingState.KnownModal || !observation.MainWindowEnabled ||
                !observation.OwnedDialogTitles.IsDefaultOrEmpty || observation.WindowTitle != target.Window.Title ||
                phase == MeituBackgroundRemovalPhase.Busy || read.Value.State == MeituStartingState.Busy)
                return OperationResult.Fail<MeituTarget>(FailureCode.MeituUnknownState,
                    "The owned editor became blocked, Busy or changed while returning from cutout. No identity probe or export was invoked.");

            if (phase == MeituBackgroundRemovalPhase.Unobserved &&
                MeituDocumentIdentityRule.ClassifyIdentityProbeSurface(baseline.Value, observation) == MeituDocumentSurfacePhase.LoadedEditor &&
                read.Value.State == MeituStartingState.KnownEditorWithExpectedWorkingCopy)
                return await ReacquireEditorAsync(target, fileName, identity, cancellationToken).ConfigureAwait(false);

            if (_clock.GetUtcNow() >= deadline)
                return OperationResult.Fail<MeituTarget>(FailureCode.Timeout,
                    "The previous cutout panel did not retire after the signed Adjust return. No identity probe or export was invoked.");
            // Returning is asynchronous in Qt. Persistent old result controls are observed only;
            // Save is withheld until the ordinary loaded editor is positively established.
            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    // Meitu 7.9's automatic-selection panel: label -> QWidget -> CMattingIconButton.
    // The other two mattingnew modules have identical ids, so the exact marker is essential.
    private const string RecognitionModuleSuffix =
        ".MattingNewParameterWidget.widget.stackedMattingWidget.page1.scrollArea.qt_scrollarea_viewport.scrollAreaWidgetContents.mattingnew";
    private const string RecognitionHistoryPrefix =
        "MainWindow.centralwidget.mainStackedWidget.editPage.subBackgroundWidget.titleWidget.stackedWidget.editorPage.";
    private const string RecognitionApplySuffix =
        ".MattingNewParameterWidget.widget.stackedMattingWidget.page1.scrollArea.qt_scrollarea_viewport.scrollAreaWidgetContents.resetWidget.applyIntelligentButton";

    private MeituObservation ObserveBackgroundRemovalResultControl(MeituTarget target, MeituObservation observation)
    {
        // Expanded Meitu 7.9 preview uses this label in place of 移除背景. Text alone does
        // not grant the alternative completion marker: the exact native button must agree.
        if (!observation.VisibleTexts.Contains("应用到背景")) return observation;
        var control = FindSignedControl(target, target.Window.Handle,
            new MeituControlSignature("应用到背景", RecognitionApplySuffix, "Button", "QPushButton", UiPatternKind.Invoke));
        bool observed = false;
        if (control.IsSuccess)
        {
            var described = _elements.Describe(control.Value);
            observed = described.IsSuccess && !described.Value.Bounds.IsEmpty;
        }
        return observation with { BackgroundRemovalApplyControlObserved = observed };
    }

    private static string RecognitionModeName(BackgroundRemovalMode mode) => mode switch
    {
        BackgroundRemovalMode.Automatic => "自动选择",
        BackgroundRemovalMode.PortraitPets => "人像宠物",
        BackgroundRemovalMode.Products => "商品货物",
        BackgroundRemovalMode.LogosStamps => "图标印章",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private OperationResult<UiElementRef> FindRecognizer(MeituTarget target, int index)
    {
        string marker = index switch { 0 => "关闭\n", 1 => "人像\n宠物", 2 => "商品\n货物", 3 => "图标\n印章", _ => throw new ArgumentOutOfRangeException(nameof(index)) };
        return FindBackgroundRemovalOwner(target, target.Window.Handle, marker,
            new MeituOwnedControlShape("Text", "QLabel", ".widget.label", "CheckBox", "CMattingIconButton",
                RecognitionModuleSuffix + ".centerWidget.item1.container1.btnSod" + index,
                ".widget.label", 2, UiPatternKind.Invoke));
    }

    private OperationResult<int> ReadSelectedRecognizer(MeituTarget target)
    {
        int selected = -1;
        for (int index = 0; index < 4; index++)
        {
            var control = FindRecognizer(target, index);
            if (control.IsFailure) return OperationResult.Fail<int>(control.Failure);
            var described = _elements.Describe(control.Value);
            if (described.IsFailure) return OperationResult.Fail<int>(described.Failure);
            if (described.Value.IsSelected is not { } isSelected)
                return OperationResult.Fail<int>(FailureCode.MeituUnknownState, "Meitu did not expose the recognizer's selected state. No result may be exported.");
            if (!isSelected) continue;
            if (selected >= 0)
                return OperationResult.Fail<int>(FailureCode.MeituUnknownState, "More than one Meitu recognizer is selected. No result may be exported.");
            selected = index;
        }

        return selected < 0
            ? OperationResult.Fail<int>(FailureCode.MeituUnknownState, "No Meitu recognizer is positively selected. No result may be exported.")
            : OperationResult.Ok(selected);
    }

    private OperationResult<ImmutableArray<UiElementIdentity>> ReadRecognizerTransitionControls(MeituTarget target)
    {
        ImmutableArray<UiElementIdentity>.Builder controls = ImmutableArray.CreateBuilder<UiElementIdentity>(6);
        for (int index = 0; index < 4; index++)
        {
            var control = FindRecognizer(target, index);
            if (control.IsFailure) return OperationResult.Fail<ImmutableArray<UiElementIdentity>>(control.Failure);
            var described = _elements.Describe(control.Value);
            if (described.IsFailure) return OperationResult.Fail<ImmutableArray<UiElementIdentity>>(described.Failure);
            controls.Add(described.Value);
        }

        foreach (var history in new[] { (Name: "\uE2A3", Id: "undoButton"), (Name: "\uE2A4", Id: "redoButton") })
        {
            // Disabled is meaningful evidence here: Undo/Redo reverse their enabled states
            // between a positively restored original and a newly applied recognizer result.
            var found = _elements.FindAll(target.Window.Handle, new UiElementQuery(UiControlKind.Button, Name: history.Name));
            if (found.IsFailure) return OperationResult.Fail<ImmutableArray<UiElementIdentity>>(found.Failure);
            List<UiElementIdentity> matches = [];
            foreach (var control in found.Value)
            {
                var item = _elements.Describe(control);
                if (item.IsSuccess && item.Value.ProcessId == target.Process.ProcessId &&
                    item.Value.AutomationId == RecognitionHistoryPrefix + history.Id &&
                    item.Value.Name == history.Name && item.Value.ClassName == "proui::IconFontButton" &&
                    item.Value.ControlTypeName == "Button" && item.Value.Supports(UiPatternKind.Invoke) &&
                    !item.Value.IsOffscreen && !item.Value.Bounds.IsEmpty)
                    matches.Add(item.Value);
            }
            if (matches.Count != 1)
                return OperationResult.Fail<ImmutableArray<UiElementIdentity>>(FailureCode.MeituUnknownState,
                    "The recognizer's native Undo/Redo state is missing or ambiguous. No processing claim is available.");
            controls.Add(matches[0]);
        }
        return OperationResult.Ok(controls.MoveToImmutable());
    }

    private async Task<OperationResult<Unit>> InvokeRecognizerControlAsync(
        MeituTarget target, UiElementRef control, MeituBackgroundRemovalSignature signature,
        string fileName, string identity, IAutomationStopSignal stop, CancellationToken cancellationToken,
        bool allowClosedRecognizer = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        stop.ReportPhase(ExternalOperationPhase.CompletedBeforeExport);
        if (StopBeforeExport(stop) is { } stopped) return OperationResult.Fail<Unit>(stopped);
        var verified = await VerifyTargetAsync(target, cancellationToken).ConfigureAwait(false);
        if (verified.IsFailure) return OperationResult.Fail<Unit>(verified.Failure);
        var state = ReadBackgroundRemovalPhaseSnapshot(target, fileName, identity, signature);
        if (state.IsFailure) return OperationResult.Fail<Unit>(state.Failure);
        MeituBackgroundRemovalPhase phase = MeituBackgroundRemovalRule.Classify(signature, state.Value.Observation);
        bool ready = phase == MeituBackgroundRemovalPhase.Complete;
        if (!ready && allowClosedRecognizer && phase != MeituBackgroundRemovalPhase.Busy &&
            signature.Completion.RequiredMarkers.Contains("移除背景"))
        {
            // Undo removes the result and disables Apply. It is a neutral preparation state,
            // corroborated by all four remaining panel markers and the four exact native toggles.
            var selected = ReadSelectedRecognizer(target);
            ready = selected.IsSuccess && selected.Value == 0 &&
                signature.Completion.RequiredMarkers.Where(marker => marker != "移除背景").All(marker =>
                    state.Value.Observation.VisibleTexts.Any(text => text.Contains(marker, StringComparison.Ordinal)));
        }
        if (!ready || state.Value.State == MeituStartingState.KnownModal)
            return OperationResult.Fail<Unit>(FailureCode.MeituUnknownState,
                $"The owned cutout state changed before recognizer input (phase {phase}; closed recognizer allowed: {allowClosedRecognizer}). No selection input was sent.");
        var stillOurs = await VerifyTargetAsync(target, cancellationToken).ConfigureAwait(false);
        if (stillOurs.IsFailure) return OperationResult.Fail<Unit>(stillOurs.Failure);
        if (StopBeforeExport(stop) is { } stoppedAtInput) return OperationResult.Fail<Unit>(stoppedAtInput);
        return _elements.Invoke(control);
    }

    private async Task<OperationResult<MeituRecognizerRun>> SelectBackgroundRecognizerAsync(
        MeituTarget target, string fileName, string identity, MeituBackgroundRemovalSignature signature,
        BackgroundRemovalMode mode,
        IAutomationStopSignal stop, CancellationToken cancellationToken)
    {
        int requested = mode switch { BackgroundRemovalMode.Automatic => 0, BackgroundRemovalMode.PortraitPets => 1, BackgroundRemovalMode.Products => 2, BackgroundRemovalMode.LogosStamps => 3, _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
        var desired = FindRecognizer(target, requested);
        if (desired.IsFailure)
        {
            var module = FindBackgroundRemovalOwner(target, target.Window.Handle, "自动选择",
                new MeituOwnedControlShape("Text", "QLabel", ".titleLabel", "CheckBox", "ModuleButton",
                    RecognitionModuleSuffix, ".buttonWidget.titleLabel", 2, UiPatternKind.Invoke));
            if (module.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(module.Failure);
            var moduleState = _elements.Describe(module.Value);
            if (moduleState.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(moduleState.Failure);
            if (moduleState.Value.IsSelected != false)
                return OperationResult.Fail<MeituRecognizerRun>(desired.Failure);
            // A collapsed module has no visible option markers. Visible but rejected controls
            // are a structural failure, never permission to try a different input.
            foreach (string name in new[] { "关闭\n", "人像\n宠物", "商品\n货物", "图标\n印章" })
            {
                var markers = _elements.FindAll(target.Window.Handle, new UiElementQuery(UiControlKind.Any, Name: name));
                if (markers.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(markers.Failure);
                foreach (var marker in markers.Value)
                {
                    var description = _elements.Describe(marker);
                    if (description.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(description.Failure);
                    if (!description.Value.IsOffscreen && !description.Value.Bounds.IsEmpty)
                        return OperationResult.Fail<MeituRecognizerRun>(desired.Failure);
                }
            }
            var expanded = await InvokeRecognizerControlAsync(target, module.Value, signature, fileName, identity, stop, cancellationToken).ConfigureAwait(false);
            if (expanded.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(expanded.Failure);
            DateTimeOffset deadline = _clock.GetUtcNow() + _options.DialogTimeout;
            do
            {
                await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
                if (StopBeforeExport(stop) is { } stoppedWhileExpanding)
                    return OperationResult.Fail<MeituRecognizerRun>(stoppedWhileExpanding);
                desired = FindRecognizer(target, requested);
            } while (desired.IsFailure && _clock.GetUtcNow() < deadline);
            if (desired.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(desired.Failure);
        }

        // Expanding the automatic module can itself start recognition on some Meitu states.
        // Observe it to completion before reading the selected recognizer or undoing anything.
        var entryCompletion = await AwaitBackgroundRemovalPhaseAsync(target, fileName, identity, signature,
            MeituBackgroundRemovalPhase.Complete, _options.BackgroundRemovalCompletionTimeout, stop, cancellationToken).ConfigureAwait(false);
        if (entryCompletion.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(entryCompletion.Failure);
        var selected = ReadSelectedRecognizer(target);
        if (selected.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(selected.Failure);
        if (mode == BackgroundRemovalMode.Automatic)
        {
            if (selected.Value == 0)
                return OperationResult.Fail<MeituRecognizerRun>(FailureCode.MeituUnknownState,
                    "Meitu has not selected an automatic recognizer. No recognizer will be guessed and no result may be exported.");
            requested = selected.Value;
        }

        // Opening this page can auto-detect immediately. Its completed result belongs to this
        // load, but must be undone before applying the operator's recognizer, even if it chose
        // the same mode. The recognizer's "关闭" state plus enabled Redo corroborates the undo.
        bool entryResultUndone = selected.Value != 0;
        if (entryResultUndone)
        {
            var undo = FindSignedControl(target, target.Window.Handle,
                new MeituControlSignature("\uE2A3", RecognitionHistoryPrefix + "undoButton", "Button", "proui::IconFontButton", UiPatternKind.Invoke));
            if (undo.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(undo.Failure);
            var undone = await InvokeRecognizerControlAsync(target, undo.Value, signature, fileName, identity, stop, cancellationToken).ConfigureAwait(false);
            if (undone.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(undone.Failure);
            DateTimeOffset undoDeadline = _clock.GetUtcNow() + _options.DialogTimeout;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (StopBeforeExport(stop) is { } stoppedAfterUndo)
                    return OperationResult.Fail<MeituRecognizerRun>(stoppedAfterUndo);
                selected = ReadSelectedRecognizer(target);
                var redo = FindSignedControl(target, target.Window.Handle,
                    new MeituControlSignature("\uE2A4", RecognitionHistoryPrefix + "redoButton", "Button", "proui::IconFontButton", UiPatternKind.Invoke));
                if (selected.IsSuccess && selected.Value == 0 && redo.IsSuccess) break;
                if (_clock.GetUtcNow() >= undoDeadline)
                    return OperationResult.Fail<MeituRecognizerRun>(FailureCode.MeituUnknownState,
                        "Meitu's entry auto-recognition was not positively undone. No recognizer was reapplied and no result may be exported.");
                await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
            }
        }

        // Correlate the selected recognizer's own run. Cached results can finish during Invoke;
        // they require the original-to-result toggle and Undo/Redo transition instead of Busy.
        var beforeReapply = ReadBackgroundRemovalPhaseSnapshot(target, fileName, identity, signature);
        if (beforeReapply.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(beforeReapply.Failure);
        var beforeControls = ReadRecognizerTransitionControls(target);
        if (beforeControls.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(beforeControls.Failure);
        desired = FindRecognizer(target, requested);
        if (desired.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(desired.Failure);
        var invoked = await InvokeRecognizerControlAsync(target, desired.Value, signature, fileName, identity, stop, cancellationToken,
            allowClosedRecognizer: true).ConfigureAwait(false);
        if (invoked.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(invoked.Failure);
        var busy = await AwaitBackgroundRemovalPhaseAsync(target, fileName, identity, signature,
            MeituBackgroundRemovalPhase.Busy, _options.BackgroundRemovalBusyTimeout, stop, cancellationToken,
            allowCompletedEntry: true).ConfigureAwait(false);
        if (busy.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(busy.Failure);
        var completion = await AwaitBackgroundRemovalPhaseAsync(target, fileName, identity, signature,
            MeituBackgroundRemovalPhase.Complete, _options.BackgroundRemovalCompletionTimeout, stop, cancellationToken).ConfigureAwait(false);
        if (completion.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(completion.Failure);
        selected = ReadSelectedRecognizer(target);
        if (selected.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(selected.Failure);
        if (selected.Value != requested)
            return OperationResult.Fail<MeituRecognizerRun>(FailureCode.MeituUnknownState,
                $"Meitu selected recognizer {selected.Value}, but {RecognitionModeName(mode)} was requested. No result may be exported.");
        var afterControls = ReadRecognizerTransitionControls(target);
        if (afterControls.IsFailure) return OperationResult.Fail<MeituRecognizerRun>(afterControls.Failure);
        var evidence = new MeituRecognitionReapplyEvidence(requested, beforeReapply.Value, completion.Value,
            beforeControls.Value, afterControls.Value);
        bool transitionProven = entryResultUndone && MeituRecognitionReapplyRule.IsValid(
            evidence, target.Process.ProcessId, identity, signature);
        bool busyObserved = MeituBackgroundRemovalRule.Classify(signature, busy.Value.Observation) == MeituBackgroundRemovalPhase.Busy;
        if (!busyObserved && !transitionProven)
            return OperationResult.Fail<MeituRecognizerRun>(FailureCode.MeituUnknownState,
                "The recognizer produced neither observable Busy nor a verified original-to-result history transition. No result may be exported.");
        return OperationResult.Ok(new MeituRecognizerRun(busyObserved ? busy.Value : null, completion.Value,
            transitionProven ? evidence : null));
    }

    private sealed record MeituRecognizerRun(MeituStateSnapshot? Busy, MeituStateSnapshot Completion,
        MeituRecognitionReapplyEvidence? Evidence);
}
