using System.Collections.Immutable;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

internal sealed record MeituHistoryButton(string AutomationId, string Glyph, string AlternateGlyph)
{
    private const string HistoryRoot = "MainWindow.centralwidget.mainStackedWidget.editPage.subBackgroundWidget.titleWidget.stackedWidget.editorPage.";
    internal static readonly MeituHistoryButton Undo = new(HistoryRoot + "undoButton", "\uE2A3", "\uE2A7");
    internal static readonly MeituHistoryButton Redo = new(HistoryRoot + "redoButton", "\uE2A4", "\uE2A8");

    internal bool Matches(UiElementIdentity control, int processId) =>
        control.ProcessId == processId && control.AutomationId == AutomationId &&
        (control.Name == Glyph || control.Name == AlternateGlyph) &&
        control.ControlTypeName == "Button" && control.ClassName == "proui::IconFontButton" &&
        control.Supports(UiPatternKind.Invoke) && !control.IsOffscreen && !control.Bounds.IsEmpty;
}

/// <summary>Verifies native history and recognizer transitions for an instant cached result.</summary>
public static class MeituRecognitionReapplyRule
{
    private const string RecognizerSuffix = ".MattingNewParameterWidget.widget.stackedMattingWidget.page1.scrollArea.qt_scrollarea_viewport.scrollAreaWidgetContents.mattingnew.centerWidget.item1.container1.btnSod";

    public static bool IsValid(MeituRecognitionReapplyEvidence evidence, int expectedProcessId,
        string observedIdentity, MeituBackgroundRemovalSignature signature)
    {
        if (evidence is null || signature is null || expectedProcessId <= 0 ||
            string.IsNullOrEmpty(observedIdentity) || evidence.RecognizerIndex is < 1 or > 3 ||
            evidence.Before is null || evidence.After is null || signature.Completion.RequiredMarkers.IsDefaultOrEmpty)
            return false;

        MeituObservation before = evidence.Before.Observation;
        MeituObservation after = evidence.After.Observation;
        if (!IsOwnedSnapshot(evidence.Before, observedIdentity) || !IsOwnedSnapshot(evidence.After, observedIdentity) ||
            string.IsNullOrEmpty(before.WindowTitle) || before.WindowTitle != after.WindowTitle ||
            MeituBackgroundRemovalRule.Classify(signature, before) == MeituBackgroundRemovalPhase.Busy ||
            MeituBackgroundRemovalRule.Classify(signature, after) != MeituBackgroundRemovalPhase.Complete)
            return false;

        string[] panelMarkers = [.. signature.Completion.RequiredMarkers.Where(marker => marker != "移除背景")];
        if (panelMarkers.Length < 4 || before.VisibleTexts.IsDefaultOrEmpty ||
            panelMarkers.Any(marker => !before.VisibleTexts.Any(text => text.Contains(marker, StringComparison.Ordinal))))
            return false;

        return ControlsMatch(evidence.BeforeControls, expectedProcessId, selected: 0, undone: true) &&
            ControlsMatch(evidence.AfterControls, expectedProcessId, evidence.RecognizerIndex, undone: false);
    }

    private static bool IsOwnedSnapshot(MeituStateSnapshot snapshot, string identity) =>
        snapshot.Observation is { MainWindowEnabled: true } observation &&
        snapshot.State is not (MeituStartingState.Busy or MeituStartingState.KnownModal) &&
        observation.OwnedDialogTitles.IsDefaultOrEmpty &&
        string.Equals(observation.ObservedDocumentIdentity, identity, StringComparison.Ordinal);

    private static bool ControlsMatch(ImmutableArray<UiElementIdentity> controls, int processId, int selected, bool undone)
    {
        if (controls.IsDefault || controls.Length != 6 || controls.Any(control => control is null ||
            control.ProcessId != processId || control.IsOffscreen || control.Bounds.IsEmpty || !control.Supports(UiPatternKind.Invoke)))
            return false;

        for (int index = 0; index < 4; index++)
        {
            UiElementIdentity[] owners = [.. controls.Where(control =>
                control.AutomationId.EndsWith(RecognizerSuffix + index, StringComparison.Ordinal))];
            if (owners.Length != 1) return false;
            UiElementIdentity owner = owners[0];
            if (owner.ControlTypeName != "CheckBox" || owner.ClassName != "CMattingIconButton" ||
                !owner.IsEnabled || !owner.Supports(UiPatternKind.Toggle) || owner.IsSelected != (index == selected))
                return false;
        }

        return controls.Any(control => MeituHistoryButton.Undo.Matches(control, processId) && control.IsEnabled == !undone) &&
            controls.Any(control => MeituHistoryButton.Redo.Matches(control, processId) && control.IsEnabled == undone);
    }
}
