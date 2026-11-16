using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Files;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Tests.Unit.AiDesign.Memorial;

public sealed class MemorialLayoutTests
{
    [Fact]
    public void Badge_ring_is_7mm()
    {
        var canvas = MemorialLayoutModule.BadgePreset;
        ((canvas.OuterDiameterMm!.Value - canvas.SafeDiameterMm!.Value) / 2).ShouldBe(7);
        canvas.ShouldBe(new MemorialCanvas(86, 86, 86, 72));
        MemorialLayoutModule.TshirtPreset.ShouldBe(new MemorialCanvas(280, 390, null, null));
    }

    [Theory]
    [InlineData(7, 43, 1, 1, true)] // center at left edge is insufficient: all four corners matter
    [InlineData(25, 19, 36, 48, false)] // exact four-corner tangency (18²+24²=30²)
    [InlineData(20, 13, 46, 60, true)]
    public void Critical_bounds_must_fit_safe_circle(double x, double y, double w, double h, bool outside)
    {
        var draft = Draft();
        var layout = Layout(draft) with { Text = [Line(draft, new(x, y, w, h))] };
        Keys(draft, layout).Contains("OutsideSafeArea").ShouldBe(outside);
    }

    [Fact]
    public void Corners_exactly_tangent_to_the_safe_circle_are_allowed()
    {
        var draft = Draft(); var layout = Layout(draft);
        Keys(draft, layout with { Text = [Line(draft, new(21.4, 14.2, 43.2, 57.6))] }).ShouldNotContain("OutsideSafeArea");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Omitting_the_person_or_a_preserved_original_is_not_a_valid_layout(bool relabel)
    {
        var draft = Draft(); var layout = Layout(draft);
        if (relabel)
            Keys(draft, layout with { Images = [layout.Images[0] with { Role = MemorialLayerRole.Background }] }).ShouldContain("MissingPersonLayer");
        else
            Keys(draft, layout with { Images = [] }).ShouldContain("MissingPreservedLayer");
    }

    [Fact]
    public void Allowing_crop_does_not_implicitly_allow_zoom()
    {
        var draft = Draft(); var layout = Layout(draft);
        draft = ConfirmRules(draft with { Layout = layout, Preservation = [draft.Preservation[0] with { AllowCrop = true, AllowUniformScale = false }] });
        Keys(draft, layout with { Images = [layout.Images[0] with { SourceCrop = new(10, 10, 80, 80) }] }).ShouldContain("PreservationConflict");
    }

    [Fact]
    public void Null_bounds_return_issues_instead_of_throwing()
    {
        var draft = Draft(); var layout = Layout(draft);
        draft = ConfirmRules(draft with { Layout = layout, Preservation = [draft.Preservation[0] with { AllowMove = false, AllowUniformScale = false }] });
        Keys(draft, layout with { Images = [layout.Images[0] with { Bounds = null! }] }).ShouldContain("InvalidGeometry");
    }

    [Fact]
    public void Medal_cannot_be_cropped_to_make_layout_fit()
    {
        var draft = Draft(); var layout = Layout(draft);
        var medal = layout.Images[0] with { Role = MemorialLayerRole.OriginalDecor, SourceCrop = new(10, 0, 80, 100) };
        Keys(draft, layout with { Images = [medal] }).ShouldContain("PreservationConflict");
    }

    [Fact]
    public void Unconfirmed_template_cannot_be_accepted()
    {
        var draft = Draft();
        Keys(draft, Layout(draft) with { ProductTemplateConfirmed = false }).ShouldContain("ProductTemplateUnconfirmed");
        Keys(draft, Layout(draft)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonfinite_or_nonpositive_geometry_is_rejected(double value)
    {
        var draft = Draft(); var layout = Layout(draft);
        Keys(draft, layout with { Canvas = new(value, 86, 86, 72) }).ShouldContain("InvalidCanvas");
        Keys(draft, layout with { Images = [layout.Images[0] with { Bounds = new(30, 30, value, 20) }] }).ShouldContain("InvalidGeometry");
        Keys(draft, layout with { Text = [layout.Text[0] with { FontSizeMm = value }] }).ShouldContain("InvalidTextStyle");
    }

    [Theory]
    [InlineData(86d, 85d, 86d, 72d)]
    [InlineData(86d, 86d, 86d, 87d)]
    [InlineData(86d, 86d, 86d, null)]
    [InlineData(86d, 86d, null, 72d)]
    public void Badge_dimensions_and_safe_circle_must_agree(double w, double h, double? outer, double? safe)
    {
        var draft = Draft();
        Keys(draft, Layout(draft) with { Canvas = new(w, h, outer, safe) }).ShouldContain("InvalidCanvas");
    }

    [Fact]
    public void Edge_decor_requires_explicit_permission_and_cannot_be_protected_content()
    {
        var draft = Draft(); var layout = Layout(draft); var image = layout.Images[0];
        var edge = image with { Role = MemorialLayerRole.GeneratedDecor, Bounds = new(0, 0, 86, 86) };
        Keys(draft, layout with { Images = [edge] }).ShouldContain("OutsideSafeArea");
        Keys(draft, layout with { Images = [edge with { AllowEdgeExtension = true }] }).ShouldContain("OutsideSafeArea");
        draft = draft with { Preservation = [draft.Preservation[0] with { KeepContent = false }] };
        Keys(draft, layout with { Images = [edge with { AllowEdgeExtension = true }] }).ShouldNotContain("OutsideSafeArea");
    }

    [Fact]
    public void Source_extent_crop_and_aspect_ratio_are_explicit_and_bounded()
    {
        var draft = Draft(); var layout = Layout(draft); var image = layout.Images[0];
        Keys(draft, layout with { Images = [image with { SourceExtent = null }] }).ShouldContain("SourceExtentRequired");
        Keys(draft, layout with { Images = [image with { Bounds = new(30, 30, 10, 20) }] }).ShouldContain("PreservationConflict");
        draft = ConfirmRules(draft with { Preservation = [draft.Preservation[0] with { AllowCrop = true }] });
        Keys(draft, layout with { Images = [image with { SourceCrop = new(90, 0, 20, 100) }] }).ShouldContain("InvalidCrop");
        Keys(draft, layout with { Images = [image with { SourceCrop = new(10, 10, 80, 80) }] }).ShouldBeEmpty();
    }

    [Fact]
    public void Move_and_scale_respect_confirmed_permissions_and_previous_placement()
    {
        var draft = Draft(); var layout = Layout(draft);
        draft = ConfirmRules(draft with { Preservation = [draft.Preservation[0] with { AllowMove = false, AllowUniformScale = false }] });
        Keys(draft, layout).ShouldContain("PreservationConflict");
        draft = draft with { Layout = layout };
        Keys(draft, layout).ShouldBeEmpty();
        Keys(draft, layout with { Images = [layout.Images[0] with { Bounds = new(31, 30, 20, 20) }] }).ShouldContain("PreservationConflict");
        Keys(draft, layout with { Images = [layout.Images[0] with { Bounds = new(30, 30, 21, 21) }] }).ShouldContain("PreservationConflict");
    }

    [Fact]
    public void Text_is_compared_by_semantic_key_without_normalizing_or_truncating()
    {
        var draft = Draft(); var layout = Layout(draft);
        Keys(draft, layout with { Text = [layout.Text[0] with { Text = "ĀNA" }] }).ShouldContain("TextMismatch");
        Keys(draft, layout with { Text = [layout.Text[0] with { Key = "unknown" }] }).ShouldContain("TextMismatch");
        Keys(draft, layout with { Text = [] }).ShouldContain("MissingText");
        Keys(draft, layout with { Text = [layout.Text[0], layout.Text[0]] }).ShouldContain("DuplicateText");
        var longName = new string('Ā', 300) + " e\u0301\r\n陈";
        draft = draft with { Fields = new Dictionary<string, string> { ["姓名"] = longName } };
        var line = Line(draft, new(30, 20, 26, 5));
        Keys(draft, layout with { Text = [line] }).ShouldNotContain("TextMismatch");
        line.Text.ShouldBe(longName); // glyph fitting belongs to B2; B1 never rewrites literal text
    }

    [Fact]
    public void Missing_assets_unconfirmed_permissions_and_relabelling_do_not_bypass_preservation()
    {
        var draft = Draft(); var layout = Layout(draft);
        Keys(draft with { Confirmations = new Dictionary<string, string>() }, layout).ShouldContain("PreservationUnconfirmed");
        Keys(draft, layout with { Images = [layout.Images[0] with { AssetId = Guid.NewGuid() }] }).ShouldContain("MissingAsset");
        Keys(draft, layout with { Images = [layout.Images[0] with { Role = MemorialLayerRole.Background, Bounds = new(0, 0, 86, 86) }] }).ShouldContain("OutsideSafeArea");
    }

    [Fact]
    public void Customized_date_labels_are_exact_required_layers_only_when_the_date_is_present()
    {
        var draft = Draft(); var layout = Layout(draft);
        var birth = draft.TemplateSnapshot.Fields.Single(x => x.Semantic == AiTemplateSemantic.BirthDate);
        const string label = "  NAISSANCE e\u0301\r\n"; const string date = "12/06/1039";
        draft = draft with { Fields = new Dictionary<string, string>(draft.Fields)
        {
            [birth.Key] = date, [AiTemplateMetadata.LabelKey(draft.TemplateSnapshot, birth)] = label
        } };
        var dateLine = new MemorialTextLine("birth-date", date, "Arial", 4, new(30, 55, 26, 4), MemorialTextPlacement.Flat, null);
        var labelLine = dateLine with { Key = MemorialLayoutModule.DateLabelKey(birth), Text = label, Bounds = new(30, 60, 26, 4) };
        Keys(draft, layout with { Text = [layout.Text[0], dateLine] }).ShouldContain("MissingText");
        Keys(draft, layout with { Text = [layout.Text[0], dateLine, labelLine] }).ShouldBeEmpty();
        Keys(draft, layout with { Text = [layout.Text[0], dateLine, labelLine with { Text = label.Trim() }] }).ShouldContain("TextMismatch");
        draft = draft with { Fields = new Dictionary<string, string>(draft.Fields) { [birth.Key] = "" } };
        Keys(draft, layout).ShouldBeEmpty();
        Keys(draft, layout with { Text = [layout.Text[0], labelLine] }).ShouldContain("TextMismatch");
    }

    [Theory]
    [InlineData("crop")]
    [InlineData("move")]
    [InlineData("scale")]
    public void Geometry_denials_apply_independently_of_background_content_permission(string denied)
    {
        var draft = Draft(); var initial = Layout(draft);
        initial = initial with { Images = [initial.Images[0] with { Role = MemorialLayerRole.Background }] };
        draft = ConfirmRules(draft with { Layout = initial, Preservation = [draft.Preservation[0] with
        {
            KeepContent = false, AllowCrop = denied != "crop", AllowMove = denied != "move", AllowUniformScale = denied != "scale"
        }] });
        var image = initial.Images[0];
        var changed = denied switch
        {
            "crop" => image with { SourceCrop = new(10, 10, 80, 80) },
            "move" => image with { Bounds = new(31, 30, 20, 20) },
            _ => image with { Bounds = new(30, 30, 21, 21) }
        };
        Keys(draft, initial with { Images = [changed] }).ShouldContain("PreservationConflict");
    }

    [Fact]
    public void Protected_source_cannot_be_split_into_multiple_roles_even_with_crop_permission()
    {
        var draft = Draft(); var layout = Layout(draft);
        draft = ConfirmRules(draft with { Preservation = [draft.Preservation[0] with { AllowCrop = true }] });
        var left = layout.Images[0] with { SourceCrop = new(0, 0, 50, 50), Bounds = new(28, 30, 10, 10) };
        var right = left with { Role = MemorialLayerRole.OriginalDecor, SourceCrop = new(50, 50, 50, 50), Bounds = new(48, 30, 10, 10) };
        Keys(draft, layout with { Images = [left, right] }).ShouldContain("PreservationConflict");
    }

    internal static AiAuthoringDraft Draft()
    {
        var template = BuiltInAiTemplates.All.Single(x => x.Content.Product == AiProductKind.MemorialBadge);
        var id = Guid.NewGuid(); var assetId = Guid.NewGuid();
        var draft = new AiAuthoringDraft
        {
            Id = id, Product = AiProductKind.MemorialBadge, Template = template.Reference, TemplateSnapshot = template.Content,
            Fields = new Dictionary<string, string> { ["姓名"] = "  ʻĀna e\u0301\r\n陈  " },
            InitialDocument = new([]), FinalDocument = new([new AiImagePart(Guid.NewGuid(), "person-photo")]),
            Slots = [new("person-photo", "person", true)], Bindings = [new("person-photo", assetId)],
            Assets = [new(assetId, 1, "source.png", "attachment-01.png", "person", "image/png", 123,
                Sha256.Parse(new string('A', 64)), new(id, $"Attachments/{assetId:N}/attachment-01.png"))],
            Preservation = [new(assetId, true, true, true, false, false)]
        };
        return ConfirmRules(draft);
    }

    private static AiAuthoringDraft ConfirmRules(AiAuthoringDraft draft) => draft with
    {
        Confirmations = draft.Preservation.ToDictionary(x => AiAuthoringConfirmationRules.PreservationKey(x.AssetId),
            x => AiAuthoringConfirmationRules.ContentHash(draft, AiAuthoringConfirmationRules.PreservationKey(x.AssetId))!)
    };
    internal static MemorialLayout Layout(AiAuthoringDraft draft) => new(MemorialLayoutModule.BadgePreset,
        [new(draft.Assets[0].Id, MemorialLayerRole.Person, new(30, 30, 20, 20), null, 1) { SourceExtent = new(0, 0, 100, 100) }],
        [Line(draft, new(30, 20, 26, 5))], true);
    private static MemorialTextLine Line(AiAuthoringDraft draft, RectMm bounds) => new("name", draft.Fields["姓名"], "Arial", 4, bounds, MemorialTextPlacement.Flat, null);
    private static string[] Keys(AiAuthoringDraft draft, MemorialLayout layout) => MemorialLayoutModule.Validate(draft, layout).Select(x => x.Key).ToArray();
}
