using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Unit.Automation;

public sealed class MeituRecognitionReapplyRuleTests
{
    private static MeituBackgroundRemovalSignature Signature() => MeituFakes.BackgroundRemoval() with
    {
        Busy = new MeituBusySignature(["智能识别中...", "取消"], 2),
        Completion = new MeituCompletionSignature(["自动选择", "局部抠图", "手动修补", "反选", "移除背景"], 5, true),
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void An_owned_closed_to_recognizer_and_undo_redo_transition_correlates_instant_results(int recognizer)
    {
        var signature = Signature();
        var evidence = MeituRecognitionReapplyEvidenceFixture.Create(signature, recognizer: recognizer);
        MeituRecognitionReapplyRule.IsValid(evidence, 4242, "A_副本", signature).ShouldBeTrue();
    }

    [Theory]
    [InlineData("before-undo-enabled")]
    [InlineData("before-redo-disabled")]
    [InlineData("after-undo-disabled")]
    [InlineData("after-redo-enabled")]
    [InlineData("foreign-process")]
    [InlineData("wrong-id")]
    [InlineData("wrong-class")]
    [InlineData("no-toggle")]
    [InlineData("no-invoke")]
    [InlineData("wrong-history-id")]
    [InlineData("wrong-undo-name")]
    [InlineData("wrong-redo-name")]
    [InlineData("multiple-selected")]
    [InlineData("offscreen")]
    [InlineData("empty-bounds")]
    [InlineData("disabled-owner")]
    [InlineData("before-selected-result")]
    [InlineData("after-selected-closed")]
    [InlineData("missing-control")]
    [InlineData("extra-control")]
    [InlineData("unknown-selection")]
    public void Broken_native_control_evidence_cannot_correlate_a_result(string defect)
    {
        var signature = Signature();
        var evidence = MeituRecognitionReapplyEvidenceFixture.Create(signature);
        var before = evidence.BeforeControls;
        var after = evidence.AfterControls;
        evidence = defect switch
        {
            "before-undo-enabled" => evidence with { BeforeControls = before.SetItem(4, before[4] with { IsEnabled = true }) },
            "before-redo-disabled" => evidence with { BeforeControls = before.SetItem(5, before[5] with { IsEnabled = false }) },
            "after-undo-disabled" => evidence with { AfterControls = after.SetItem(4, after[4] with { IsEnabled = false }) },
            "after-redo-enabled" => evidence with { AfterControls = after.SetItem(5, after[5] with { IsEnabled = true }) },
            "foreign-process" => evidence with { AfterControls = after.SetItem(2, after[2] with { ProcessId = 999 }) },
            "wrong-id" => evidence with { AfterControls = after.SetItem(2, after[2] with { AutomationId = "wrong.btnSod2" }) },
            "wrong-class" => evidence with { AfterControls = after.SetItem(2, after[2] with { ClassName = "Other" }) },
            "no-toggle" => evidence with { AfterControls = after.SetItem(2, after[2] with { SupportedPatterns = [UiPatternKind.Invoke] }) },
            "no-invoke" => evidence with { AfterControls = after.SetItem(2, after[2] with { SupportedPatterns = [UiPatternKind.Toggle] }) },
            "wrong-history-id" => evidence with { AfterControls = after.SetItem(4, after[4] with { AutomationId = "other.undoButton" }) },
            "wrong-undo-name" => evidence with { BeforeControls = before.SetItem(4, before[4] with { Name = "other" }) },
            "wrong-redo-name" => evidence with { AfterControls = after.SetItem(5, after[5] with { Name = "other" }) },
            "multiple-selected" => evidence with { AfterControls = after.SetItem(1, after[1] with { IsSelected = true }) },
            "offscreen" => evidence with { AfterControls = after.SetItem(2, after[2] with { IsOffscreen = true }) },
            "empty-bounds" => evidence with { AfterControls = after.SetItem(2, after[2] with { Bounds = new UiBounds(0, 0, 0, 0) }) },
            "disabled-owner" => evidence with { AfterControls = after.SetItem(2, after[2] with { IsEnabled = false }) },
            "before-selected-result" => evidence with { BeforeControls = after },
            "after-selected-closed" => evidence with { AfterControls = before },
            "missing-control" => evidence with { AfterControls = after.RemoveAt(5) },
            "extra-control" => evidence with { AfterControls = after.Add(after[0]) },
            "unknown-selection" => evidence with { AfterControls = after.SetItem(1, after[1] with { IsSelected = null }) },
            _ => throw new InvalidOperationException(defect),
        };
        MeituRecognitionReapplyRule.IsValid(evidence, 4242, "A_副本", signature).ShouldBeFalse();
    }

    [Theory]
    [InlineData("missing-before-marker")]
    [InlineData("missing-after-marker")]
    [InlineData("before-busy")]
    [InlineData("before-state-busy")]
    [InlineData("wrong-identity")]
    [InlineData("different-title")]
    [InlineData("modal")]
    [InlineData("disabled-window")]
    [InlineData("invalid-recognizer")]
    public void Broken_snapshot_evidence_cannot_correlate_a_result(string defect)
    {
        var signature = Signature();
        var evidence = MeituRecognitionReapplyEvidenceFixture.Create(signature);
        var before = evidence.Before.Observation;
        var after = evidence.After.Observation;
        evidence = defect switch
        {
            "missing-before-marker" => evidence with { Before = evidence.Before with { Observation = before with { VisibleTexts = before.VisibleTexts.RemoveAt(0) } } },
            "missing-after-marker" => evidence with { After = evidence.After with { Observation = after with { VisibleTexts = after.VisibleTexts.RemoveAt(0) } } },
            "before-busy" => evidence with { Before = evidence.Before with { Observation = before with { VisibleTexts = [.. before.VisibleTexts, "智能识别中...", "取消"] } } },
            "before-state-busy" => evidence with { Before = evidence.Before with { State = MeituStartingState.Busy } },
            "wrong-identity" => evidence with { After = evidence.After with { Observation = after with { ObservedDocumentIdentity = "B_副本" } } },
            "different-title" => evidence with { After = evidence.After with { Observation = after with { WindowTitle = "other editor" } } },
            "modal" => evidence with { Before = evidence.Before with { Observation = before with { OwnedDialogTitles = ["Blocking"] } } },
            "disabled-window" => evidence with { After = evidence.After with { Observation = after with { MainWindowEnabled = false } } },
            "invalid-recognizer" => evidence with { RecognizerIndex = 0 },
            _ => throw new InvalidOperationException(defect),
        };
        MeituRecognitionReapplyRule.IsValid(evidence, 4242, "A_副本", signature).ShouldBeFalse();
    }
}
