using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Memorial;

/// <summary>Freezes the confirmed draft revision; callers cannot substitute a render layout or source list.</summary>
public sealed class MemorialCompositionModule(IAiAuthoringDraftStore drafts, IMemorialRenderer renderer)
{
    public async Task<OperationResult<MemorialRenderResult>> RenderAsync(
        Guid draftId, long expectedRevision, MemorialRenderPurpose purpose, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return OperationResult.Fail<MemorialRenderResult>(FailureCode.Cancelled, "Memorial composition cancelled.");
        if (draftId == Guid.Empty || expectedRevision < 0 || !Enum.IsDefined(purpose))
            return OperationResult.Fail<MemorialRenderResult>(FailureCode.PreconditionNotMet, "Invalid memorial render request.");

        var read = await drafts.ReadAsync(draftId, ct);
        if (read.IsFailure) return OperationResult.Fail<MemorialRenderResult>(read.Failure);
        if (read.Value.Id != draftId || read.Value.Revision != expectedRevision)
            return OperationResult.Fail<MemorialRenderResult>(FailureCode.RevisionIntegrityMismatch, "The memorial draft revision changed.");

        var plan = CreatePlan(read.Value, purpose);
        return plan.IsFailure ? OperationResult.Fail<MemorialRenderResult>(plan.Failure) : await renderer.RenderAsync(plan.Value, ct);
    }

    public static OperationResult<MemorialRenderPlan> CreatePlan(AiAuthoringDraft draft, MemorialRenderPurpose purpose)
    {
        try
        {
            draft = AiAuthoringSnapshots.Draft(draft);
            if (!Enum.IsDefined(purpose) || draft.Mode != AiAuthoringMode.Detailed ||
                draft.Product is not (AiProductKind.MemorialBadge or AiProductKind.MemorialTshirt) || draft.Layout is not { } layout)
                return Unconfirmed("MemorialLayoutRequired");
            int step = AiDetailedReview.GetFirstIncompleteStep(draft);
            if (step < 5)
                return Unconfirmed(step switch { 2 => "SourceRequired", 3 => "PreservationUnconfirmed", _ => "TextUnconfirmed" });

            var issue = MemorialLayoutModule.Validate(draft, layout).FirstOrDefault();
            if (issue is not null) return Unconfirmed(issue.Key, issue.FieldKey);
            var fields = draft.TemplateSnapshot.Fields.Where(x => x.Kind != AiTemplateFieldKind.ImageReference).ToArray();
            foreach (var line in layout.Text)
            {
                var field = fields.SingleOrDefault(x => AiAuthoringConfirmationRules.TextKey(x) == line.Key ||
                    AiAuthoringConfirmationRules.IsDate(x) && MemorialLayoutModule.DateLabelKey(x) == line.Key);
                string? key = field is null ? null : AiAuthoringConfirmationRules.TextKey(field);
                if (key is null || !AiAuthoringConfirmationRules.CanConfirm(draft, key) || !AiAuthoringConfirmationRules.IsConfirmed(draft, key))
                    return Unconfirmed("TextUnconfirmed", line.Key);
            }

            var assets = AiDetailedReview.ActiveAssets(draft).OrderBy(x => x.Id).ToArray();
            if (assets.Length is < 1 or > AiDesignAttachmentLimits.MaximumFiles) return Unconfirmed("InvalidActiveAssets");
            foreach (var asset in assets)
            {
                string key = AiAuthoringConfirmationRules.PreservationKey(asset.Id);
                if (!AiAuthoringConfirmationRules.CanConfirm(draft, key) || !AiAuthoringConfirmationRules.IsConfirmed(draft, key))
                    return Unconfirmed("PreservationUnconfirmed", key);
            }
            var plan = new MemorialRenderPlan(draft.Id, draft.Revision, layout, Array.AsReadOnly(assets), purpose, "");
            return OperationResult.Ok(plan with { SourceManifestSha256 = ComputeSourceManifest(plan) });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { return Unconfirmed("InvalidMemorialDraft"); }
    }

    /// <summary>Versioned, UTF-8 manifest; layer and text order are significant, attachment enumeration order is not.</summary>
    public static string ComputeSourceManifest(MemorialRenderPlan plan) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            SchemaVersion = 1,
            plan.DraftId,
            plan.DraftRevision,
            plan.Layout,
            plan.Purpose,
            Assets = plan.Assets.OrderBy(x => x.Id).Select(x => new { x.Id, x.ByteLength, Sha256 = x.Sha256.Value }).ToArray()
        })));

    private static OperationResult<MemorialRenderPlan> Unconfirmed(string issue, string? field = null) =>
        OperationResult.Fail<MemorialRenderPlan>(OperationFailure.Create(FailureCode.PreconditionNotMet, issue,
            context: new Dictionary<string, string>(StringComparer.Ordinal) { ["issue"] = issue, ["field"] = field ?? "" }));
}
