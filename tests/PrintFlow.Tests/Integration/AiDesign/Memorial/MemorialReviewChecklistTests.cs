using System.IO;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Infrastructure.AiDesign.Memorial;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Tests.Integration.AiDesign.Memorial;

public sealed class MemorialReviewChecklistTests
{
    [Fact]
    public async Task Restart_recovers_exact_render_checks_and_all_five_still_require_explicit_acceptance()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();
        var render = await fixture.RenderAsync(draft, MemorialRenderPurpose.DesignMaterial);
        var checklist = Checklist(fixture);
        var initial = (await checklist.ReadAsync(render, default)).Value;
        initial.Revision.ShouldBe(0);
        initial.Checks.Count.ShouldBe(5);
        initial.Checks.Values.ShouldAllBe(value => !value);
        (await checklist.AcceptAsync(render, initial.Revision, default)).Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        var state = (await checklist.SetCheckAsync(render, initial.Revision, MemorialReviewItem.Person, true, default)).Value;
        (await Checklist(fixture).SetCheckAsync(render, initial.Revision, MemorialReviewItem.Decor, true, default))
            .Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);

        checklist = new MemorialReviewChecklist(new FileAiAuthoringDraftStore(fixture.Root), new FileMemorialRenderStore(fixture.Root),
            new FileMemorialReviewStore(fixture.Root));
        var recovered = (await checklist.ReadAsync(render, default)).Value;
        recovered.Render.ShouldBe(render);
        recovered.Revision.ShouldBe(state.Revision);
        recovered.Checks.Where(x => x.Value).Select(x => x.Key).ShouldBe([MemorialReviewItem.Person]);
        foreach (var item in new[] { MemorialReviewItem.Decor, MemorialReviewItem.Text, MemorialReviewItem.Layout, MemorialReviewItem.Transparency })
            state = (await checklist.SetCheckAsync(render, state.Revision, item, true, default)).Value;
        state.Accepted.ShouldBeFalse();
        var accepted = (await checklist.AcceptAsync(render, state.Revision, default)).Value;
        accepted.Accepted.ShouldBeTrue();
        (await Checklist(fixture).ReadAsync(render, default)).Value.Accepted.ShouldBeTrue();
        var other = await fixture.RenderAsync(draft, MemorialRenderPurpose.DesignMaterial);
        (await checklist.ReadAsync(other, default)).Value.Checks.Values.ShouldAllBe(value => !value);
        (await fixture.Drafts.ReadAsync(draft.Id, default)).Value.Revision.ShouldBe(draft.Revision);
        var revoked = (await checklist.SetCheckAsync(render, accepted.Revision, MemorialReviewItem.Text, false, default)).Value;
        revoked.Accepted.ShouldBeFalse();
    }

    [Fact]
    public async Task Changed_name_only_revokes_its_content_confirmation_and_layout_while_stale_render_cannot_be_accepted()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();
        var render = await fixture.RenderAsync(draft, MemorialRenderPurpose.DesignMaterial);
        var checklist = Checklist(fixture);
        var state = await CheckAllAsync(checklist, render);
        var module = new AiAuthoringModule(fixture.Drafts, new AiTemplateCatalog(new FileAiTemplateLibraryStore(fixture.Root)));
        var changed = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetField("literal", "Different"), default)).Value;

        AiAuthoringConfirmationRules.IsConfirmed(changed, "name").ShouldBeFalse();
        changed.Preservation.ShouldAllBe(rule => AiAuthoringConfirmationRules.IsConfirmed(changed, AiAuthoringConfirmationRules.PreservationKey(rule.AssetId)));
        changed.Layout!.ProductTemplateConfirmed.ShouldBeFalse();
        (await checklist.AcceptAsync(render, state.Revision, default)).Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        (await fixture.Renders.ReadAsync(render.File, default)).Value.ShouldBe(render);
    }

    [Fact]
    public async Task Preview_forged_facts_and_replaced_png_cannot_authorize_review()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();
        var checklist = Checklist(fixture);
        var preview = await fixture.RenderAsync(draft);
        (await checklist.SetCheckAsync(preview, 0, MemorialReviewItem.Person, true, default)).IsFailure.ShouldBeTrue();
        var render = await fixture.RenderAsync(draft, MemorialRenderPurpose.DesignMaterial);
        var state = await CheckAllAsync(checklist, render);
        var forged = render with { Facts = render.Facts with { PixelWidth = render.Facts.PixelWidth + 1 } };
        (await checklist.AcceptAsync(forged, state.Revision, default)).IsFailure.ShouldBeTrue();

        string png = Directory.GetFiles(fixture.RenderPath(render), "*.png").Single();
        File.SetAttributes(png, FileAttributes.Normal);
        byte[] bytes = await File.ReadAllBytesAsync(png);
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(png, bytes);
        (await checklist.AcceptAsync(render, state.Revision, default)).IsFailure.ShouldBeTrue();
        (await new FileMemorialReviewStore(fixture.Root).ReadAsync(render.File, default)).Value.Accepted.ShouldBeFalse();
    }

    private static MemorialReviewChecklist Checklist(MemorialRenderFixture fixture) =>
        new(fixture.Drafts, fixture.Renders, new FileMemorialReviewStore(fixture.Root));

    private static async Task<MemorialReviewState> CheckAllAsync(MemorialReviewChecklist checklist, MemorialRenderResult render)
    {
        var state = (await checklist.ReadAsync(render, default)).Value;
        foreach (var item in Enum.GetValues<MemorialReviewItem>())
            state = (await checklist.SetCheckAsync(render, state.Revision, item, true, default)).Value;
        return state;
    }
}
