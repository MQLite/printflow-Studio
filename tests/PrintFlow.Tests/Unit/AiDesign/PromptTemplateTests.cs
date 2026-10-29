using PrintFlow.Domain.AiDesign;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Tests.Unit.AiDesign;

public sealed class PromptTemplateTests
{
    private readonly PromptTemplateService _templates = new();
    [Theory]
    [InlineData("subject", "subject")]
    [InlineData("badge", "subject")]
    [InlineData("text-print", "text")]
    public void Three_templates_require_subject_or_text(string template, string required)
    {
        _templates.Compose(template, new Dictionary<string,string>(), [], "en").IsFailure.ShouldBeTrue();
        var p = _templates.Compose(template, new Dictionary<string,string> { [required] = "literal art" }, [], "en");
        p.IsSuccess.ShouldBeTrue(); p.Value.Text.ShouldContain("literal art"); p.Value.Text.ShouldContain("watermark");
        p.Value.HandoffInstructions.ShouldContain("one"); p.Value.HandoffInstructions.ShouldContain("download");
        _templates.Validate(p.Value, []).IsSuccess.ShouldBeTrue();
    }
    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public void Preserves_literal_operator_text(string language)
    {
        const string text = "  中文\n\"Keep $ literal\"  ";
        var fields = new Dictionary<string,string> { ["text"] = text, ["fontStyle"] = "手写\n粗体" };
        var p = _templates.Compose("text-print", fields, [], language).Value;
        p.Fields["text"].ShouldBe(text); p.Text.ShouldContain(text); p.Text.ShouldContain("手写\n粗体");
        p.Text.ShouldNotContain(p.HandoffInstructions);
        fields["text"] = "changed after composing"; p.Fields["text"].ShouldBe(text);
    }
    [Fact]
    public void Deleted_reference_blocks_submit_and_same_names_have_distinct_handoff()
    {
        var id = Guid.NewGuid(); var a = AiDesignTransitionsTests.Attachment(id, 1) with { Purpose = "subject reference" };
        var b = AiDesignTransitionsTests.Attachment(id, 2) with { Purpose = "color reference" };
        var p = _templates.Compose("subject", new Dictionary<string,string> { ["subject"] = "cat" }, [a,b], "en").Value;
        p.AttachmentReferences.ShouldBe(new[] { a.Id, b.Id });
        _templates.Validate(p, [a]).IsFailure.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => _templates.RenderHandoff(p, [a]));
        var copy = _templates.RenderHandoff(p with { Text = "operator edited prompt" }, [a,b]);
        copy.ShouldContain("operator edited prompt"); copy.ShouldContain("attachment-01.png"); copy.ShouldContain("attachment-02.png");
        copy.ShouldContain("subject reference"); copy.ShouldContain("color reference"); copy.ShouldNotContain("uploaded", Case.Insensitive);
        copy.ShouldNotContain("C:\\"); copy.ShouldNotContain(id.ToString());
    }
    [Fact]
    public async Task Edited_prompt_is_frozen_and_running_edit_is_rejected()
    {
        using TempWorkspace files = new(); var store = new FileAiDesignRequestStore(files.Root);
        var p = _templates.Compose("subject", new Dictionary<string,string> { ["subject"] = "cat" }, [], "en").Value with { Text = "final\n\"exact text\"" };
        var r = (await store.CreateAsync(Guid.NewGuid(), new(p, []), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.Queued(Guid.NewGuid(), "token", 1, 1, Guid.NewGuid()), default)).Value;
        (await store.ChangeAsync(r.Fence, new AiDesignEvent.DraftSaved(p with { Text = "later edit" }, [], 1), default)).IsFailure.ShouldBeTrue();
        (await store.ReadAsync(r.RequestId, default)).Value.Prompt.Text.ShouldBe("final\n\"exact text\"");
    }
}
