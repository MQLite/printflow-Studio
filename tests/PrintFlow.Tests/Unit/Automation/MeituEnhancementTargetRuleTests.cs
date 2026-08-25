using System.Collections.Immutable;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Unit.Automation;

/// <summary>
/// The Enhancement target rule: exactly one structurally valid owner, or nothing
/// (Epic 11300 Part B2A §8, §21).
/// </summary>
/// <remarks>
/// Every case here is a refusal except the first, and every refusal asserts
/// <c>inputSent=false</c> in the failure context. That assertion is the point: the rule's job is
/// not to find a control, it is to decline to invent one, and a test that only checked the
/// failure code would pass against a rule that guessed and then apologised.
/// </remarks>
public sealed class MeituEnhancementTargetRuleTests
{
    private const int Pid = 4242;
    private const string ModuleId = "MainWindow.contentsWidget.ModuleButton";

    private static MeituOwnedControlShape Shape() => MeituFakes.Enhancement().ActionControl;

    private static UiElementIdentity Marker(
        string name = MeituFakes.EnhancementMarker,
        string? id = null,
        string controlType = "Text",
        string className = "QLabel",
        int processId = Pid,
        UiBounds? bounds = null) =>
        new(controlType, id ?? ModuleId + ".buttonWidget.titleLabel", name, className, processId,
            [UiPatternKind.Invoke], bounds ?? new UiBounds(494, 634, 67, 24), true, false);

    private static UiElementIdentity Owner(
        string? id = null,
        string controlType = "CheckBox",
        string className = "ModuleButton",
        int processId = Pid,
        bool enabled = true,
        bool offscreen = false,
        UiBounds? bounds = null,
        ImmutableArray<UiPatternKind>? patterns = null) =>
        new(controlType, id ?? ModuleId, string.Empty, className, processId,
            patterns ?? [UiPatternKind.Invoke, UiPatternKind.Value, UiPatternKind.Toggle],
            bounds ?? new UiBounds(452, 618, 260, 56), enabled, offscreen);

    private static OperationResult<int> Select(params MeituCardCandidate[] candidates) =>
        MeituEnhancementTargetRule.SelectActionOwner(
            Shape(), MeituFakes.EnhancementMarker, Pid, candidates);

    // -----------------------------------------------------------------------------
    // The one accepted shape
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_marker_owned_by_a_structurally_valid_control_resolves_to_that_control()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(Marker(), Owner()));

        chosen.IsSuccess.ShouldBeTrue();
        chosen.Value.ShouldBe(0);
    }

    // -----------------------------------------------------------------------------
    // The marker is never the target (§8, §21)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The Part A defect, restated at the depth this rule works at.
    /// </summary>
    /// <remarks>
    /// A marker put forward as its own owner satisfies "is invokable" — Meitu's labels all
    /// advertise <c>InvokePattern</c> — and fails everything else. If this ever passes, PrintFlow
    /// is back to invoking a <c>QLabel</c> and reporting success.
    /// </remarks>
    [Fact]
    public void The_text_marker_is_never_accepted_as_its_own_action_target()
    {
        UiElementIdentity marker = Marker();

        OperationResult<int> chosen = Select(new MeituCardCandidate(marker, marker));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["inputSent"].ShouldBe("false");
        chosen.Failure.Context["rejections"].ShouldContain("not CheckBox");
    }

    /// <summary>
    /// The layout widget between the marker and the control that acts.
    /// </summary>
    /// <remarks>
    /// The real editor nests a <c>QWidget</c> named <c>buttonWidget</c> there, and it advertises
    /// <c>InvokePattern</c>. "Walk up to the first invokable ancestor" would stop here.
    /// </remarks>
    [Fact]
    public void The_intervening_layout_widget_is_refused_even_though_it_advertises_Invoke()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(
            Marker(),
            Owner(id: ModuleId + ".buttonWidget", controlType: "Group", className: "QWidget",
                patterns: [UiPatternKind.Invoke, UiPatternKind.Value])));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["inputSent"].ShouldBe("false");
    }

    // -----------------------------------------------------------------------------
    // Ambiguity is never resolved by picking (§8, §21)
    // -----------------------------------------------------------------------------

    [Fact]
    public void Two_structurally_valid_owners_for_one_marker_refuse_rather_than_choose()
    {
        OperationResult<int> chosen = Select(
            new MeituCardCandidate(Marker(), Owner()),
            new MeituCardCandidate(
                Marker(id: "Other.ModuleButton.buttonWidget.titleLabel"),
                Owner(id: "Other.ModuleButton")));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["accepted"].ShouldBe("2");
        chosen.Failure.Context["inputSent"].ShouldBe("false");
        chosen.Failure.TechnicalDetail.ShouldContain("will not choose between them");
    }

    [Fact]
    public void No_candidate_at_all_refuses_and_says_so()
    {
        OperationResult<int> chosen = Select();

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["candidates"].ShouldBe("0");
        chosen.Failure.Context["inputSent"].ShouldBe("false");
    }

    /// <summary>Two markers of which exactly one has a valid owner is not ambiguous.</summary>
    [Fact]
    public void One_valid_owner_among_several_candidates_resolves_to_it()
    {
        OperationResult<int> chosen = Select(
            new MeituCardCandidate(Marker(), Owner(className: "QWidget")),
            new MeituCardCandidate(Marker(), Owner()));

        chosen.IsSuccess.ShouldBeTrue();
        chosen.Value.ShouldBe(1);
    }

    // -----------------------------------------------------------------------------
    // Process ownership (§8, §21)
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_marker_in_another_process_is_refused()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(Marker(processId: 999), Owner()));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("process 999");
        chosen.Failure.Context["inputSent"].ShouldBe("false");
    }

    [Fact]
    public void An_owner_in_another_process_is_refused()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(Marker(), Owner(processId: 999)));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("process 999");
        chosen.Failure.Context["inputSent"].ShouldBe("false");
    }

    // -----------------------------------------------------------------------------
    // State (§8, §21)
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_disabled_owner_is_refused()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(Marker(), Owner(enabled: false)));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("owner is disabled");
    }

    [Fact]
    public void An_offscreen_owner_is_refused()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(Marker(), Owner(offscreen: true)));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("owner is offscreen");
    }

    [Fact]
    public void An_owner_lacking_the_required_pattern_is_refused()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(
            Marker(), Owner(patterns: [UiPatternKind.Toggle])));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("does not support Invoke");
    }

    [Fact]
    public void An_owner_whose_bounds_do_not_enclose_the_marker_is_refused()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(
            Marker(), Owner(bounds: new UiBounds(0, 0, 40, 40))));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("do not enclose");
    }

    // -----------------------------------------------------------------------------
    // The structural tie between marker and owner (§8)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The check that makes this one relation rather than two shape tests.
    /// </summary>
    /// <remarks>
    /// Both elements here have the right type, class, patterns and process, and the ids even
    /// share a prefix. What they do not share is the exact intervening path the evidence
    /// records, so this owner titles a different module and must be refused.
    /// </remarks>
    [Fact]
    public void An_owner_whose_id_does_not_extend_to_the_marker_id_is_refused()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(
            Marker(id: ModuleId + ".otherWidget.titleLabel"), Owner()));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("plus '.buttonWidget.titleLabel'");
        chosen.Failure.Context["inputSent"].ShouldBe("false");
    }

    [Fact]
    public void An_owner_whose_id_lacks_the_signed_suffix_is_refused()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(
            Marker(id: "MainWindow.contentsWidget.SomethingElse.buttonWidget.titleLabel"),
            Owner(id: "MainWindow.contentsWidget.SomethingElse")));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("does not end with '.ModuleButton'");
    }

    // -----------------------------------------------------------------------------
    // Exact names only (§8)
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData("ENH-ACTION-HISTORY")]
    [InlineData("ENH-ACTIO")]
    [InlineData("enh-action")]
    [InlineData("")]
    public void A_marker_name_that_is_not_exactly_the_signed_one_is_refused(string name)
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(Marker(name: name), Owner()));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["inputSent"].ShouldBe("false");
    }

    /// <summary>
    /// A marker whose ancestor at the signed depth could not be read.
    /// </summary>
    /// <remarks>
    /// The walk stopping short is a refusal, never licence to accept whatever it did reach or to
    /// keep climbing. The failure text names the depth so an upgrade that reshapes the tree says
    /// what changed.
    /// </remarks>
    [Fact]
    public void A_marker_with_no_readable_ancestor_at_the_signed_depth_is_refused()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(Marker(), Owner: null));

        chosen.IsFailure.ShouldBeTrue();
        chosen.Failure.Context["rejections"].ShouldContain("2 level(s) above the marker");
        chosen.Failure.Context["inputSent"].ShouldBe("false");
    }

    /// <summary>
    /// The failure lists every reason, not the first.
    /// </summary>
    /// <remarks>
    /// When a Meitu upgrade moves this control, the useful diagnostic is everything that stopped
    /// matching — otherwise the operator fixes one property and rediscovers the next.
    /// </remarks>
    [Fact]
    public void All_failing_checks_are_reported_together()
    {
        OperationResult<int> chosen = Select(new MeituCardCandidate(
            Marker(), Owner(className: "QWidget", enabled: false, offscreen: true)));

        chosen.IsFailure.ShouldBeTrue();
        string rejections = chosen.Failure.Context["rejections"];
        rejections.ShouldContain("owner class is 'QWidget'");
        rejections.ShouldContain("owner is disabled");
        rejections.ShouldContain("owner is offscreen");
    }
}
