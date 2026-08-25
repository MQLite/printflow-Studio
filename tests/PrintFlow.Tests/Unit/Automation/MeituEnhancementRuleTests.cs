using System.Collections.Immutable;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Unit.Automation;

/// <summary>
/// Busy and completion are decided from signed positive markers, never from an absence
/// (Epic 11300 Part B2A §13, §14, §23, §24).
/// </summary>
/// <remarks>
/// The load-bearing test in this file is
/// <see cref="Busy_disappearing_is_not_completion_on_its_own"/>. Everything else here guards a
/// rule that could reasonably be written the wrong way round; that one guards the rule the task
/// forbids outright, and it is the difference between "Meitu finished" and "Meitu stopped
/// telling us anything".
/// </remarks>
public sealed class MeituEnhancementRuleTests
{
    private static MeituObservation Showing(params string[] texts) =>
        new("美图秀秀-图片编辑", [.. texts], [], MainWindowEnabled: true, "A.png");

    private static MeituEnhancementSignature Signature() => MeituFakes.Enhancement();

    // -----------------------------------------------------------------------------
    // Busy (§13, §23)
    // -----------------------------------------------------------------------------

    [Fact]
    public void Signed_positive_Busy_markers_read_as_Busy()
    {
        MeituEnhancementRule.Classify(Signature(), Showing(MeituFakes.BusyTexts()))
            .ShouldBe(MeituEnhancementPhase.Busy);
    }

    /// <summary>Either progress phase satisfies the rule, because each supplies two markers.</summary>
    [Theory]
    [InlineData("ENH-RUNNING")]
    [InlineData("ENH-ELAPSED")]
    public void Either_progress_message_paired_with_the_abort_affordance_reads_as_Busy(string progress)
    {
        MeituEnhancementRule.Classify(Signature(), Showing(progress, "ENH-ABORT"))
            .ShouldBe(MeituEnhancementPhase.Busy);
    }

    [Fact]
    public void Ordinary_editor_content_does_not_read_as_Busy()
    {
        MeituEnhancementRule.IsBusy(Signature(), Showing([.. MeituFakes.EditorMarkers]))
            .ShouldBeFalse();
    }

    /// <summary>
    /// One marker is not two.
    /// </summary>
    /// <remarks>
    /// The abort affordance on its own is the weak half of the signature — a cancel button is a
    /// thing many panels have — so the threshold is what stops it carrying the decision alone.
    /// </remarks>
    [Fact]
    public void A_single_Busy_marker_does_not_reach_the_signed_threshold()
    {
        MeituEnhancementRule.IsBusy(Signature(), Showing("ENH-ABORT")).ShouldBeFalse();
    }

    [Fact]
    public void An_empty_observation_does_not_read_as_Busy()
    {
        MeituEnhancementRule.IsBusy(Signature(), Showing()).ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------
    // Completion (§14, §24)
    // -----------------------------------------------------------------------------

    [Fact]
    public void Signed_positive_completion_markers_with_Busy_gone_read_as_complete()
    {
        MeituEnhancementRule.Classify(Signature(), Showing(MeituFakes.CompletedTexts()))
            .ShouldBe(MeituEnhancementPhase.Complete);
    }

    /// <summary>
    /// The rule §14 names explicitly: Busy going away is not completion.
    /// </summary>
    /// <remarks>
    /// This is the observation a run would produce if Meitu crashed its panel, or if the
    /// automation read came back thin. Without positive markers it must classify as unobserved,
    /// so the caller keeps waiting and eventually reports a timeout — not a finished enhancement.
    /// </remarks>
    [Fact]
    public void Busy_disappearing_is_not_completion_on_its_own()
    {
        MeituEnhancementRule.Classify(Signature(), Showing([.. MeituFakes.EditorMarkers]))
            .ShouldBe(MeituEnhancementPhase.Unobserved);
    }

    [Fact]
    public void Too_few_completion_markers_do_not_reach_the_signed_threshold()
    {
        MeituEnhancementRule.Classify(
            Signature(), Showing("ENH-PANEL-A", "ENH-PANEL-B", "ENH-PANEL-C"))
            .ShouldBe(MeituEnhancementPhase.Unobserved);
    }

    // -----------------------------------------------------------------------------
    // Busy outranks completion (§13, §23)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The panel exists while the work runs, so its markers are visible during Busy.
    /// </summary>
    /// <remarks>
    /// Live observation: the panel and the first progress message appear in the same frame. If
    /// completion won this contest, a caller would stop waiting and send input into a running
    /// operation — which is exactly what §13 forbids.
    /// </remarks>
    [Fact]
    public void Completion_markers_visible_during_Busy_still_read_as_Busy()
    {
        MeituObservation both = Showing([.. MeituFakes.CompletionMarkers, "ENH-RUNNING", "ENH-ABORT"]);

        MeituEnhancementRule.Classify(Signature(), both).ShouldBe(MeituEnhancementPhase.Busy);
        MeituEnhancementRule.IsComplete(Signature(), both, busy: true).ShouldBeFalse();
    }

    /// <summary>A signature that permits overlap says so, and is then honoured.</summary>
    [Fact]
    public void Completion_may_overlap_Busy_only_when_the_evidence_records_that_it_may()
    {
        MeituEnhancementSignature overlapping = Signature() with
        {
            Completion = Signature().Completion with { RequiresBusyAbsent = false },
        };

        MeituEnhancementRule.IsComplete(
            overlapping,
            Showing([.. MeituFakes.CompletionMarkers, "ENH-RUNNING", "ENH-ABORT"]),
            busy: true).ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // Evidence that describes no screen matches no screen (§14)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// An empty marker list matches nothing, not everything.
    /// </summary>
    /// <remarks>
    /// The inversion matters more here than anywhere else in the adapter. Under the usual "all
    /// of an empty set holds" convention, evidence that listed no markers would make every
    /// observation Busy, or every observation complete — the two most dangerous verdicts this
    /// rule can reach, arrived at by saying nothing at all.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void A_signature_with_no_usable_markers_matches_nothing(int minimum)
    {
        ImmutableArray<string> none = [];
        MeituEnhancementSignature broken = Signature() with
        {
            Busy = new MeituBusySignature(none, minimum),
            Completion = new MeituCompletionSignature(none, minimum, RequiresBusyAbsent: true),
        };

        MeituEnhancementRule.IsBusy(broken, Showing(MeituFakes.BusyTexts())).ShouldBeFalse();
        MeituEnhancementRule.Classify(broken, Showing(MeituFakes.CompletedTexts()))
            .ShouldBe(MeituEnhancementPhase.Unobserved);
    }

    /// <summary>A threshold above the marker count is unsatisfiable, and is treated as such.</summary>
    [Fact]
    public void A_threshold_larger_than_the_marker_list_matches_nothing()
    {
        MeituEnhancementSignature broken = Signature() with
        {
            Busy = new MeituBusySignature(MeituFakes.BusyMarkers, MinimumRequiredMarkers: 99),
        };

        MeituEnhancementRule.IsBusy(broken, Showing(MeituFakes.BusyTexts())).ShouldBeFalse();
    }

    /// <summary>
    /// The window title is not a place a Busy or completion marker may be found.
    /// </summary>
    /// <remarks>
    /// Unlike the welcome markers, these are read from automation names only. A title is the one
    /// property of a window anything can claim, and Meitu's editor title does not change while
    /// it computes.
    /// </remarks>
    [Fact]
    public void Markers_in_the_window_title_alone_do_not_count()
    {
        MeituObservation titled = new(
            "ENH-RUNNING ENH-ABORT", [], [], MainWindowEnabled: true, "A.png");

        MeituEnhancementRule.IsBusy(Signature(), titled).ShouldBeFalse();
    }
}
