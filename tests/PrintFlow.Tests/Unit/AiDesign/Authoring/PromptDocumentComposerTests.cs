using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.Tests.Unit.AiDesign.Authoring;

public sealed class PromptDocumentComposerTests
{
    [Theory]
    [InlineData("Header\nfirst\nsecond (keep note)\nFooter", "", "Header\n (keep note)\nFooter")]
    [InlineData("Header\nfirst\nsecond (keep note)\nFooter", "new first\nnew second\n", "Header\nnew first\nnew second (keep note)\nFooter")]
    [InlineData("Header\nfirst (keep note)\nsecond\nfirst\nFooter", "", "Header\n (keep note)\nfirst\nFooter")]
    public void Live_change_updates_multiline_field_while_keeping_an_inline_note(string current, string value, string expected)
    {
        static AiPromptDocument Document(string text) => new([new AiTextPart(Guid.NewGuid(), text)]);
        var before = Document("Header\nfirst\nsecond\nFooter");
        var edited = Document(current);
        var after = Document("Header\n" + value + "Footer");

        PromptFieldSynchronization.Update(edited, before, after).Parts.OfType<AiTextPart>()
            .Select(part => part.Text).ShouldBe([expected]);
    }

    [Fact]
    public void Live_change_uses_distinct_anchors_across_repeated_blank_lines()
    {
        static AiPromptDocument Document(string text) => new([new AiTextPart(Guid.NewGuid(), text)]);
        const string before = "Header\n\nAlpha\n\nOld\n\nFooter";
        string after = before.Replace("Old", "New", StringComparison.Ordinal);
        var updated = PromptFieldSynchronization.Update(Document(before + "\nmanual"), Document(before), Document(after));
        updated.Parts.OfType<AiTextPart>().Select(part => part.Text).ShouldBe([after + "\nmanual"]);
    }

    [Fact]
    public void Live_change_can_omit_a_line_with_a_reference_while_keeping_manual_text_and_other_images()
    {
        var template = Template("Header\r\n{{Date}} {{Photo}}\r\nFooter {{Layout}}",
            new AiTemplateField("Date", AiTemplateFieldKind.OptionalDateLine, false),
            new AiTemplateField("Photo", AiTemplateFieldKind.ImageReference, false, SlotId: "photo"),
            new AiTemplateField("Layout", AiTemplateFieldKind.ImageReference, false, SlotId: "layout"));
        var before = PromptDocumentComposer.Compose(template, new Dictionary<string, string> { ["Date"] = "1948" }).Document;
        var after = PromptDocumentComposer.Compose(template, new Dictionary<string, string>()).Document;
        var layout = before.Parts.OfType<AiImagePart>().Single(x => x.SlotId == "layout");
        var edited = new AiPromptDocument(before.Parts.Append(new AiTextPart(Guid.NewGuid(), "\r\n  Manual  ")).ToArray());

        var updated = PromptFieldSynchronization.Update(edited, before, after);

        updated.Parts.OfType<AiImagePart>().ShouldHaveSingleItem().ShouldBe(layout);
        string.Concat(updated.Parts.OfType<AiTextPart>().Select(x => x.Text)).ShouldBe("Header\r\nFooter \r\n  Manual  ");
    }

    [Fact]
    public void Compose_preserves_operator_literals()
    {
        const string literal = "  ʻĀna\r\n陈 {{姓名}} e\u0301  ";
        var template = Template("{{姓名}}", new AiTemplateField("姓名", AiTemplateFieldKind.Text, true));

        var result = PromptDocumentComposer.Compose(template, new Dictionary<string, string> { ["姓名"] = literal });

        Text(result).ShouldBe(literal);
        result.Issues.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Missing_dates_omit_whole_label_line(string newline)
    {
        var template = Template($"姓名：{{{{姓名}}}}{newline}SUNRISE: {{{{出生日期行}}}}{newline}{{{{离世日期行}}}}{newline}结尾",
            new AiTemplateField("姓名", AiTemplateFieldKind.Text, true),
            new AiTemplateField("出生日期行", AiTemplateFieldKind.OptionalDateLine, false, DateLabel: "SUNRISE"),
            new AiTemplateField("离世日期行", AiTemplateFieldKind.OptionalDateLine, false, DateLabel: "SUNSET"));

        var result = PromptDocumentComposer.Compose(template, new Dictionary<string, string>
        {
            ["姓名"] = "Āna", ["出生日期行"] = "", ["离世日期行"] = "  "
        });

        Text(result).ShouldBe($"姓名：Āna{newline}结尾");
        Text(result).ShouldNotContain("SUNRISE");
        Text(result).ShouldNotContain("SUNSET");
        result.Issues.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("{{日期}}\r\n其余", "其余")]
    [InlineData("其余\nSUNSET: {{日期}}", "其余\n")]
    [InlineData("{{日期}}", "")]
    public void Missing_date_can_remove_first_last_or_only_line(string body, string expected)
    {
        var result = PromptDocumentComposer.Compose(Template(body,
            new AiTemplateField("日期", AiTemplateFieldKind.OptionalDateLine, false, DateLabel: "SUNSET")),
            new Dictionary<string, string>());

        Text(result).ShouldBe(expected);
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void Present_date_uses_exact_label_override_and_literal_value()
    {
        var result = PromptDocumentComposer.Compose(Template("{{日期}}\r\n结尾",
            new AiTemplateField("日期", AiTemplateFieldKind.OptionalDateLine, false, DateLabel: "SUNRISE")),
            new Dictionary<string, string>
            {
                ["日期"] = " 12/06/1039 {{姓名}} ", ["日期.label"] = "  NÉE  "
            });

        Text(result).ShouldBe("  NÉE  :  12/06/1039 {{姓名}} \r\n结尾");
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_variable_remains_visible()
    {
        var result = PromptDocumentComposer.Compose(Template("采用{{未定义}}；{{未定义}}"),
            new Dictionary<string, string> { ["未定义"] = "must not substitute undeclared input" });

        Text(result).ShouldBe("采用{{未定义}}；{{未定义}}");
        result.Issues.ShouldContain(x => x.FieldKey == "未定义");
    }

    [Fact]
    public void Empty_text_remains_visible_instead_of_producing_a_broken_instruction()
    {
        var result = PromptDocumentComposer.Compose(Template("姓名：{{姓名}}；补充：{{补充}}",
            new AiTemplateField("姓名", AiTemplateFieldKind.Text, true, "not an explicit empty override"),
            new AiTemplateField("补充", AiTemplateFieldKind.Text, false)),
            new Dictionary<string, string> { ["姓名"] = " " });

        Text(result).ShouldBe("姓名：{{姓名}}；补充：{{补充}}");
        result.Issues.Select(x => x.FieldKey).ShouldBe(new[] { "姓名", "补充" });
    }

    [Fact]
    public void Defaults_are_substituted_once_and_field_names_are_case_sensitive()
    {
        var result = PromptDocumentComposer.Compose(Template("{{Name}}|{{name}}|{{规格}}",
            new AiTemplateField("Name", AiTemplateFieldKind.Text, true),
            new AiTemplateField("规格", AiTemplateFieldKind.Text, false, "280×390mm {{Name}}")),
            new Dictionary<string, string> { ["Name"] = "{{规格}}" });

        Text(result).ShouldBe("{{规格}}|{{name}}|280×390mm {{Name}}");
        result.Issues.ShouldHaveSingleItem().FieldKey.ShouldBe("name");
    }

    [Fact]
    public void Image_variable_creates_slot_not_a_path()
    {
        var template = Template("人物采用{{人物参考}}，再使用{{人物参考}}。",
            new AiTemplateField("人物参考", AiTemplateFieldKind.ImageReference, false, SlotId: "person-photo",
                Semantic: AiTemplateSemantic.PersonImage));
        var fields = new Dictionary<string, string> { ["人物参考"] = @"C:\customer\portrait.png" };

        var result = PromptDocumentComposer.Compose(template, fields);

        var images = result.Document.Parts.OfType<AiImagePart>().ToArray();
        images.Length.ShouldBe(2);
        images[0].SlotId.ShouldBe("person-photo");
        images[1].SlotId.ShouldBe("person-photo");
        images[0].PartId.ShouldNotBe(images[1].PartId);
        Text(result).ShouldBe("人物采用，再使用。");
        result.Issues.ShouldBeEmpty();
        PromptDocumentComposer.Compose(template, fields).Document.Parts.OfType<AiImagePart>()
            .First().SlotId.ShouldBe("person-photo");
    }

    [Fact]
    public void Image_without_a_slot_remains_an_unresolved_visible_reference()
    {
        var result = PromptDocumentComposer.Compose(Template("人物采用{{人物参考}}",
            new AiTemplateField("人物参考", AiTemplateFieldKind.ImageReference, false)), new Dictionary<string, string>());

        Text(result).ShouldBe("人物采用{{人物参考}}");
        result.Document.Parts.OfType<AiImagePart>().ShouldBeEmpty();
        result.Issues.ShouldHaveSingleItem().FieldKey.ShouldBe("人物参考");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  原文\r\n e\u0301 {{unfinished }\n普通正文  ")]
    public void Body_without_variables_is_a_valid_verbatim_document(string body)
    {
        var result = PromptDocumentComposer.Compose(Template(body), new Dictionary<string, string>());

        Text(result).ShouldBe(body);
        result.Issues.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en")]
    public void Builtin_templates_compose_all_three_products_without_customer_defaults(string language)
    {
        var templates = BuiltInAiTemplates.ForLanguage(language);
        templates.Select(x => x.Reference.Id).ShouldBe(new[]
        {
            "memorial-tshirt-quick", "memorial-badge-quick", "memorial-text-change-quick"
        });
        templates.Select(x => x.Content.Product).ShouldBe(new[]
        {
            AiProductKind.MemorialTshirt, AiProductKind.MemorialBadge, AiProductKind.TextChange
        });

        foreach (var template in templates)
        {
            var values = template.Content.Fields.Where(x => x.Kind == AiTemplateFieldKind.Text)
                .ToDictionary(x => x.Key, _ => "  ʻĀna 陈 {{姓名}}  ", StringComparer.Ordinal);
            var result = PromptDocumentComposer.Compose(template.Content, values);
            Text(result).ShouldContain("  ʻĀna 陈 {{姓名}}  ");
            Text(result).ShouldNotContain("SUNRISE");
            Text(result).ShouldNotContain("SUNSET");
            result.Issues.ShouldBeEmpty();
            result.Document.Parts.OfType<AiImagePart>().ShouldNotBeEmpty();
            template.Content.Fields.Where(x => x.Semantic is AiTemplateSemantic.Name or
                AiTemplateSemantic.BirthDate or AiTemplateSemantic.DeathDate || x.Kind == AiTemplateFieldKind.ImageReference)
                .ShouldAllBe(x => x.DefaultValue == null);
            if (template.Content.Product != AiProductKind.TextChange)
            {
                template.Content.Fields.Count(x => x.Semantic == AiTemplateSemantic.Name).ShouldBe(1);
                template.Content.Fields.Count(x => x.Semantic == AiTemplateSemantic.BirthDate).ShouldBe(1);
                template.Content.Fields.Count(x => x.Semantic == AiTemplateSemantic.DeathDate).ShouldBe(1);
                template.Content.Fields.Count(x => x.Semantic == AiTemplateSemantic.PersonImage).ShouldBe(1);
            }
            else
            {
                result.Document.Parts.OfType<AiImagePart>().ShouldHaveSingleItem().SlotId.ShouldBe("edit-source");
            }
        }
        Text(PromptDocumentComposer.Compose(templates[1].Content, new Dictionary<string, string>()))
            .ShouldContain(language == "en" ? "confirm" : "确认");
        BuiltInAiTemplates.All.Count.ShouldBe(3);
    }

    private static AiTemplateContent Template(string body, params AiTemplateField[] fields) =>
        new("test", AiProductKind.MemorialTshirt, body, fields);

    private static string Text(AiPromptComposition composition) =>
        string.Concat(composition.Document.Parts.OfType<AiTextPart>().Select(x => x.Text));
}
