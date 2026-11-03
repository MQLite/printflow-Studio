using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public sealed partial class AiAuthoringModule
{
    private async Task<OperationResult<AiAuthoringDraft>> BindNewImageAsync(AiAuthoringDraft draft, AiAuthoringEdit.BindImage edit, CancellationToken ct)
    {
        var slot = draft.Slots.SingleOrDefault(x => x.SlotId == edit.SlotId);
        if (attachments is null || slot is null || edit.Selection is null ||
            !new[] { ".png", ".jpg", ".jpeg", ".webp" }.Any(ext => edit.Selection.AbsolutePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return Fail("An explicit supported image selection and slot are required.");
        bool active = draft.FinalDocument.Parts.OfType<AiImagePart>().Any(x => x.SlotId == edit.SlotId);
        var snapshots = await attachments.SnapshotAsync(draft.Id, [edit.Selection with { Purpose = slot.Role }], active ? ActiveAssets(draft, edit.SlotId) : [], ct);
        if (snapshots.IsFailure) return OperationResult.Fail<AiAuthoringDraft>(snapshots.Failure);
        var added = snapshots.Value.Where(x => !draft.Assets.Any(old => old.Id == x.Id)).ToArray();
        if (added.Length != 1 || !IsImageMetadata(added[0])) return Fail("Image snapshot was not published.");
        var verified = await attachments.VerifyImagesAsync(draft.Id, added, ct);
        if (verified.IsFailure) return OperationResult.Fail<AiAuthoringDraft>(verified.Failure);
        return OperationResult.Ok(draft with
        {
            Assets = draft.Assets.Append(added[0]).ToArray(),
            Bindings = draft.Bindings.Where(x => x.SlotId != edit.SlotId).Append(new(edit.SlotId, added[0].Id)).ToArray()
        });
    }

    private async Task<OperationResult<AiAuthoringDraft>> BindExistingImageAsync(AiAuthoringDraft draft, AiAuthoringEdit.BindExistingImage edit, CancellationToken ct)
    {
        var asset = draft.Assets.SingleOrDefault(x => x.Id == edit.AssetId);
        if (attachments is null || !draft.Slots.Any(x => x.SlotId == edit.SlotId) || asset is null || !IsImageMetadata(asset)) return Fail("Selected draft image or slot is missing.");
        var next = draft with { Bindings = draft.Bindings.Where(x => x.SlotId != edit.SlotId).Append(new(edit.SlotId, asset.Id)).ToArray() };
        // An inactive choice still needs verified image bytes, but is not a ninth active file.
        var verify = await attachments.VerifyImagesAsync(draft.Id, [asset], ct);
        return verify.IsFailure ? OperationResult.Fail<AiAuthoringDraft>(verify.Failure) : OperationResult.Ok(next);
    }

    private static IReadOnlyList<AiDesignAttachment> ActiveAssets(AiAuthoringDraft draft, string? excludingSlot = null)
    {
        var slots = draft.FinalDocument.Parts.OfType<AiImagePart>().Select(x => x.SlotId).ToHashSet(StringComparer.Ordinal);
        var ids = draft.Bindings.Where(x => x.SlotId != excludingSlot && slots.Contains(x.SlotId)).Select(x => x.AssetId).ToHashSet();
        return draft.Assets.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Order).ToArray();
    }

    private static AiAuthoringDraft InsertReference(AiAuthoringDraft draft, AiAuthoringEdit.InsertImageReference edit)
    {
        if (edit.Slot is null || draft.Slots.Any(x => x.SlotId == edit.Slot.SlotId) ||
            draft.FinalDocument.Parts.Concat(draft.InitialDocument.Parts).Any(x => x.PartId == edit.PartId))
            throw new ArgumentException("Image slot and part identities must be new.");
        var parts = draft.FinalDocument.Parts.ToList();
        int index = edit.AfterPartId is null ? parts.Count : parts.FindIndex(x => x.PartId == edit.AfterPartId) + 1;
        if (edit.AfterPartId is not null && index == 0) throw new ArgumentException("Insertion anchor is missing.");
        parts.Insert(index, new AiImagePart(edit.PartId, edit.Slot.SlotId));
        return draft with { Slots = draft.Slots.Append(edit.Slot).ToArray(), FinalDocument = new(parts), IsManuallyEdited = true, NeedsFieldReconcile = true };
    }

    private static bool IsImageMetadata(AiDesignAttachment asset)
    {
        string extension = asset.MediaType switch
        {
            "image/png" => ".png", "image/webp" => ".webp",
            "image/jpeg" => asset.UploadName.EndsWith(".jpeg", StringComparison.Ordinal) ? ".jpeg" : ".jpg", _ => ""
        };
        return extension.Length > 0 && asset.UploadName == $"attachment-{asset.Order:D2}{extension}" &&
            asset.Snapshot.RelativePath == $"Attachments/{asset.Id:N}/{asset.UploadName}" && asset.OriginalName.IndexOfAny(['/', '\\']) < 0;
    }
}
