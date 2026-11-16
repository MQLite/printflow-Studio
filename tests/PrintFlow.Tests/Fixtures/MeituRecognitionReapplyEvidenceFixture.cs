using System.Collections.Immutable;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Tests.Fixtures;

internal static class MeituRecognitionReapplyEvidenceFixture
{
    internal const string RecognizerRoot = "MainWindow.MattingNewParameterWidget.widget.stackedMattingWidget.page1.scrollArea.qt_scrollarea_viewport.scrollAreaWidgetContents.mattingnew.centerWidget.item1.container1.btnSod";
    internal const string HistoryRoot = "MainWindow.centralwidget.mainStackedWidget.editPage.subBackgroundWidget.titleWidget.stackedWidget.editorPage.";

    internal static MeituRecognitionReapplyEvidence Create(
        MeituBackgroundRemovalSignature signature, string identity = "A_副本", int processId = 4242, int recognizer = 2)
    {
        MeituStateSnapshot Snapshot(IEnumerable<string> markers) => new(
            MeituStartingState.KnownEditorWithExpectedWorkingCopy, [],
            new MeituObservation("美图秀秀-图片编辑", [.. markers], [], true, "A.png", identity));
        return new MeituRecognitionReapplyEvidence(recognizer,
            Snapshot(signature.Completion.RequiredMarkers.Where(marker => marker != "移除背景")),
            Snapshot(signature.Completion.RequiredMarkers),
            Controls(processId, selected: 0, undone: true),
            Controls(processId, selected: recognizer, undone: false));
    }

    private static ImmutableArray<UiElementIdentity> Controls(int processId, int selected, bool undone)
    {
        List<UiElementIdentity> controls = [];
        for (int index = 0; index < 4; index++)
            controls.Add(new UiElementIdentity("CheckBox", RecognizerRoot + index, "", "CMattingIconButton",
                processId, [UiPatternKind.Invoke, UiPatternKind.Toggle], new UiBounds(100 + index * 60, 220, 51, 80),
                true, false) { IsSelected = selected == index });
        controls.Add(new UiElementIdentity("Button", HistoryRoot + "undoButton", "\uE2A3", "proui::IconFontButton",
            processId, [UiPatternKind.Invoke], new UiBounds(1000, 50, 32, 32), !undone, false));
        controls.Add(new UiElementIdentity("Button", HistoryRoot + "redoButton", "\uE2A4", "proui::IconFontButton",
            processId, [UiPatternKind.Invoke], new UiBounds(1036, 50, 32, 32), undone, false));
        return [.. controls];
    }
}
