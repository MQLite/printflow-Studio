using System.Globalization;
using PrintFlow.App.Resources;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// Order-sensitive regression for the combined-run language leak: the language-switching tests
/// run first, in one fixed order, and a test that relies on the ambient UI culture runs after them.
/// </summary>
/// <remarks>
/// Before the fix, both producer classes restored the resolved <see cref="OperatorCulture.Current"/>
/// as an explicit selection, and one also left <see cref="CultureInfo.DefaultThreadCurrentUICulture"/>
/// set by <c>LocalisationService.Use</c>; about 25 later tests then read the wrong language.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class OperatorCultureIsolationTests
{
    [Fact]
    public async Task Language_tests_leave_the_ambient_culture_in_charge_for_the_next_test()
    {
        CultureInfo? selectedBefore = OperatorCultureScope.RawSelection;
        CultureInfo? defaultBefore = CultureInfo.DefaultThreadCurrentUICulture;
        selectedBefore.ShouldBeNull("the application never unselects, so a test starting with a selection inherited a leak");

        // Producers: the actual test methods, including ones that call LocalisationService.Use.
        WorkflowPurposeCardTests cards = new();
        await cards.Every_card_explains_purpose_result_and_dimensions_without_clipping("zh-CN", 1000, 700);
        cards.Language_switch_refreshes_existing_cards_without_selecting_or_replacing_them();
        OperatorWave1Tests wave1 = new();
        await wave1.Active_crop_panel_asks_for_the_selection_not_to_begin_cropping_again("en");
        await wave1.New_review_focus_is_nonactivating_refresh_preserves_focus_and_mouse_remainders_are_consumed();

        OperatorCultureScope.RawSelection.ShouldBeNull();
        CultureInfo.DefaultThreadCurrentUICulture.ShouldBe(defaultBefore);

        // Consumer: the pattern the leaked-into tests use — set the ambient UI culture, read a string.
        CultureInfo ambient = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            Strings.Home_RecentNoThumbnail.ShouldBe("无预览");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Strings.Home_RecentNoThumbnail.ShouldBe("No preview");
        }
        finally
        {
            CultureInfo.CurrentUICulture = ambient;
        }
    }

    [Fact]
    public void The_scope_restores_the_exact_prior_state_even_when_the_test_throws()
    {
        CultureInfo? selectedBefore = OperatorCultureScope.RawSelection;
        CultureInfo? defaultBefore = CultureInfo.DefaultThreadCurrentUICulture;
        CultureInfo uiBefore = CultureInfo.CurrentUICulture;
        using (new OperatorCultureScope())
        {
            OperatorCulture.Select(CultureInfo.GetCultureInfo("en"));
            Should.Throw<InvalidOperationException>(() =>
            {
                using OperatorCultureScope inner = new("zh-CN");
                CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
                throw new InvalidOperationException("test failure inside the scope");
            });

            // A prior explicit selection is restored as that selection, not replaced by null.
            OperatorCultureScope.RawSelection!.Name.ShouldBe("en");
            CultureInfo.DefaultThreadCurrentUICulture.ShouldBe(defaultBefore);
            CultureInfo.CurrentUICulture.ShouldBe(uiBefore);
        }

        OperatorCultureScope.RawSelection.ShouldBe(selectedBefore);
    }
}
