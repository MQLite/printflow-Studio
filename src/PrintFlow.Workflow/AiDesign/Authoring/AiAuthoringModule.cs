using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public sealed partial class AiAuthoringModule(IAiAuthoringDraftStore store, AiTemplateCatalog catalog, IAiDesignAttachments? attachments = null)
{
    public Task<OperationResult<AiAuthoringDraftListing>> ListAsync(CancellationToken ct) => store.ListAsync(ct);
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
    public Task<OperationResult<IReadOnlyList<AiAuthoringDraft>>> ReadHistoryAsync(Guid id, CancellationToken ct) => store.ReadHistoryAsync(id, ct);
    public async Task<OperationResult<AiAuthoringDraft>> ApplyAsync(Guid id, long expectedRevision, AiAuthoringEdit edit, CancellationToken ct)
    {
        try
        {
            if (edit is AiAuthoringEdit.ReplaceDocument replacement)
                edit = replacement with { Document = AiAuthoringSnapshots.Document(replacement.Document) };
            if (edit is AiAuthoringEdit.SetLayout pendingLayout)
                edit = pendingLayout with { Layout = MemorialLayoutModule.Freeze(pendingLayout.Layout) };
            var read = await store.ReadAsync(id, ct); if (read.IsFailure) return read;
            var draft = read.Value;
            if (draft.Revision != expectedRevision) return Fail("Draft is stale.");
            AiAuthoringDraft next;
            switch (edit)
            {
                case AiAuthoringEdit.SetLayout placement:
                    var layout = placement.Layout;
                    bool changed = !MemorialLayoutModule.SameContent(draft.Layout, layout);
                    if (changed) layout = layout with { ProductTemplateConfirmed = false };
                    var layoutIssues = MemorialLayoutModule.Validate(draft, layout);
                    if (layoutIssues.Any(x => x.Key is "TextMismatch" or "DuplicateText" or "PreservationConflict") ||
                        layout.ProductTemplateConfirmed && layoutIssues.Count > 0) return Fail("The layout has unresolved text, geometry or preservation issues.");
                    next = draft with { Layout = layout };
                    break;
                case AiAuthoringEdit.SetDateInterpretation interpretation:
                    if (interpretation.Format is null || interpretation.Format.Length > 0 && !AiDetailedReview.DateFormats.Contains(interpretation.Format, StringComparer.Ordinal) ||
                        !draft.TemplateSnapshot.Fields.Any(x => x.Key == interpretation.FieldKey && AiAuthoringConfirmationRules.IsDate(x))) return Fail("Choose an explicit supported date format.");
                    var dateField = draft.TemplateSnapshot.Fields.Single(x => x.Key == interpretation.FieldKey);
                    next = draft with { Fields = new Dictionary<string, string>(draft.Fields, StringComparer.Ordinal) { [AiTemplateMetadata.FormatKey(draft.TemplateSnapshot, dateField)] = interpretation.Format } };
                    break;
                case AiAuthoringEdit.SetPreservation preservation:
                    if (preservation.Rule is not { } rule || !AiDetailedReview.ActiveAssets(draft).Any(x => x.Id == rule.AssetId))
                        return Fail("An active image is required for preservation permissions.");
                    next = draft with { Preservation = draft.Preservation.Where(x => x.AssetId != rule.AssetId).Append(rule).ToArray() };
                    break;
                case AiAuthoringEdit.ConfirmField confirmation:
                    if (!AiAuthoringConfirmationRules.CanConfirm(draft, confirmation.Key) || AiAuthoringConfirmationRules.ContentHash(draft, confirmation.Key) != confirmation.ExpectedContentHash)
                        return Fail("The displayed content is missing, invalid or changed; review it again.");
                    var preservedAsset = AiDetailedReview.ActiveAssets(draft).SingleOrDefault(x => AiAuthoringConfirmationRules.PreservationKey(x.Id) == confirmation.Key);
                    if (preservedAsset is not null)
                    {
                        if (attachments is null) return Fail("Verified image reads are unavailable.");
                        var verifiedPreservation = await attachments.VerifyImagesAsync(id, [preservedAsset], ct);
                        if (verifiedPreservation.IsFailure) return OperationResult.Fail<AiAuthoringDraft>(verifiedPreservation.Failure);
                    }
                    next = draft with { Confirmations = new Dictionary<string, string>(draft.Confirmations, StringComparer.Ordinal) { [confirmation.Key] = confirmation.ExpectedContentHash } };
                    break;
                case AiAuthoringEdit.AcknowledgeDocumentReconciliation reconciliation:
                    if (AiAuthoringConfirmationRules.DocumentHash(draft.FinalDocument) != reconciliation.ExpectedDocumentHash || AiAuthoringConfirmationRules.FieldsHash(draft) != reconciliation.ExpectedFieldsHash)
                        return Fail("The document or fields changed; compare both sides again.");
                    next = draft with { Confirmations = new Dictionary<string, string>(draft.Confirmations, StringComparer.Ordinal)
                    {
                        [AiAuthoringConfirmationRules.ReconciledDocument] = reconciliation.ExpectedDocumentHash,
                        [AiAuthoringConfirmationRules.ReconciledFields] = reconciliation.ExpectedFieldsHash
                    } };
                    break;
                case AiAuthoringEdit.RestoreRevision restore:
                    var history = await store.ReadHistoryAsync(id, ct);
                    if (history.IsFailure) return OperationResult.Fail<AiAuthoringDraft>(history.Failure);
                    var historical = history.Value.SingleOrDefault(x => x.Revision == restore.Revision);
                    if (historical is null) return Fail("Historical revision is unavailable.");
                    next = historical with { Revision = expectedRevision };
                    var historicalAssets = ActiveAssets(next);
                    if (historicalAssets.Count > 0)
                    {
                        if (attachments is null) return Fail("Verified image reads are unavailable.");
                        var verifiedHistory = await attachments.VerifyImagesAsync(id, historicalAssets, ct);
                        if (verifiedHistory.IsFailure) return OperationResult.Fail<AiAuthoringDraft>(verifiedHistory.Failure);
                    }
                    break;
                case AiAuthoringEdit.SetField field:
                    if (field.Value is null || !draft.TemplateSnapshot.Fields.Any(x => x.Kind != AiTemplateFieldKind.ImageReference && (x.Key == field.Key ||
                        AiAuthoringConfirmationRules.IsDate(x) && (AiTemplateMetadata.LabelKey(draft.TemplateSnapshot, x) == field.Key || AiTemplateMetadata.FormatKey(draft.TemplateSnapshot, x) == field.Key))))
                        return Fail("Unknown authoring field.");
                    if (draft.TemplateSnapshot.Fields.Any(x => x.Key == field.Key &&
                        MemorialLayoutModule.FixedPromptSpecification(draft.Product, x) is { } fixedValue && field.Value != fixedValue))
                        return Fail("The product layout defines this specification.");
                    var fields = new Dictionary<string, string>(draft.Fields, StringComparer.Ordinal) { [field.Key] = field.Value };
                    next = draft with { Fields = fields, NeedsFieldReconcile = draft.NeedsFieldReconcile || draft.IsManuallyEdited };
                    break;
                case AiAuthoringEdit.ReplaceDocument document:
                    next = draft with { FinalDocument = document.Document, IsManuallyEdited = true, NeedsFieldReconcile = true };
                    break;
                case AiAuthoringEdit.Recompose recompose:
                    if (draft.IsManuallyEdited && !recompose.PreservePrevious) return Fail("Preserve the manual draft before recomposing.");
                    var composedFields = new Dictionary<string, string>(draft.Fields, StringComparer.Ordinal);
                    foreach (var definition in draft.TemplateSnapshot.Fields)
                        if (MemorialLayoutModule.FixedPromptSpecification(draft.Product, definition) is { } specification)
                            composedFields[definition.Key] = specification;
                    var composition = PromptDocumentComposer.Compose(draft.TemplateSnapshot, composedFields);
                    next = draft with { Fields = composedFields, FinalDocument = composition.Document, IsManuallyEdited = false, NeedsFieldReconcile = false };
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
                case AiAuthoringEdit.BindImage image:
                    var bound = await BindNewImageAsync(draft, image, ct); if (bound.IsFailure) return bound;
                    next = bound.Value;
                    break;
                case AiAuthoringEdit.BindExistingImage existing:
                    var reused = await BindExistingImageAsync(draft, existing, ct); if (reused.IsFailure) return reused;
                    next = reused.Value;
                    break;
                case AiAuthoringEdit.InsertImageReference insertion:
                    next = InsertReference(draft, insertion);
                    break;
                case AiAuthoringEdit.RemoveReference removal:
                    if (!draft.FinalDocument.Parts.OfType<AiImagePart>().Any(x => x.PartId == removal.PartId)) return Fail("Image reference is missing.");
                    next = draft with { FinalDocument = new(draft.FinalDocument.Parts.Where(x => x.PartId != removal.PartId).ToArray()), IsManuallyEdited = true, NeedsFieldReconcile = true };
                    break;
                case AiAuthoringEdit.RemoveImage removal:
                    if (!draft.Assets.Any(x => x.Id == removal.AssetId)) return Fail("Image is missing.");
                    next = draft with { Bindings = draft.Bindings.Where(x => x.AssetId != removal.AssetId).ToArray() };
                    break;
                default:
                    return Fail("This authoring edit is not available yet.");
            }
            // Every prior successful state already has an immutable snapshot. Publish the new
            // pointer only after the next snapshot exists; a failed save cannot erase the old one.
            if (next.Layout is not null && (!MemorialLayoutModule.SameInputs(draft, next) ||
                edit is not AiAuthoringEdit.SetLayout && !MemorialLayoutModule.SameContent(draft.Layout, next.Layout)))
                next = next with { Layout = next.Layout with { ProductTemplateConfirmed = false } };
            next = AiAuthoringSnapshots.Draft(AiAuthoringConfirmationRules.Refresh(next));
            var active = ActiveAssets(next);
            if (active.Count > AiDesignAttachmentLimits.MaximumFiles || active.Any(x => x.ByteLength > AiDesignAttachmentLimits.ImageBytes || !IsImageMetadata(x)) ||
                active.Sum(x => x.ByteLength) > AiDesignAttachmentLimits.TotalBytes) return Fail("Active image attachment limit exceeded.");
            if (edit is not (AiAuthoringEdit.BindImage or AiAuthoringEdit.BindExistingImage))
            {
                var previous = ActiveAssets(draft).Select(x => x.Id).ToHashSet();
                var activated = active.Where(x => !previous.Contains(x.Id)).ToArray();
                if (activated.Length > 0)
                {
                    if (attachments is null) return Fail("Verified image reads are unavailable.");
                    var verified = await attachments.VerifyImagesAsync(draft.Id, activated, ct);
                    if (verified.IsFailure) return OperationResult.Fail<AiAuthoringDraft>(verified.Failure);
                }
            }
            return await store.SaveAsync(next, expectedRevision, ct);
        }
        catch (ArgumentException) { return Fail("Invalid authoring edit."); }
    }

    public AiPromptExport Export(AiAuthoringDraft draft)
    {
        draft = AiAuthoringSnapshots.Draft(draft);
        var text = new System.Text.StringBuilder(); var issues = new List<AiAuthoringIssue>();
        var required = new Dictionary<Guid, AiDesignAttachment>();
        foreach (var part in draft.FinalDocument.Parts)
        {
            if (part is AiTextPart literal) text.Append(literal.Text);
            else if (part is AiImagePart image)
            {
                string role = draft.Slots.Single(x => x.SlotId == image.SlotId).Role;
                var binding = draft.Bindings.SingleOrDefault(x => x.SlotId == image.SlotId);
                var asset = binding is null ? null : draft.Assets.SingleOrDefault(x => x.Id == binding.AssetId);
                if (asset is not null && IsImageMetadata(asset))
                {
                    text.Append(role).Append('：').Append(asset.UploadName);
                    required[asset.Id] = asset;
                }
                else
                {
                    text.Append('[').Append(role).Append("：待选择图片]");
                    issues.Add(new("missing-image", null, image.SlotId));
                }
            }
        }
        var activeSlots = draft.FinalDocument.Parts.OfType<AiImagePart>().Select(x => x.SlotId).ToHashSet(StringComparer.Ordinal);
        var manifest = required.Values.OrderBy(x => x.Order).Select(asset => asset with
        {
            Purpose = string.Join(" / ", draft.Slots.Where(slot => activeSlots.Contains(slot.SlotId) && draft.Bindings.Any(b => b.SlotId == slot.SlotId && b.AssetId == asset.Id)).Select(slot => slot.Role))
        }).ToArray();
        return new(text.ToString(), Array.AsReadOnly(manifest), issues.AsReadOnly());
    }

    private static AiAuthoringDraft WithTemplate(AiAuthoringDraft draft, AiTemplateDefinition template)
    {
        var fields = template.Content.Fields.Where(x => x.Kind != AiTemplateFieldKind.ImageReference)
            .Select(x => (x.Key, Value: MemorialLayoutModule.FixedPromptSpecification(template.Content.Product, x) ?? x.DefaultValue))
            .Where(x => x.Value is not null)
            .ToDictionary(x => x.Key, x => x.Value!, StringComparer.Ordinal);
        var slots = template.Content.Fields.Where(x => x.Kind == AiTemplateFieldKind.ImageReference)
            .Select(x => new AiReferenceSlot(x.SlotId!, x.Key, x.Required)).ToArray();
        var composition = PromptDocumentComposer.Compose(template.Content, fields);
        return AiAuthoringSnapshots.Draft(draft with
        {
            Template = template.Reference, TemplateSnapshot = template.Content, Product = template.Content.Product,
            Fields = fields, Slots = slots, Bindings = [], InitialDocument = composition.Document, FinalDocument = composition.Document,
            IsManuallyEdited = false, NeedsFieldReconcile = false, Confirmations = new Dictionary<string, string>(), Layout = null
        });
    }

    private static OperationResult<AiAuthoringDraft> Fail(string message) => OperationResult.Fail<AiAuthoringDraft>(FailureCode.PreconditionNotMet, message);
}
