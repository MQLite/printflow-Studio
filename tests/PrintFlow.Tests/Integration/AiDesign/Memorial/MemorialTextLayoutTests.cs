using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Infrastructure.AiDesign.Memorial;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Tests.Integration.AiDesign.Memorial;

public sealed class MemorialTextLayoutTests
{
    [Fact]
    public void Name_macrons_apostrophe_and_case_survive_layout()
    {
        var draft = Draft("ʻĀna  DEMO 陈"); var styles = Styles(draft);
        IMemorialTextLayout layout = new WpfMemorialTextLayout();
        var result = layout.Arrange(draft, Canvas, styles);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        result.Value.Single().Text.ShouldBe("ʻĀna  DEMO 陈");
        result.Value.Single().FontSizeMm.ShouldBe(styles[0].FontSizeMm);
        Should.Throw<NotSupportedException>(() => ((IList<MemorialTextLine>)result.Value).Clear());
    }

    [Fact]
    public void Long_name_offers_layout_change_instead_of_truncation()
    {
        var text = new string('W', 150); var draft = Draft(text);
        var result = new WpfMemorialTextLayout().Arrange(draft, Canvas, [Style() with { Bounds = new(10, 10, 20, 15) }]);
        result.IsFailure.ShouldBeTrue(); result.Failure.Context["issue"].ShouldBe("TextDoesNotFit");
        result.Failure.Context["field"].ShouldBe("name");
        draft.Fields["姓名"].ShouldBe(text);
    }

    [Fact]
    public void Missing_glyph_blocks_confirmation()
    {
        var draft = Draft("NAME \u0378");
        var result = new WpfMemorialTextLayout().Arrange(draft, Canvas, Styles(draft));
        result.IsFailure.ShouldBeTrue(); result.Failure.Context["issue"].ShouldBe("MissingGlyph");
    }

    [Fact]
    public void Arc_changes_positions_not_text()
    {
        const string name = "ʻĀna e\u0301 OFFICE"; var draft = Draft(name); var engine = new WpfMemorialTextLayout();
        var flat = engine.Arrange(draft, Canvas, Styles(draft)).Value.Single();
        var arc = engine.Arrange(draft, Canvas, [Style() with { Placement = MemorialTextPlacement.Arc, ArcRadiusMm = 40 }]).Value.Single();
        arc.Text.ShouldBe(flat.Text); arc.Text.ShouldBe(name);
        var a = engine.Prepare(flat).Value; var b = engine.Prepare(arc).Value;
        b.Clusters.Any(x => Math.Abs(x.AngleDegrees) > 0.01).ShouldBeTrue();
        a.Clusters.All(x => x.AngleDegrees == 0).ShouldBeTrue();
        b.Ink.IsFrozen.ShouldBeTrue();
        b.Ink.Bounds.ShouldNotBe(a.Ink.Bounds);
        b.Clusters.Sum(x => x.GlyphCount).ShouldBe(a.Clusters.Sum(x => x.GlyphCount));
    }

    [Fact]
    public void Combining_sequences_and_ligatures_remain_whole_shaped_clusters()
    {
        const string name = "e\u0301 ffi"; var engine = new WpfMemorialTextLayout();
        var flat = engine.Arrange(Draft(name), Canvas, [Style() with { FontFamily = "Times New Roman" }]).Value.Single();
        var arc = flat with { Placement = MemorialTextPlacement.Arc, ArcRadiusMm = 40 };
        var shaped = engine.Prepare(arc).Value;
        shaped.Clusters.ShouldContain(x => x.SourceStart == 0 && x.SourceLength >= 2);
        shaped.Clusters.ShouldNotContain(x => x.SourceStart == 1);
        var flatClusters = engine.Prepare(flat).Value.Clusters;
        shaped.Clusters.Select(x => (x.SourceStart, x.SourceLength, x.GlyphCount)).ShouldBe(flatClusters.Select(x => (x.SourceStart, x.SourceLength, x.GlyphCount)));
    }

    [Fact]
    public void Explicit_newlines_and_spaces_are_preserved_and_measured()
    {
        const string text = "  ʻĀna e\u0301\r\n陈 DEMO  "; var draft = Draft(text); var engine = new WpfMemorialTextLayout();
        var line = engine.Arrange(draft, Canvas, Styles(draft)).Value.Single();
        line.Text.ShouldBe(text);
        var prepared = engine.Prepare(line).Value;
        prepared.Clusters.Select(x => x.SourceStart).ShouldContain(text.IndexOf('陈'));
        var tooShort = engine.Arrange(draft, Canvas, [Style() with { Bounds = new(10, 10, 100, 4) }]);
        tooShort.IsFailure.ShouldBeTrue(); tooShort.Failure.Context["issue"].ShouldBe("TextDoesNotFit");
        var arc = engine.Arrange(draft, Canvas, [Style() with { Placement = MemorialTextPlacement.Arc, ArcRadiusMm = 40 }]);
        arc.IsFailure.ShouldBeTrue(); arc.Failure.Context["issue"].ShouldBe("ArcRequiresSingleLine");
    }

    [Fact]
    public void Dates_and_custom_labels_use_the_same_current_content_confirmation()
    {
        var draft = Draft("Āna", "12/06/1039", "  NAISSANCE e\u0301  "); var engine = new WpfMemorialTextLayout();
        var result = engine.Arrange(draft, Canvas, Styles(draft)); result.IsSuccess.ShouldBeTrue();
        result.Value.Single(x => x.Key == "birth-date").Text.ShouldBe("12/06/1039");
        result.Value.Single(x => x.Key == "date-label:birth-date").Text.ShouldBe("  NAISSANCE e\u0301  ");
        var field = draft.TemplateSnapshot.Fields.Single(x => x.Semantic == AiTemplateSemantic.BirthDate);
        var changed = draft with { Fields = new Dictionary<string, string>(draft.Fields) { [AiTemplateMetadata.LabelKey(draft.TemplateSnapshot, field)] = "CHANGED" } };
        changed = changed with { FinalDocument = PromptDocumentComposer.Compose(changed.TemplateSnapshot, changed.Fields).Document };
        engine.Arrange(changed, Canvas, Styles(changed)).Failure.Context["issue"].ShouldBe("TextUnconfirmed");
        engine.Arrange(draft, Canvas, [Style()]).Failure.Context["issue"].ShouldBe("MissingTextStyle");
    }

    [Theory]
    [InlineData("unconfirmed")]
    [InlineData("reconcile")]
    [InlineData("date-format")]
    public void Pending_text_or_reconciliation_cannot_be_used_for_hidden_composition(string pending)
    {
        var draft = Draft("Āna", pending == "date-format" ? "12/06/1039" : null);
        if (pending == "unconfirmed") draft = draft with { Confirmations = new Dictionary<string, string>() };
        if (pending == "reconcile") draft = draft with { IsManuallyEdited = true, NeedsFieldReconcile = true };
        if (pending == "date-format") draft = draft with { Fields = new Dictionary<string, string>(draft.Fields) { ["出生日期行.format"] = "" } };
        var result = new WpfMemorialTextLayout().Arrange(draft, Canvas, Styles(draft));
        result.IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("font")]
    [InlineData("small")]
    [InlineData("nan")]
    [InlineData("bounds")]
    [InlineData("radius")]
    [InlineData("key")]
    public void Invalid_styles_return_actionable_issues(string invalid)
    {
        var style = invalid switch
        {
            "font" => Style() with { FontFamily = "missing-installed-family-923048" },
            "small" => Style() with { FontSizeMm = 1 },
            "nan" => Style() with { FontSizeMm = double.NaN },
            "bounds" => Style() with { Bounds = new(279, 10, 30, 20) },
            "radius" => Style() with { Placement = MemorialTextPlacement.Arc, ArcRadiusMm = 0 },
            _ => Style() with { Key = "not-a-field" }
        };
        var result = new WpfMemorialTextLayout().Arrange(Draft("Āna"), Canvas, [style]);
        result.IsFailure.ShouldBeTrue(); result.Failure.Context["issue"].ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Optional_dates_omit_both_value_and_label_and_duplicate_styles_are_refused()
    {
        var draft = Draft("Āna"); var engine = new WpfMemorialTextLayout();
        engine.Arrange(draft, Canvas, Styles(draft)).Value.Select(x => x.Key).ShouldBe(["name"]);
        engine.Arrange(draft, Canvas, [Style(), Style()]).Failure.Context["issue"].ShouldBe("DuplicateTextStyle");
        var bad = new[] { Style() with { Key = "date-label:birth-date" } };
        engine.Arrange(draft, Canvas, bad).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Actual_flat_and_arc_outlines_fit_the_reserved_bounds_without_scaling()
    {
        var draft = Draft("ʻĀna OFFICE 陈"); var engine = new WpfMemorialTextLayout();
        foreach (var placement in new[] { MemorialTextPlacement.Flat, MemorialTextPlacement.Arc })
        {
            var style = Style() with { Placement = placement, ArcRadiusMm = placement == MemorialTextPlacement.Arc ? 40 : null };
            var line = engine.Arrange(draft, Canvas, [style]).Value.Single();
            var actual = engine.Prepare(line).Value;
            actual.Ink.Bounds.IsEmpty.ShouldBeFalse();
            new Rect(style.Bounds.X - 1e-6, style.Bounds.Y - 1e-6, style.Bounds.Width + 2e-6, style.Bounds.Height + 2e-6).Contains(actual.Ink.Bounds).ShouldBeTrue();
            line.FontSizeMm.ShouldBe(4);
        }
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    [InlineData(0)]
    public void Malformed_or_hidden_control_text_is_never_silently_dropped(int codeUnit)
    {
        string name = "NAME" + (char)codeUnit;
        var draft = Draft(name);
        var result = new WpfMemorialTextLayout().Arrange(draft, Canvas, Styles(draft));
        result.IsFailure.ShouldBeTrue(); result.Failure.Context["issue"].ShouldBe("InvalidText");
    }

    [Fact]
    public void Trailing_newline_counts_as_an_empty_line_and_trailing_spaces_use_real_advance()
    {
        var engine = new WpfMemorialTextLayout();
        var plain = engine.Prepare(new("name", "A", "Segoe UI", 4, new(0, 0, 100, 6), MemorialTextPlacement.Flat, null));
        plain.IsSuccess.ShouldBeTrue();
        engine.Prepare(new("name", "A\r\n", "Segoe UI", 4, new(0, 0, 100, 6), MemorialTextPlacement.Flat, null)).IsFailure.ShouldBeTrue();
        engine.Prepare(new("name", "A" + new string(' ', 50), "Segoe UI", 4, new(0, 0, 15, 15), MemorialTextPlacement.Flat, null)).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("مُحَمَّد")]
    [InlineData("A\U0001D400e\u0301陈")]
    public void International_shaping_and_surrogate_clusters_survive_both_placements(string name)
    {
        var engine = new WpfMemorialTextLayout(); var draft = Draft(name);
        var flat = engine.Arrange(draft, Canvas, Styles(draft)); flat.IsSuccess.ShouldBeTrue(flat.IsFailure ? flat.Failure.TechnicalDetail : "");
        var arc = engine.Arrange(draft, Canvas, [Style() with { Placement = MemorialTextPlacement.Arc, ArcRadiusMm = 40 }]);
        arc.IsSuccess.ShouldBeTrue(arc.IsFailure ? arc.Failure.TechnicalDetail : "");
        arc.Value.Single().Text.ShouldBe(name);
        var drawing = engine.Prepare(arc.Value.Single()).Value;
        drawing.Ink.Bounds.IsEmpty.ShouldBeFalse();
        if (name.Contains('\uD835')) drawing.Clusters.ShouldContain(x => x.SourceStart == 1 && x.SourceLength >= 2);
    }

    [Fact]
    public void Explicit_font_size_floor_can_be_configured_but_never_automatically_crossed()
    {
        var style = Style() with { FontSizeMm = 1.5 };
        new WpfMemorialTextLayout().Arrange(Draft("Ana"), Canvas, [style]).Failure.Context["issue"].ShouldBe("FontTooSmall");
        var result = new WpfMemorialTextLayout(new(MinimumFontSizeMm: 1)).Arrange(Draft("Ana"), Canvas, [style]);
        result.IsSuccess.ShouldBeTrue(); result.Value.Single().FontSizeMm.ShouldBe(1.5);
    }

    [Theory]
    [InlineData("AVATAR", FlowDirection.LeftToRight)]
    [InlineData("A陈B", FlowDirection.LeftToRight)]
    [InlineData("مُحَمَّد DEMO", FlowDirection.RightToLeft)]
    public void Flat_outline_metrics_match_native_WPF_including_fallback_and_bidi(string text, FlowDirection direction)
    {
        WpfRendering.OnStaThread(() =>
        {
            const double units = 96 / 25.4;
            var reference = new FormattedText(text, CultureInfo.InvariantCulture, direction,
                new Typeface("Segoe UI"), 4 * units, Brushes.Black, 1).BuildGeometry(new Point());
            var actual = new WpfMemorialTextLayout().Prepare(new("name", text, "Segoe UI", 4, new(0, 0, 100, 25), MemorialTextPlacement.Flat, null)).Value;
            actual.Ink.Bounds.Width.ShouldBe(reference.Bounds.Width / units, 0.002);
            actual.Ink.Bounds.Height.ShouldBe(reference.Bounds.Height / units, 0.002);
        });
    }

    [Fact]
    public void Missing_canvas_is_refused_before_shaping()
    {
        var draft = Draft("Ana");
        new WpfMemorialTextLayout().Arrange(draft, null!, Styles(draft)).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void Unicode_line_breaks_preserve_source_and_cannot_silently_become_an_arc(string separator)
    {
        string text = "Ana" + separator + "DEMO"; var draft = Draft(text); var engine = new WpfMemorialTextLayout();
        var flat = engine.Arrange(draft, Canvas, Styles(draft));
        flat.IsSuccess.ShouldBeTrue(flat.IsFailure ? flat.Failure.TechnicalDetail : ""); flat.Value.Single().Text.ShouldBe(text);
        engine.Arrange(draft, Canvas, [Style() with { Placement = MemorialTextPlacement.Arc, ArcRadiusMm = 40 }]).Failure.Context["issue"].ShouldBe("ArcRequiresSingleLine");
    }

    [Fact]
    public void Representative_text_outlines_render_offscreen()
    {
        WpfRendering.OnStaThread(() =>
        {
            var engine = new WpfMemorialTextLayout(); var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 600, 1000));
                dc.PushTransform(new ScaleTransform(4, 4));
                var panels = new[]
                {
                    new MemorialTextLine("name", "ʻĀna e\u0301 DEMO 陈", "Segoe UI", 5, new(8, 12, 134, 28), MemorialTextPlacement.Flat, null),
                    new MemorialTextLine("name", "ʻĀna e\u0301 DEMO 陈", "Segoe UI", 5, new(8, 50, 134, 48), MemorialTextPlacement.Arc, 42),
                    new MemorialTextLine("birth-date", "12/06/1039", "Segoe UI", 4, new(8, 109, 134, 14), MemorialTextPlacement.Flat, null),
                    new MemorialTextLine("date-label:birth-date", "NAISSANCE e\u0301", "Segoe UI", 4, new(8, 128, 134, 14), MemorialTextPlacement.Flat, null),
                    new MemorialTextLine("name", "محمّد!", "Segoe UI", 5, new(8, 156, 134, 25), MemorialTextPlacement.Flat, null),
                    new MemorialTextLine("name", "محمّد (Ali)", "Segoe UI", 5, new(8, 190, 134, 45), MemorialTextPlacement.Arc, 42)
                };
                foreach (var panel in panels)
                {
                    var drawing = engine.Prepare(panel); drawing.IsSuccess.ShouldBeTrue();
                    dc.DrawRectangle(null, new Pen(Brushes.LightGray, 0.2), new Rect(panel.Bounds.X, panel.Bounds.Y, panel.Bounds.Width, panel.Bounds.Height));
                    dc.DrawGeometry(Brushes.Black, null, drawing.Value.Ink);
                }
                dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(600, 1000, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            byte[] pixels = new byte[600 * 1000 * 4]; bitmap.CopyPixels(pixels, 600 * 4, 0);
            Enumerable.Range(0, 600 * 1000).Count(x => pixels[x * 4] < 100 && pixels[x * 4 + 3] > 0).ShouldBeGreaterThan(500);
            string? evidence = Environment.GetEnvironmentVariable("PRINTFLOW_B2_TEXT_EVIDENCE");
            if (evidence is not null)
            {
                Directory.CreateDirectory(evidence);
                using var output = new FileStream(Path.Combine(evidence, $"text-outlines-{Guid.NewGuid():N}.png"), FileMode.CreateNew);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(output);
            }
        });
    }

    [Theory]
    [InlineData("A\u0338")]
    [InlineData("مُحَمَّد")]
    public void Overlapping_strokes_preserve_native_ink_area(string text)
    {
        WpfRendering.OnStaThread(() =>
        {
            const double units = 96 / 25.4;
            var native = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 4 * units, Brushes.Black, 1).BuildGeometry(new Point());
            var actual = new WpfMemorialTextLayout().Prepare(new("name", text, "Segoe UI", 4, new(0, 0, 100, 25), MemorialTextPlacement.Flat, null)).Value;
            actual.Ink.GetArea(0.00001, ToleranceType.Absolute).ShouldBe(native.GetArea(0.00001, ToleranceType.Absolute) / (units * units), 0.005);
        });
    }

    [Fact]
    public void Overflowing_arc_conversion_cannot_return_successful_empty_ink()
    {
        var result = new WpfMemorialTextLayout().Prepare(new("name", "AB", "Segoe UI", 4, new(0, 0, 100, 25), MemorialTextPlacement.Arc, double.MaxValue));
        result.IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("\u200B")]
    [InlineData("\u200D")]
    public void Mandatory_names_with_no_visible_ink_require_explicit_correction(string name)
    {
        var draft = Draft(name); var result = new WpfMemorialTextLayout().Arrange(draft, Canvas, Styles(draft));
        result.IsFailure.ShouldBeTrue(); result.Failure.Context["issue"].ShouldBe("NoVisibleText");
        draft.Fields["姓名"].ShouldBe(name);
    }

    [Theory]
    [InlineData(MemorialTextPlacement.Flat)]
    [InlineData(MemorialTextPlacement.Arc)]
    public void WPF_emergency_breaks_cannot_be_accepted_as_operator_line_breaks(MemorialTextPlacement placement)
    {
        var line = new MemorialTextLine("name", new string('A', 9601), "Segoe UI", 4, new(0, 0, 1_000_000, 200), placement,
            placement == MemorialTextPlacement.Arc ? 1_000_000 : null);
        var result = new WpfMemorialTextLayout(new(MaximumTextLength: 16384)).Prepare(line);
        result.IsFailure.ShouldBeTrue(); result.Failure.Context["issue"].ShouldBe("TextDoesNotFit");
    }

    [Fact]
    public void Memorial_templates_without_a_name_semantic_cannot_produce_empty_success()
    {
        var draft = Draft("Ana");
        var template = draft.TemplateSnapshot with { Body = "No fields", Fields = [] };
        var document = PromptDocumentComposer.Compose(template, new Dictionary<string, string>()).Document;
        draft = draft with { TemplateSnapshot = template, Fields = new Dictionary<string, string>(), InitialDocument = document, FinalDocument = document };
        var result = new WpfMemorialTextLayout().Arrange(draft, Canvas, []);
        result.IsFailure.ShouldBeTrue(); result.Failure.Context["issue"].ShouldBe("MissingNameSemantic");
    }

    [Theory]
    [InlineData("محمّد!", 5)]
    [InlineData("محمّد (Ali)", 7)]
    [InlineData("\u200F123!", 4)]
    public void RTL_sentence_end_and_Latin_suffix_stay_on_the_visual_left(string text, int endIndex)
    {
        var engine = new WpfMemorialTextLayout();
        foreach (var placement in new[] { MemorialTextPlacement.Flat, MemorialTextPlacement.Arc })
        {
            var prepared = engine.Prepare(new("name", text, "Segoe UI", 4, new(0, 0, 100, 25), placement, placement == MemorialTextPlacement.Arc ? 40 : null)).Value;
            int firstIndex = text[0] == '\u200F' ? 1 : 0;
            var first = prepared.Clusters.Single(x => x.SourceStart == firstIndex);
            var end = prepared.Clusters.Single(x => x.SourceStart == endIndex);
            end.OriginMm.X.ShouldBeLessThan(first.OriginMm.X);
            if (placement == MemorialTextPlacement.Arc) end.AngleDegrees.ShouldBeLessThan(first.AngleDegrees);
        }
    }

    private static readonly MemorialCanvas Canvas = MemorialLayoutModule.TshirtPreset;
    private static MemorialTextStyle Style() => new("name", "Segoe UI", 4, new(10, 10, 100, 25), MemorialTextPlacement.Flat, null);
    private static IReadOnlyList<MemorialTextStyle> Styles(AiAuthoringDraft draft) => MemorialLayoutModule.TextInputs(draft)
        .Where(x => x.Key is "name" or "birth-date" or "date-label:birth-date")
        .Select((x, i) => Style() with { Key = x.Key, Bounds = new(10, 10 + i * 30, 100, 25) }).ToArray();

    internal static AiAuthoringDraft Draft(string name, string? date = null, string label = "SUNRISE")
    {
        var template = BuiltInAiTemplates.All.Single(x => x.Content.Product == AiProductKind.MemorialTshirt);
        var fields = new Dictionary<string, string> { ["姓名"] = name };
        if (date is not null)
        {
            fields["出生日期行"] = date; fields["出生日期行.format"] = "dd/MM/yyyy"; fields["出生日期行.label"] = label;
        }
        var composition = PromptDocumentComposer.Compose(template.Content, fields);
        var draft = new AiAuthoringDraft
        {
            Id = Guid.NewGuid(), Template = template.Reference, TemplateSnapshot = template.Content, Product = template.Content.Product,
            Fields = fields, InitialDocument = composition.Document, FinalDocument = composition.Document,
            Slots = template.Content.Fields.Where(x => x.Kind == AiTemplateFieldKind.ImageReference).Select(x => new AiReferenceSlot(x.SlotId!, x.Key, x.Required)).ToArray()
        };
        return draft with { Confirmations = AiDetailedReview.TextFields(draft).ToDictionary(AiAuthoringConfirmationRules.TextKey,
            x => AiAuthoringConfirmationRules.ContentHash(draft, AiAuthoringConfirmationRules.TextKey(x))!) };
    }
}
