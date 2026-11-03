using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Authoring;
using static PrintFlow.Tests.Integration.AiDesign.Authoring.AuthoringPersistenceTests;

namespace PrintFlow.Tests.Unit.AiDesign.Authoring;

public sealed class DetailedConfirmationTests
{
    [Fact]
    public async Task Changing_color_does_not_revoke_name_confirmation()
    {
        using TempWorkspace root = new(); var module = Module(root.Root); var draft = await Create(module);
        draft = await Edit(module, draft, new AiAuthoringEdit.SetField("姓名", "  ʻĀna e\u0301 陈\r\nJR  "));
        draft = await Confirm(module, draft, "name");
        draft = await Edit(module, draft, new AiAuthoringEdit.SetField("补充要求", "蓝色背景"));
        AiAuthoringConfirmationRules.IsConfirmed(draft, "name").ShouldBeTrue();
        draft.Fields["姓名"].ShouldBe("  ʻĀna e\u0301 陈\r\nJR  ");
        draft = await Edit(module, draft, new AiAuthoringEdit.SetField("姓名", "changed"));
        draft.Confirmations.ShouldNotContainKey("name");
    }

    [Fact]
    public async Task Year_1039_is_not_silently_changed()
    {
        using TempWorkspace root = new(); var module = Module(root.Root); var draft = await Create(module);
        draft = await Edit(module, draft, new AiAuthoringEdit.SetField("出生日期行", "12/06/1039"));
        draft = await Edit(module, draft, new AiAuthoringEdit.SetDateInterpretation("出生日期行", "dd/MM/yyyy"));
        AiDetailedReview.GetIssues(draft).ShouldContain(x => x.Key == "unusual-year" && x.FieldKey == "birth-date");
        draft = await Confirm(module, draft, "birth-date");
        AiAuthoringConfirmationRules.IsConfirmed(draft, "birth-date").ShouldBeTrue();
        draft.Fields["出生日期行"].ShouldBe("12/06/1039");
        draft = await Edit(module, draft, new AiAuthoringEdit.SetField("出生日期行", "12/06/1939"));
        draft.Confirmations.ShouldNotContainKey("birth-date");
    }

    [Fact]
    public async Task Ambiguous_day_month_needs_explicit_format()
    {
        using TempWorkspace root = new(); var module = Module(root.Root); var draft = await Create(module);
        draft = await Edit(module, draft, new AiAuthoringEdit.SetField("出生日期行", "12/06/1939"));
        AiDetailedReview.GetIssues(draft).ShouldContain(x => x.Key == "date-format-required");
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.ConfirmField("birth-date", Hash(draft, "birth-date")), default)).IsFailure.ShouldBeTrue();
        draft = await Edit(module, draft, new AiAuthoringEdit.SetDateInterpretation("出生日期行", "MM/dd/yyyy"));
        AiDetailedReview.InterpretDate("12/06/1939", "MM/dd/yyyy").ShouldBe("1939-12-06");
        draft = await Confirm(module, draft, "birth-date");
        draft = await Edit(module, draft, new AiAuthoringEdit.SetField("出生日期行.label", "NÉE"));
        draft.Confirmations.ShouldNotContainKey("birth-date");
        draft = await Confirm(module, draft, "birth-date");
        draft = await Edit(module, draft, new AiAuthoringEdit.SetDateInterpretation("出生日期行", "dd/MM/yyyy"));
        draft.Confirmations.ShouldNotContainKey("birth-date");
    }

    [Theory]
    [InlineData("29/02/1939", "", "dd/MM/yyyy", "", "invalid-date")]
    [InlineData("2000-02-30", "", "yyyy-MM-dd", "", "invalid-date")]
    [InlineData("2026-11-03", "2026-11-02", "yyyy-MM-dd", "yyyy-MM-dd", "date-order")]
    [InlineData("2027", "2026", "yyyy", "yyyy", "date-order")]
    public void Invalid_dates_and_definite_reversed_ranges_block_review(string birth, string death, string bf, string df, string issue) =>
        AiDetailedReview.ValidateDates(birth, death, bf, df).ShouldContain(x => x.Key == issue);

    [Fact]
    public void Omitted_dates_and_overlapping_year_ranges_are_not_invented_or_rejected()
    {
        AiDetailedReview.ValidateDates(null, "", "", "").ShouldBeEmpty();
        AiDetailedReview.ValidateDates("1939", "1939-01-01", "yyyy", "yyyy-MM-dd").ShouldBeEmpty();
        AiDetailedReview.ValidateDates("29/02/2000", "", "dd/MM/yyyy", "").ShouldBeEmpty();
    }

    [Fact]
    public async Task Reconciliation_checks_both_hashes_and_never_creates_field_confirmations()
    {
        using TempWorkspace root = new(); var module = Module(root.Root); var draft = await Create(module);
        draft = await Edit(module, draft, new AiAuthoringEdit.SetField("姓名", "form name"));
        draft = await Edit(module, draft, new AiAuthoringEdit.ReplaceDocument(Document("different name in manual text")));
        string docHash = AiAuthoringConfirmationRules.DocumentHash(draft.FinalDocument), fieldsHash = AiAuthoringConfirmationRules.FieldsHash(draft);
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.AcknowledgeDocumentReconciliation("stale", fieldsHash), default)).IsFailure.ShouldBeTrue();
        draft = await Edit(module, draft, new AiAuthoringEdit.AcknowledgeDocumentReconciliation(docHash, fieldsHash));
        draft.NeedsFieldReconcile.ShouldBeFalse(); draft.Confirmations.ShouldNotContainKey("name");
        Text(draft.FinalDocument).ShouldBe("different name in manual text"); draft.Fields["姓名"].ShouldBe("form name");
        draft = await Confirm(module, draft, "name");
        draft = await Edit(module, draft, new AiAuthoringEdit.SetField("补充要求", "new color"));
        draft.NeedsFieldReconcile.ShouldBeTrue(); AiAuthoringConfirmationRules.IsConfirmed(draft, "name").ShouldBeTrue();
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.AcknowledgeDocumentReconciliation(docHash, fieldsHash), default)).IsFailure.ShouldBeTrue();
        draft = await Edit(module, draft, new AiAuthoringEdit.AcknowledgeDocumentReconciliation(docHash, AiAuthoringConfirmationRules.FieldsHash(draft)));
        draft = await Edit(module, draft, new AiAuthoringEdit.ReplaceDocument(Document("edited again")));
        draft.NeedsFieldReconcile.ShouldBeTrue();
    }

    [Fact]
    public async Task Renamed_template_variables_keep_semantic_confirmation_identity_and_stale_hashes_are_rejected()
    {
        using TempWorkspace root = new(); var catalog = new AiTemplateCatalog(new FileAiTemplateLibraryStore(root.Root));
        var source = BuiltInAiTemplates.All[0].Content;
        var content = source with { Body = source.Body.Replace("{{姓名}}", "{{CustomerName}}", StringComparison.Ordinal), Fields = source.Fields.Select(x => x.Semantic == AiTemplateSemantic.Name ? x with { Key = "CustomerName" } : x).ToArray() };
        var template = (await catalog.SaveAsync(null, content, false, default)).Value;
        var module = Module(root.Root); var draft = (await module.CreateAsync(template.Reference, default)).Value;
        draft = await Edit(module, draft, new AiAuthoringEdit.SetField("CustomerName", "Āna"));
        string previous = Hash(draft, "name");
        draft = await Edit(module, draft, new AiAuthoringEdit.SetField("CustomerName", "Ana"));
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.ConfirmField("name", previous), default)).IsFailure.ShouldBeTrue();
        draft = await Confirm(module, draft, "name"); AiAuthoringConfirmationRules.IsConfirmed(draft, "name").ShouldBeTrue();
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.ConfirmField("anything", previous), default)).IsFailure.ShouldBeTrue();
    }

    private static string Hash(AiAuthoringDraft draft, string key) => AiAuthoringConfirmationRules.ContentHash(draft, key)!;
    private static Task<AiAuthoringDraft> Confirm(AiAuthoringModule module, AiAuthoringDraft draft, string key) => Edit(module, draft, new AiAuthoringEdit.ConfirmField(key, Hash(draft, key)));
    private static async Task<AiAuthoringDraft> Create(AiAuthoringModule module) => (await module.CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
    private static async Task<AiAuthoringDraft> Edit(AiAuthoringModule module, AiAuthoringDraft draft, AiAuthoringEdit edit)
    {
        var result = await module.ApplyAsync(draft.Id, draft.Revision, edit, default);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : ""); return result.Value;
    }
}
