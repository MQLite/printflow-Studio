using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public sealed class AiAuthoringModule(IAiAuthoringDraftStore store, AiTemplateCatalog catalog)
{
    public async Task<OperationResult<AiAuthoringDraft>> CreateAsync(AiTemplateRef template, CancellationToken ct)
    {
        var listed = await catalog.ListAsync(ct); if (listed.IsFailure) return OperationResult.Fail<AiAuthoringDraft>(listed.Failure);
        var selected = listed.Value.SingleOrDefault(x => x.Reference == template);
        if (selected is null) return Fail("Template is missing or stale.");
        var draft = WithTemplate(new AiAuthoringDraft
        {
            Id = Guid.NewGuid(), Template = selected.Reference, TemplateSnapshot = selected.Content,
            Fields = new Dictionary<string, string>(), InitialDocument = new([]), FinalDocument = new([])
        }, selected);
        return await store.SaveAsync(draft, null, ct);
    }
    public Task<OperationResult<AiAuthoringDraft>> ReadAsync(Guid id, CancellationToken ct) => store.ReadAsync(id, ct);
    public async Task<OperationResult<AiAuthoringDraft>> ApplyAsync(Guid id, long expectedRevision, AiAuthoringEdit edit, CancellationToken ct)
    {
        try
        {
            if (edit is AiAuthoringEdit.ReplaceDocument replacement)
                edit = replacement with { Document = AiAuthoringSnapshots.Document(replacement.Document) };
            var read = await store.ReadAsync(id, ct); if (read.IsFailure) return read;
            var draft = read.Value;
            if (draft.Revision != expectedRevision) return Fail("Draft is stale.");
            AiAuthoringDraft next;
            switch (edit)
            {
                case AiAuthoringEdit.SetField field:
                    if (field.Value is null || !draft.TemplateSnapshot.Fields.Any(x => x.Key == field.Key ||
                        x.Kind == AiTemplateFieldKind.OptionalDateLine && (x.Key + ".label" == field.Key || x.Key + ".format" == field.Key)))
                        return Fail("Unknown authoring field.");
                    var fields = new Dictionary<string, string>(draft.Fields, StringComparer.Ordinal) { [field.Key] = field.Value };
                    next = draft with { Fields = fields, NeedsFieldReconcile = draft.NeedsFieldReconcile || draft.IsManuallyEdited };
                    break;
                case AiAuthoringEdit.ReplaceDocument document:
                    next = draft with { FinalDocument = document.Document, IsManuallyEdited = true, NeedsFieldReconcile = true };
                    break;
                case AiAuthoringEdit.Recompose recompose:
                    if (draft.IsManuallyEdited && !recompose.PreservePrevious) return Fail("Preserve the manual draft before recomposing.");
                    var composition = PromptDocumentComposer.Compose(draft.TemplateSnapshot, draft.Fields);
                    next = draft with { FinalDocument = composition.Document, IsManuallyEdited = false, NeedsFieldReconcile = false };
                    break;
                case AiAuthoringEdit.SwitchMode mode:
                    next = draft with { Mode = mode.Mode };
                    break;
                case AiAuthoringEdit.SelectTemplate selection:
                    var listed = await catalog.ListAsync(ct); if (listed.IsFailure) return OperationResult.Fail<AiAuthoringDraft>(listed.Failure);
                    var template = listed.Value.SingleOrDefault(x => x.Reference == selection.Template);
                    if (template is null) return Fail("Template is missing or stale.");
                    next = WithTemplate(draft, template);
                    break;
                default:
                    return Fail("This authoring edit is not available yet.");
            }
            // Every prior successful state already has an immutable snapshot. Publish the new
            // pointer only after the next snapshot exists; a failed save cannot erase the old one.
            return await store.SaveAsync(AiAuthoringSnapshots.Draft(next), expectedRevision, ct);
        }
        catch (ArgumentException) { return Fail("Invalid authoring edit."); }
    }

    public AiPromptExport Export(AiAuthoringDraft draft)
    {
        draft = AiAuthoringSnapshots.Draft(draft);
        var text = new System.Text.StringBuilder(); var issues = new List<AiAuthoringIssue>();
        foreach (var part in draft.FinalDocument.Parts)
        {
            if (part is AiTextPart literal) text.Append(literal.Text);
            else if (part is AiImagePart image)
            {
                string role = draft.Slots.Single(x => x.SlotId == image.SlotId).Role;
                text.Append('[').Append(role).Append("：待选择图片]");
                issues.Add(new("missing-image", null, image.SlotId));
            }
        }
        return new(text.ToString(), [], issues.AsReadOnly());
    }

    private static AiAuthoringDraft WithTemplate(AiAuthoringDraft draft, AiTemplateDefinition template)
    {
        var fields = template.Content.Fields.Where(x => x.Kind != AiTemplateFieldKind.ImageReference && x.DefaultValue is not null)
            .ToDictionary(x => x.Key, x => x.DefaultValue!, StringComparer.Ordinal);
        var slots = template.Content.Fields.Where(x => x.Kind == AiTemplateFieldKind.ImageReference)
            .Select(x => new AiReferenceSlot(x.SlotId!, x.Key, x.Required)).ToArray();
        var composition = PromptDocumentComposer.Compose(template.Content, fields);
        return AiAuthoringSnapshots.Draft(draft with
        {
            Template = template.Reference, TemplateSnapshot = template.Content, Product = template.Content.Product,
            Fields = fields, Slots = slots, Bindings = [], InitialDocument = composition.Document, FinalDocument = composition.Document,
            IsManuallyEdited = false, NeedsFieldReconcile = false, Confirmations = new Dictionary<string, string>()
        });
    }

    private static OperationResult<AiAuthoringDraft> Fail(string message) => OperationResult.Fail<AiAuthoringDraft>(FailureCode.PreconditionNotMet, message);
}
