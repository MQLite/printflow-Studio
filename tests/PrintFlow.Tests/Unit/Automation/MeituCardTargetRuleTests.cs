using System.Collections.Immutable;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Unit.Automation;

/// <summary>
/// The structural targeting rule (Epic 11300 Part B1 §3, §4, §5, §20).
/// </summary>
/// <remarks>
/// Pure-function tests over records, for the same reason the classifier's are: every case that
/// matters here is a <i>refusal</i>, and a refusal is only worth trusting if the situation that
/// must produce it can be constructed. Constructing "two structurally valid cards" or "an
/// ancestor in another process" against a live Meitu is not something a test run can arrange;
/// against records it is three lines.
/// </remarks>
public sealed class MeituCardTargetRuleTests
{
    private const int MeituPid = 4242;
    private const string Marker = "图片编辑";
    private const string CardId = "StartupWidget.wStartup.funcArea.functionWidget.CardButton";

    private static MeituCardShape Shape() => MeituFakes.CardShape();

    /// <summary>The label as Meitu really reports it: a Text/QLabel that advertises Invoke.</summary>
    private static UiElementIdentity Label(
        string name = Marker,
        string? automationId = null,
        int processId = MeituPid,
        string controlType = "Text",
        string className = "QLabel") =>
        new(controlType, automationId ?? CardId + ".titleLabel", name, className, processId,
            [UiPatternKind.Invoke], new UiBounds(880, 535, 112, 22), IsEnabled: true, IsOffscreen: false);

    /// <summary>The card as Meitu really reports it: a CheckBox/CardButton with Invoke and Toggle.</summary>
    private static UiElementIdentity Card(
        string? automationId = null,
        int processId = MeituPid,
        string controlType = "CheckBox",
        string className = "CardButton",
        ImmutableArray<UiPatternKind>? patterns = null,
        bool enabled = true,
        bool offscreen = false,
        UiBounds? bounds = null) =>
        new(controlType, automationId ?? CardId, string.Empty, className, processId,
            patterns ?? [UiPatternKind.Invoke, UiPatternKind.Value, UiPatternKind.Toggle],
            bounds ?? new UiBounds(812, 520, 196, 70), enabled, offscreen);

    private static OperationResult<int> Select(params MeituCardCandidate[] candidates) =>
        MeituCardTargetRule.SelectOwningCard(Shape(), Marker, MeituPid, candidates);

    // -----------------------------------------------------------------------------
    // Valid (§20)
    // -----------------------------------------------------------------------------

    [Fact]
    public void The_signed_marker_resolves_to_the_card_that_owns_it()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(Label(), Card()));

        chosen.IsSuccess.ShouldBeTrue();
        chosen.Value.ShouldBe(0);
    }

    // -----------------------------------------------------------------------------
    // The Part A defect: the title label is not the target (§20)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The label itself is never the target, however invokable it looks.
    /// </summary>
    /// <remarks>
    /// This is the defect Part B1 exists to fix, stated as a test. Meitu's title label is a
    /// <c>Text</c> element that advertises <c>InvokePattern</c>, so invoking it succeeds and does
    /// nothing — the worst possible failure mode, because it reports success. A candidate whose
    /// "owner" is the label itself must be refused even though the label satisfies every
    /// pattern-based check one might write.
    /// </remarks>
    [Fact]
    public void The_title_label_is_never_treated_as_the_card_even_though_it_advertises_Invoke()
    {
        UiElementIdentity label = Label();
        label.Supports(UiPatternKind.Invoke).ShouldBeTrue();

        OperationResult<int> chosen = Select(new MeituCardCandidate(label, label));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Code.ShouldBe(FailureCode.MeituOpenInputFailed);
        chosen.Failure.Context["inputSent"].ShouldBe("false");
    }

    /// <summary>
    /// A layout container between the label and the card is refused.
    /// </summary>
    /// <remarks>
    /// In Meitu's Qt tree every ancestor from the label up to the window advertises
    /// <c>InvokePattern</c>, so "walk up to the first invokable ancestor" is not a rule at all.
    /// The panel used here is the real <c>StartupFunctionWidget</c> that sits above the card.
    /// </remarks>
    [Fact]
    public void An_invokable_layout_container_above_the_card_is_refused()
    {
        UiElementIdentity panel = Card(
            automationId: "StartupWidget.wStartup.funcArea.functionWidget",
            controlType: "Group",
            className: "StartupFunctionWidget",
            bounds: new UiBounds(460, 504, 1000, 168));

        OperationResult<int> chosen = Select(new MeituCardCandidate(Label(), panel));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.TechnicalDetail.ShouldContain("图片编辑");
    }

    // -----------------------------------------------------------------------------
    // Ambiguity and absence (§5, §20)
    // -----------------------------------------------------------------------------

    [Fact]
    public void Two_structurally_valid_cards_are_never_chosen_between()
    {
        OperationResult<int> chosen = Select(
            new MeituCardCandidate(Label(), Card()),
            new MeituCardCandidate(Label(), Card()));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Code.ShouldBe(FailureCode.MeituOpenInputFailed);
        chosen.Failure.TechnicalDetail.ShouldContain("will not choose between them");
        chosen.Failure.Context["accepted"].ShouldBe("2");
    }

    [Fact]
    public void A_marker_with_no_reachable_owner_fails_closed()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(Label(), Owner: null));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.TechnicalDetail.ShouldContain("None of the 1");
        chosen.Failure.Context["rejections"].ShouldContain("no owning element was reachable");
    }

    [Fact]
    public void No_marker_at_all_fails_closed()
    {
        OperationResult<int> chosen = Select();

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Code.ShouldBe(FailureCode.MeituOpenInputFailed);
        chosen.Failure.Context["candidates"].ShouldBe("0");
    }

    // -----------------------------------------------------------------------------
    // Wrong process (§5, §20)
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_card_belonging_to_another_process_is_refused()
    {
        OperationResult<int> chosen = Select(
            new MeituCardCandidate(Label(), Card(processId: 9999)));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("owner belongs to process 9999");
    }

    [Fact]
    public void A_marker_belonging_to_another_process_is_refused()
    {
        OperationResult<int> chosen = Select(
            new MeituCardCandidate(Label(processId: 9999), Card(processId: 9999)));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("marker belongs to process 9999");
    }

    // -----------------------------------------------------------------------------
    // The id relation, which is what makes this a structural rule (§4)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A card of the right shape that does not own <i>this</i> marker is refused.
    /// </summary>
    /// <remarks>
    /// The case that matters most on the real start page. All eight cards share the automation id
    /// <c>…functionWidget.CardButton</c>, so shape checks alone are satisfied by 海报设计 and
    /// 批处理 just as well as by 图片编辑. What separates them is that the marker's id must be the
    /// card's id plus the label segment — a relation between the two elements rather than a
    /// property of either.
    /// </remarks>
    [Fact]
    public void A_correctly_shaped_card_that_does_not_own_this_marker_is_refused()
    {
        UiElementIdentity otherCard = Card(automationId: "StartupWidget.wStartup.other.CardButton");

        OperationResult<int> chosen = Select(new MeituCardCandidate(Label(), otherCard));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("is not owner id");
    }

    // -----------------------------------------------------------------------------
    // Name matching (§22)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The marker name is matched exactly, never by prefix or substring.
    /// </summary>
    /// <remarks>
    /// Part A's classifier defect was an accepted prefix. The same mistake in the targeting rule
    /// would let a label whose text merely starts with the signed marker carry an invoke.
    /// </remarks>
    [Fact]
    public void A_name_that_only_starts_with_the_signed_marker_is_refused()
    {
        OperationResult<int> chosen = Select(
            new MeituCardCandidate(Label(name: Marker + "历史"), Card()));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("not exactly");
    }

    // -----------------------------------------------------------------------------
    // The remaining shape checks (§4)
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_card_that_does_not_expose_the_required_pattern_is_refused()
    {
        OperationResult<int> chosen = Select(
            new MeituCardCandidate(Label(), Card(patterns: [UiPatternKind.Value])));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("does not support Invoke");
    }

    [Fact]
    public void A_disabled_or_offscreen_card_is_refused()
    {
        Select(new MeituCardCandidate(Label(), Card(enabled: false)))
            .Failure.Context["rejections"].ShouldContain("owner is disabled");

        Select(new MeituCardCandidate(Label(), Card(offscreen: true)))
            .Failure.Context["rejections"].ShouldContain("owner is offscreen");
    }

    /// <summary>
    /// A card whose rectangle does not enclose the marker's is refused.
    /// </summary>
    /// <remarks>
    /// The bounds are evidence, never a click target — nothing turns them back into a
    /// coordinate. They are checked because a control that genuinely owns a label draws around
    /// it, and enclosure is a relation that survives the window being moved or resized.
    /// </remarks>
    [Fact]
    public void A_card_that_does_not_enclose_its_marker_is_refused()
    {
        OperationResult<int> chosen = Select(
            new MeituCardCandidate(Label(), Card(bounds: new UiBounds(0, 0, 10, 10))));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("do not enclose marker bounds");
    }

    [Fact]
    public void A_marker_of_the_wrong_control_type_or_class_is_refused()
    {
        Select(new MeituCardCandidate(Label(controlType: "Button"), Card()))
            .Failure.Context["rejections"].ShouldContain("marker control type is Button");

        Select(new MeituCardCandidate(Label(className: "DownPointButton"), Card()))
            .Failure.Context["rejections"].ShouldContain("marker class is 'DownPointButton'");
    }

    // -----------------------------------------------------------------------------
    // The signed-control rule (§21)
    // -----------------------------------------------------------------------------

    private static UiElementIdentity OpenControl(
        string name = "打开图片",
        string automationId = "MainWindow.OpenMaskWidget.backgroundWidget.openWidget.widget_2.openButton",
        string controlType = "Button",
        string className = "QPushButton",
        int processId = MeituPid) =>
        new(controlType, automationId, name, className, processId,
            [UiPatternKind.Invoke, UiPatternKind.Value], new UiBounds(1014, 435, 250, 44),
            IsEnabled: true, IsOffscreen: false);

    private static OperationResult<int> SelectControl(params UiElementIdentity[] candidates) =>
        MeituCardTargetRule.SelectSignedControl(MeituFakes.EditorOpenControl(), MeituPid, candidates);

    [Fact]
    public void The_signed_open_control_resolves_when_every_property_matches()
    {
        SelectControl(OpenControl()).Value.ShouldBe(0);
    }

    /// <summary>
    /// The editor's other <c>openButton</c> is refused.
    /// </summary>
    /// <remarks>
    /// Meitu's editor has a toolbar control whose automation id also ends in <c>openButton</c>
    /// and which carries no automation name. Requiring the exact name <i>and</i> the owning
    /// widget's path segment is what separates the two; either check alone lets the wrong one
    /// through.
    /// </remarks>
    [Fact]
    public void The_editor_toolbar_open_button_is_not_the_signed_open_control()
    {
        OperationResult<int> chosen = SelectControl(OpenControl(
            name: string.Empty,
            automationId: "MainWindow.centralwidget.mainStackedWidget.editPage.titleWidget.editorPage.openButton",
            className: "IconTextOptionButton"));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["inputSent"].ShouldBe("false");
    }

    [Fact]
    public void A_signed_control_matched_twice_is_never_chosen_between()
    {
        OperationResult<int> chosen = SelectControl(OpenControl(), OpenControl());

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.TechnicalDetail.ShouldContain("will not choose between them");
    }

    [Fact]
    public void A_signed_control_in_another_process_is_refused()
    {
        SelectControl(OpenControl(processId: 9999))
            .Failure.Context["rejections"].ShouldContain("belongs to process 9999");
    }

    [Fact]
    public void A_missing_signed_control_fails_closed()
    {
        OperationResult<int> chosen = SelectControl();

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Code.ShouldBe(FailureCode.MeituOpenInputFailed);
    }
}
