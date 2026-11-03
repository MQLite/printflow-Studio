using PrintFlow.Domain.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public static class BuiltInAiTemplates
{
    public static IReadOnlyList<AiTemplateDefinition> All { get; } = Create(english: false);
    private static IReadOnlyList<AiTemplateDefinition> English { get; } = Create(english: true);

    // Both languages retain the same field keys/semantics and template IDs.
    // Localization selects prose only; it never translates customer input.
    public static IReadOnlyList<AiTemplateDefinition> ForLanguage(string language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? English : All;

    private static IReadOnlyList<AiTemplateDefinition> Create(bool english) => Array.AsReadOnly(new[]
    {
        Define("memorial-tshirt-quick", english ? "Memorial T-shirt" : "人物纪念T恤", AiProductKind.MemorialTshirt,
            english ?
            """
            Create a standalone memorial T-shirt design for the person in {{人物参考}}, referencing the layout and decoration in {{版式参考}}.
            Name: {{姓名}}
            {{出生日期行}}
            {{离世日期行}}
            Design area: {{设计规格}}. This historical preset must be confirmed for the product.
            Preserve the person's face, expression, clothing and chest ornaments; retain specified original decorations without adding or removing them.
            {{补充要求}}
            Keep the outside and specified gaps transparent, with the complete subject and clear edges. Output independent artwork, without a garment or mockup.
            """ :
            """
            为{{人物参考}}中的人物制作纪念T恤独立图案，参考{{版式参考}}的布局与装饰。
            姓名：{{姓名}}
            {{出生日期行}}
            {{离世日期行}}
            设计区域：{{设计规格}}，按此构图意图安排内容；此历史预设仍需确认。
            保持人物面部、表情、服饰和胸前饰物；保留指定原装饰，不擅自增加或删减。
            {{补充要求}}
            图案外围及指定间隙透明，保留完整主体与清晰边缘；输出独立图案，不绘制衣服实物或样机。
            """,
            MemorialFields(new AiTemplateField("设计规格", AiTemplateFieldKind.Text, false, "280×390mm"))),
        Define("memorial-badge-quick", english ? "Round memorial badge" : "圆形纪念徽章", AiProductKind.MemorialBadge,
            english ?
            """
            Create a round memorial badge design for the person in {{人物参考}}, referencing {{版式参考}}.
            Name: {{姓名}}
            {{出生日期行}}
            {{离世日期行}}
            Historical preset: outer canvas {{画布外圆}}, critical-content safe area {{内容安全区}}; confirm the actual product template before use.
            Preserve the person's face, expression, clothing and ornaments; retain specified original decorations.
            Keep the name, face, dates and all required content fully inside the safe area. Extend only the background into the outer ring; keep everything outside the circle transparent.
            {{补充要求}}
            Provide standalone circular artwork, without a physical badge or mockup.
            """ :
            """
            为{{人物参考}}中的人物制作圆形纪念徽章图案，参考{{版式参考}}。
            姓名：{{姓名}}
            {{出生日期行}}
            {{离世日期行}}
            本次历史预设：画布外圆{{画布外圆}}，关键内容安全区{{内容安全区}}；实际产品模板仍需确认。
            保持人物面部、表情、服饰与饰物；沿用指定原装饰。
            姓名、人脸、日期与需完整显示的内容放在安全区内；外围环带只延伸背景，外圆之外透明。
            {{补充要求}}
            提供独立圆形图案，不绘制实物徽章或样机。
            """,
            MemorialFields(new("画布外圆", AiTemplateFieldKind.Text, false, "86mm"),
                new("内容安全区", AiTemplateFieldKind.Text, false, "72mm"))),
        Define("memorial-text-change-quick", english ? "Change name and dates only" : "只改姓名日期", AiProductKind.TextChange,
            english ?
            """
            Change only the specified text in {{待修改图稿}}.
            Original text: {{原文字}}
            Replace with: {{新文字}}
            Use the replacement text verbatim, including case, numbers, symbols and date format.
            Keep all other faces, expressions, clothing, ornaments, decoration, colors and layout unchanged; do not regenerate the whole portrait.
            {{补充要求}}
            Retain the existing transparency and artwork boundary.
            """ :
            """
            修改{{待修改图稿}}中的指定文字。
            原文字：{{原文字}}
            替换为：{{新文字}}
            新文字逐字采用原文，包括大小写、数字、符号与日期格式。
            其余人物面部、表情、衣服、饰物、装饰、配色和布局保持不变；不重新生成整张人物图。
            {{补充要求}}
            保留原有透明区域和图案边界。
            """,
            Array.AsReadOnly(new AiTemplateField[]
            {
                new("待修改图稿", AiTemplateFieldKind.ImageReference, false, SlotId: "edit-source",
                    Semantic: AiTemplateSemantic.EditSourceImage),
                new("原文字", AiTemplateFieldKind.Text, true),
                new("新文字", AiTemplateFieldKind.Text, true),
                new("补充要求", AiTemplateFieldKind.Text, false)
            }))
    });

    private static AiTemplateDefinition Define(string id, string name, AiProductKind product, string body,
        IReadOnlyList<AiTemplateField> fields) => new(new(id, 1), new(name, product, body, fields), id);

    private static IReadOnlyList<AiTemplateField> MemorialFields(params AiTemplateField[] specificationFields) =>
        Array.AsReadOnly(new AiTemplateField[]
        {
            new("姓名", AiTemplateFieldKind.Text, true, Semantic: AiTemplateSemantic.Name),
            new("人物参考", AiTemplateFieldKind.ImageReference, false, SlotId: "person-photo",
                Semantic: AiTemplateSemantic.PersonImage),
            new("版式参考", AiTemplateFieldKind.ImageReference, false, SlotId: "layout-reference",
                Semantic: AiTemplateSemantic.LayoutImage),
            new("出生日期行", AiTemplateFieldKind.OptionalDateLine, false, DateLabel: "SUNRISE",
                Semantic: AiTemplateSemantic.BirthDate),
            new("离世日期行", AiTemplateFieldKind.OptionalDateLine, false, DateLabel: "SUNSET",
                Semantic: AiTemplateSemantic.DeathDate)
        }.Concat(specificationFields).Append(new("补充要求", AiTemplateFieldKind.Text, false)).ToArray());
}
