using System.Collections.Immutable;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// The signed structural description of an actionable control reached by walking up from the
/// text marker that titles it (Epic 11300 Part B2A §7, §8).
/// </summary>
/// <param name="MarkerControlType">The marker element's control type.</param>
/// <param name="MarkerClassName">The marker element's framework class.</param>
/// <param name="MarkerAutomationIdSuffix">What the marker's own automation id ends with.</param>
/// <param name="OwnerControlType">The actionable owner's control type.</param>
/// <param name="OwnerClassName">The actionable owner's framework class.</param>
/// <param name="OwnerAutomationIdSuffix">What the owner's automation id ends with.</param>
/// <param name="MarkerRelativeAutomationIdSuffix">
/// The exact string the owner's automation id gains to become the marker's. Distinct from
/// <paramref name="MarkerAutomationIdSuffix"/> whenever the owner is not the marker's immediate
/// parent, which is precisely the case this record exists for.
/// </param>
/// <param name="OwnerAncestorDepth">
/// How many control-view levels above the marker the owner sits. Signed, never searched for.
/// </param>
/// <param name="RequiredOwnerPattern">The pattern the owner must expose before it may be invoked.</param>
/// <remarks>
/// <see cref="MeituCardShape"/> already describes a marker and the control that owns it, and
/// this record deliberately does not reuse it. The difference is not cosmetic:
/// <see cref="MeituCardShape"/> encodes a <i>one-level</i> relationship in which the marker's id
/// is the owner's id plus the marker's own suffix, and Meitu's editor tool list does not have
/// that shape. The 高级调整 entries nest a layout <c>QWidget</c> between the <c>QLabel</c> and
/// the <c>ModuleButton</c> that acts:
///
/// <code>
/// Text     …contentsWidget.ModuleButton.buttonWidget.titleLabel  name='AI变清晰'  QLabel
/// Group    …contentsWidget.ModuleButton.buttonWidget                               QWidget
/// CheckBox …contentsWidget.ModuleButton                                            ModuleButton
/// </code>
///
/// Bending the card shape to fit would have meant either walking "up until something matches",
/// which is the unbounded search §8 forbids, or silently treating the intermediate
/// <c>QWidget</c> as the target — and that <c>QWidget</c> advertises <c>InvokePattern</c>, so
/// invoking it would have reported success and done nothing. Exactly the Part A defect, one
/// level higher.
/// </remarks>
public sealed record MeituOwnedControlShape(
    string MarkerControlType,
    string MarkerClassName,
    string MarkerAutomationIdSuffix,
    string OwnerControlType,
    string OwnerClassName,
    string OwnerAutomationIdSuffix,
    string MarkerRelativeAutomationIdSuffix,
    int OwnerAncestorDepth,
    UiPatternKind RequiredOwnerPattern);

/// <summary>
/// Decides which — if any — marker/owner pair may be invoked for a signed action marker
/// (Epic 11300 Part B2A §8).
/// </summary>
/// <remarks>
/// Pure and static, like <see cref="MeituCardTargetRule"/>, and it refuses in the same two
/// directions: zero acceptable candidates and more than one acceptable candidate both fail.
/// There is no "closest invokable ancestor" and no "first text match".
///
/// The anchoring problem here is sharper than it was on the start page, and the evidence says
/// so. Every entry in the editor's 高级调整 list — 消除笔, <c>AI变清晰</c>, 无痕改字, AI换背景 —
/// reports the <i>identical</i> automation id <c>…contentsWidget.ModuleButton</c> for its owner
/// and <c>…contentsWidget.ModuleButton.buttonWidget.titleLabel</c> for its marker. The id is
/// therefore a shape check and nothing else; the only thing that separates "make this sharper"
/// from "replace this background" is the marker's name, so the walk starts there.
/// </remarks>
public static class MeituEnhancementTargetRule
{
    /// <summary>
    /// Returns the index of the one candidate whose owner may be invoked, or a failure.
    /// </summary>
    /// <param name="shape">The signed structural description.</param>
    /// <param name="markerName">The signed marker text the control must be titled with.</param>
    /// <param name="expectedProcessId">The verified Meitu process both elements must belong to.</param>
    /// <param name="candidates">Every marker match found beneath the verified window, with its walked owner.</param>
    public static OperationResult<int> SelectActionOwner(
        MeituOwnedControlShape shape,
        string markerName,
        int expectedProcessId,
        IReadOnlyList<MeituCardCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentException.ThrowIfNullOrWhiteSpace(markerName);
        ArgumentNullException.ThrowIfNull(candidates);

        List<int> accepted = [];
        List<string> rejections = [];

        for (int index = 0; index < candidates.Count; index++)
        {
            ImmutableArray<string> reasons = Reject(shape, markerName, expectedProcessId, candidates[index]);
            if (reasons.IsEmpty)
            {
                accepted.Add(index);
            }
            else
            {
                rejections.Add($"candidate {index}: {string.Join("; ", reasons)}");
            }
        }

        if (accepted.Count == 1)
        {
            return OperationResult.Ok(accepted[0]);
        }

        return OperationResult.Fail<int>(OperationFailure.Create(
            FailureCode.MeituUnknownState,
            accepted.Count > 1
                ? $"'{markerName}' resolved to {accepted.Count} structurally valid controls beneath the " +
                  "verified Meitu window; PrintFlow will not choose between them. No input was sent."
                : candidates.Count == 0
                    ? $"No element named '{markerName}' exists beneath the verified Meitu window, so the " +
                      "Enhancement control it titles could not be located. No input was sent."
                    : $"None of the {candidates.Count} element(s) named '{markerName}' is titled by a control " +
                      "matching the signed Enhancement structure. No input was sent.",
            isRetryable: false,
            context: new Dictionary<string, string>
            {
                ["marker"] = markerName,
                ["candidates"] = candidates.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["accepted"] = accepted.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["rejections"] = rejections.Count == 0 ? "(none)" : string.Join(" | ", rejections),
                ["inputSent"] = "false",
            }));
    }

    /// <summary>Every reason this candidate may not be invoked. Empty means it may.</summary>
    private static ImmutableArray<string> Reject(
        MeituOwnedControlShape shape, string markerName, int expectedProcessId, MeituCardCandidate candidate)
    {
        ImmutableArray<string>.Builder reasons = ImmutableArray.CreateBuilder<string>();
        UiElementIdentity marker = candidate.Marker;

        if (!string.Equals(marker.Name, markerName, StringComparison.Ordinal))
        {
            reasons.Add($"marker name is '{marker.Name}', not exactly '{markerName}'");
        }

        if (!string.Equals(marker.ControlTypeName, shape.MarkerControlType, StringComparison.Ordinal))
        {
            reasons.Add($"marker control type is {marker.ControlTypeName}, not {shape.MarkerControlType}");
        }

        if (!string.Equals(marker.ClassName, shape.MarkerClassName, StringComparison.Ordinal))
        {
            reasons.Add($"marker class is '{marker.ClassName}', not '{shape.MarkerClassName}'");
        }

        if (!marker.AutomationId.EndsWith(shape.MarkerAutomationIdSuffix, StringComparison.Ordinal))
        {
            reasons.Add($"marker id '{marker.AutomationId}' does not end with '{shape.MarkerAutomationIdSuffix}'");
        }

        if (marker.ProcessId != expectedProcessId)
        {
            reasons.Add($"marker belongs to process {marker.ProcessId}, not the verified {expectedProcessId}");
        }

        if (candidate.Owner is not { } owner)
        {
            // Either the marker has no ancestor at the signed depth, or one of the levels could
            // not be read. Both mean the tree is not the tree the evidence describes, and
            // neither licenses looking one level further.
            reasons.Add($"no ancestor {shape.OwnerAncestorDepth} level(s) above the marker was readable");
            return reasons.ToImmutable();
        }

        if (!string.Equals(owner.ControlTypeName, shape.OwnerControlType, StringComparison.Ordinal))
        {
            reasons.Add($"owner control type is {owner.ControlTypeName}, not {shape.OwnerControlType}");
        }

        if (!string.Equals(owner.ClassName, shape.OwnerClassName, StringComparison.Ordinal))
        {
            reasons.Add($"owner class is '{owner.ClassName}', not '{shape.OwnerClassName}'");
        }

        if (!owner.AutomationId.EndsWith(shape.OwnerAutomationIdSuffix, StringComparison.Ordinal))
        {
            reasons.Add($"owner id '{owner.AutomationId}' does not end with '{shape.OwnerAutomationIdSuffix}'");
        }

        // The tie that makes this one structural relation rather than two independent shape
        // checks. Meitu's Qt ids encode the widget path, so the marker's id must be the owner's
        // id plus the exact intervening segments the evidence recorded — '.buttonWidget' as well
        // as '.titleLabel'. A same-shaped control elsewhere in the tree cannot satisfy this
        // against *this* marker.
        if (!string.Equals(
                marker.AutomationId,
                owner.AutomationId + shape.MarkerRelativeAutomationIdSuffix,
                StringComparison.Ordinal))
        {
            reasons.Add(
                $"marker id '{marker.AutomationId}' is not owner id '{owner.AutomationId}' plus " +
                $"'{shape.MarkerRelativeAutomationIdSuffix}'");
        }

        if (owner.ProcessId != expectedProcessId)
        {
            reasons.Add($"owner belongs to process {owner.ProcessId}, not the verified {expectedProcessId}");
        }

        if (!owner.Supports(shape.RequiredOwnerPattern))
        {
            reasons.Add($"owner does not support {shape.RequiredOwnerPattern}");
        }

        if (!owner.IsEnabled)
        {
            reasons.Add("owner is disabled");
        }

        if (owner.IsOffscreen)
        {
            reasons.Add("owner is offscreen");
        }

        if (!owner.Bounds.Contains(marker.Bounds))
        {
            reasons.Add($"owner bounds {owner.Bounds} do not enclose marker bounds {marker.Bounds}");
        }

        return reasons.ToImmutable();
    }
}
