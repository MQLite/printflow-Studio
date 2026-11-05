using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Memorial;

public sealed class MemorialVersionModule(IAiAuthoringDraftStore drafts, IAiAuthoringAssetFork assets, string language = "zh-CN")
{
    public async Task<OperationResult<AiAuthoringDraft>> ForkAsync(Guid sourceId, long sourceRevision, AiProductKind target, CancellationToken ct)
    {
        if (sourceId == Guid.Empty || sourceRevision < 0 || !IsMemorialProduct(target))
            return Fail("Select a source revision and another memorial product.");
        var read = await drafts.ReadAsync(sourceId, ct);
        if (read.IsFailure) return read;
        var source = read.Value;
        if (source.Revision != sourceRevision || !IsMemorialProduct(source.Product) || source.Product == target)
            return Fail("Select a current memorial draft and a different product.");

        Guid childId = Guid.NewGuid();
        var selected = AiDetailedReview.ActiveAssets(source);
        var template = BuiltInAiTemplates.ForLanguage(language).Single(item => item.Content.Product == target);
        return await assets.CopyAndPublishAsync(sourceId, childId, selected, copied =>
            drafts.WithCurrentRevisionAsync(sourceId, sourceRevision, current =>
            {
                if (!selected.SequenceEqual(AiDetailedReview.ActiveAssets(current)) || selected.Count != copied.Count)
                    return Task.FromResult(Fail("The source attachment manifest changed during conversion."));
                var idMap = selected.Zip(copied).ToDictionary(pair => pair.First.Id, pair => pair.Second.Id);
                var child = CreateChild(current, childId, template, copied, idMap);
                return drafts.SaveAsync(child, null, ct);
            }, ct), ct);
    }

    private static AiAuthoringDraft CreateChild(AiAuthoringDraft source, Guid childId, AiTemplateDefinition template,
        IReadOnlyList<AiDesignAttachment> copied, IReadOnlyDictionary<Guid, Guid> assetIds)
    {
        var slots = new List<AiReferenceSlot>();
        var slotIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var textOrigins = new Dictionary<string, AiTemplateField>(StringComparer.Ordinal);
        var activeSlots = source.FinalDocument.Parts.OfType<AiImagePart>().Select(image => image.SlotId).ToHashSet(StringComparer.Ordinal);
        var sourcePreset = BuiltInAiTemplates.All.Single(item => item.Content.Product == source.Product);
        var sourcePresetKeys = sourcePreset.Content.Fields.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var field in template.Content.Fields)
        {
            var original = source.TemplateSnapshot.Fields.FirstOrDefault(candidate => field.Semantic == AiTemplateSemantic.FreeText
                ? sourcePresetKeys.Contains(field.Key) && candidate.Semantic == field.Semantic && candidate.Kind == field.Kind && candidate.Key == field.Key
                : candidate.Semantic == field.Semantic &&
                    (candidate.Kind == AiTemplateFieldKind.ImageReference) == (field.Kind == AiTemplateFieldKind.ImageReference));
            if (field.Kind == AiTemplateFieldKind.ImageReference)
            {
                var sourceSlot = source.Slots.SingleOrDefault(slot => slot.SlotId == original?.SlotId);
                slots.Add(new(field.SlotId!, sourceSlot?.Role ?? field.Key, field.Required || sourceSlot?.Required == true));
                if (sourceSlot is not null) slotIds.Add(sourceSlot.SlotId, field.SlotId!);
            }
            else if (original is not null) textOrigins.Add(field.Key, original);
        }

        template = IncludeCustomText(source, sourcePreset, childId, template, textOrigins);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var confirmedText = new List<string>();
        foreach (var field in template.Content.Fields.Where(field => field.Kind != AiTemplateFieldKind.ImageReference))
        {
            if (textOrigins.TryGetValue(field.Key, out var original))
            {
                fields.Add(field.Key, AiAuthoringConfirmationRules.Value(source, original));
                if (AiAuthoringConfirmationRules.IsDate(field))
                {
                    fields.Add(AiTemplateMetadata.LabelKey(template.Content, field), AiAuthoringConfirmationRules.DateLabel(source, original));
                    fields.Add(AiTemplateMetadata.FormatKey(template.Content, field), AiAuthoringConfirmationRules.DateFormat(source, original));
                }
                if (AiAuthoringConfirmationRules.IsConfirmed(source, AiAuthoringConfirmationRules.TextKey(original)))
                    confirmedText.Add(AiAuthoringConfirmationRules.TextKey(field));
            }
            else if (field.DefaultValue is { } value) fields.Add(field.Key, value);
        }

        var composed = PromptDocumentComposer.Compose(template.Content, fields).Document;
        var parts = composed.Parts.ToList();
        foreach (var image in source.FinalDocument.Parts.OfType<AiImagePart>().DistinctBy(image => image.SlotId))
        {
            if (slotIds.ContainsKey(image.SlotId)) continue;
            var slot = source.Slots.Single(value => value.SlotId == image.SlotId);
            string slotId = slots.Any(value => value.SlotId == slot.SlotId) ? $"fork-{Guid.NewGuid():N}" : slot.SlotId;
            slotIds.Add(slot.SlotId, slotId);
            slots.Add(slot with { SlotId = slotId });
            parts.Add(new AiTextPart(Guid.NewGuid(), "\n"));
            parts.Add(new AiImagePart(Guid.NewGuid(), slotId));
        }

        var child = new AiAuthoringDraft
        {
            Id = childId, ParentDraftId = source.Id, ParentDraftRevision = source.Revision,
            Template = template.Reference, TemplateSnapshot = template.Content, Product = template.Content.Product,
            Mode = AiAuthoringMode.Detailed, Fields = fields, InitialDocument = composed, FinalDocument = new(parts),
            Assets = copied, Slots = slots,
            Bindings = source.Bindings.Where(binding => activeSlots.Contains(binding.SlotId) && slotIds.ContainsKey(binding.SlotId) && assetIds.ContainsKey(binding.AssetId))
                .Select(binding => new AiSlotBinding(slotIds[binding.SlotId], assetIds[binding.AssetId])).ToArray(),
            Preservation = source.Preservation.Where(rule => assetIds.ContainsKey(rule.AssetId))
                .Select(rule => rule with { AssetId = assetIds[rule.AssetId] }).ToArray()
        };

        var confirmedAssets = assetIds.Where(pair => AiAuthoringConfirmationRules.IsConfirmed(source, AiAuthoringConfirmationRules.PreservationKey(pair.Key)))
            .Select(pair => AiAuthoringConfirmationRules.PreservationKey(pair.Value));
        var confirmations = confirmedText.Concat(confirmedAssets).Where(key => AiAuthoringConfirmationRules.CanConfirm(child, key))
            .ToDictionary(key => key, key => AiAuthoringConfirmationRules.ContentHash(child, key)!, StringComparer.Ordinal);
        return AiAuthoringSnapshots.Draft(AiAuthoringConfirmationRules.Refresh(child with { Confirmations = confirmations }));
    }

    private static AiTemplateDefinition IncludeCustomText(AiAuthoringDraft source, AiTemplateDefinition sourcePreset, Guid childId,
        AiTemplateDefinition target, Dictionary<string, AiTemplateField> textOrigins)
    {
        var targetKeys = target.Content.Fields.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        var sourceSpecifications = sourcePreset.Content.Fields.Where(field => !targetKeys.Contains(field.Key))
            .ToDictionary(field => field.Key, StringComparer.Ordinal);
        bool fromPreset = source.Template == sourcePreset.Reference || source.Template == ConversionTemplate(source.Id);
        var usedOrigins = textOrigins.Values.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        var reservedKeys = targetKeys.Concat(source.TemplateSnapshot.Fields.Select(field => field.Key)).ToHashSet(StringComparer.Ordinal);
        var extra = new List<AiTemplateField>();
        foreach (var original in source.TemplateSnapshot.Fields)
        {
            if (original.Kind == AiTemplateFieldKind.ImageReference || usedOrigins.Contains(original.Key)) continue;
            if (sourceSpecifications.TryGetValue(original.Key, out var specification) &&
                (fromPreset || original == specification && AiAuthoringConfirmationRules.Value(source, original) == specification.DefaultValue &&
                    !AiAuthoringConfirmationRules.IsConfirmed(source, AiAuthoringConfirmationRules.TextKey(original)))) continue;

            string key = original.Key;
            if (targetKeys.Contains(key))
            {
                // Keep target preset keys and all existing source field keys reserved.
                int suffix = 2;
                do { key = $"{original.Key}~{suffix++}"; } while (!reservedKeys.Add(key));
            }
            extra.Add(original with { Key = key });
            textOrigins.Add(key, original);
        }

        if (extra.Count == 0) return target;
        var content = target.Content with
        {
            Fields = target.Content.Fields.Concat(extra).ToArray(),
            Body = target.Content.Body + "\n" + string.Join('\n', extra.Select(field => "{{" + field.Key + "}}"))
        };
        // The added field schema is local to this draft. The library preset remains independent.
        return target with { Reference = ConversionTemplate(childId), Content = content };
    }

    private static AiTemplateRef ConversionTemplate(Guid draftId) => new($"memorial-conversion-{draftId:N}", 1);

    private static bool IsMemorialProduct(AiProductKind value) => value is AiProductKind.MemorialTshirt or AiProductKind.MemorialBadge;
    private static OperationResult<AiAuthoringDraft> Fail(string detail) => OperationResult.Fail<AiAuthoringDraft>(FailureCode.PreconditionNotMet, detail);
}
