using System.Collections.Immutable;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>Verifies native history and recognizer transitions for an instant cached result.</summary>
public static class MeituRecognitionReapplyRule
{
    private const string RecognizerSuffix = ".MattingNewParameterWidget.widget.stackedMattingWidget.page1.scrollArea.qt_scrollarea_viewport.scrollAreaWidgetContents.mattingnew.centerWidget.item1.container1.btnSod";
    private const string HistoryRoot = "MainWindow.centralwidget.mainStackedWidget.editPage.subBackgroundWidget.titleWidget.stackedWidget.editorPage.";

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

        return HistoryMatches(controls, "undoButton", "\uE2A3", enabled: !undone) &&
            HistoryMatches(controls, "redoButton", "\uE2A4", enabled: undone);
    }

    private static bool HistoryMatches(ImmutableArray<UiElementIdentity> controls, string id, string name, bool enabled)
    {
        UiElementIdentity[] matches = [.. controls.Where(control => control.AutomationId == HistoryRoot + id)];
        return matches.Length == 1 && matches[0].ControlTypeName == "Button" &&
            matches[0].Name == name && matches[0].ClassName == "proui::IconFontButton" && matches[0].IsEnabled == enabled;
    }
}
