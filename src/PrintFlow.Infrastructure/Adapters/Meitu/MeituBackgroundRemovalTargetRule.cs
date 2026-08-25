using System.Collections.Immutable;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// Selects the one exact fixed-depth owner of a signed Background Removal page marker.
/// </summary>
public static class MeituBackgroundRemovalTargetRule
{
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

        return accepted.Count == 1
            ? OperationResult.Ok(accepted[0])
            : OperationResult.Fail<int>(OperationFailure.Create(
                FailureCode.MeituUnknownState,
                accepted.Count > 1
                    ? $"'{markerName}' resolved to {accepted.Count} valid Background Removal controls; " +
                      "PrintFlow will not choose between them. No input was sent."
                    : candidates.Count == 0
                        ? $"No exact marker named '{markerName}' exists beneath the verified Meitu editor. " +
                          "No input was sent."
                        : $"None of the {candidates.Count} '{markerName}' candidate(s) matches the signed " +
                          "Background Removal structure. No input was sent.",
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

    private static ImmutableArray<string> Reject(
        MeituOwnedControlShape shape,
        string markerName,
        int expectedProcessId,
        MeituCardCandidate candidate)
    {
        ImmutableArray<string>.Builder reasons = ImmutableArray.CreateBuilder<string>();
        UiElementIdentity marker = candidate.Marker;

        if (!string.Equals(marker.Name, markerName, StringComparison.Ordinal))
            reasons.Add($"marker name is '{marker.Name}', not exactly '{markerName}'");
        if (!string.Equals(marker.ControlTypeName, shape.MarkerControlType, StringComparison.Ordinal))
            reasons.Add($"marker control type is {marker.ControlTypeName}, not {shape.MarkerControlType}");
        if (!string.Equals(marker.ClassName, shape.MarkerClassName, StringComparison.Ordinal))
            reasons.Add($"marker class is '{marker.ClassName}', not '{shape.MarkerClassName}'");
        if (!marker.AutomationId.EndsWith(shape.MarkerAutomationIdSuffix, StringComparison.Ordinal))
            reasons.Add($"marker id '{marker.AutomationId}' does not end with '{shape.MarkerAutomationIdSuffix}'");
        if (marker.ProcessId != expectedProcessId)
            reasons.Add($"marker belongs to process {marker.ProcessId}, not the verified {expectedProcessId}");

        if (candidate.Owner is not { } owner)
        {
            reasons.Add($"no owner exists at signed depth {shape.OwnerAncestorDepth}");
            return reasons.ToImmutable();
        }

        if (!string.Equals(owner.ControlTypeName, shape.OwnerControlType, StringComparison.Ordinal))
            reasons.Add($"owner control type is {owner.ControlTypeName}, not {shape.OwnerControlType}");
        if (!string.Equals(owner.ClassName, shape.OwnerClassName, StringComparison.Ordinal))
            reasons.Add($"owner class is '{owner.ClassName}', not '{shape.OwnerClassName}'");
        if (!owner.AutomationId.EndsWith(shape.OwnerAutomationIdSuffix, StringComparison.Ordinal))
            reasons.Add($"owner id '{owner.AutomationId}' does not end with '{shape.OwnerAutomationIdSuffix}'");
        if (!string.Equals(
                marker.AutomationId,
                owner.AutomationId + shape.MarkerRelativeAutomationIdSuffix,
                StringComparison.Ordinal))
            reasons.Add("marker and owner automation ids do not have the signed fixed-depth relationship");
        if (owner.ProcessId != expectedProcessId)
            reasons.Add($"owner belongs to process {owner.ProcessId}, not the verified {expectedProcessId}");
        if (!owner.Supports(shape.RequiredOwnerPattern))
            reasons.Add($"owner does not support {shape.RequiredOwnerPattern}");
        if (!owner.IsEnabled)
            reasons.Add("owner is disabled");
        if (owner.IsOffscreen)
            reasons.Add("owner is offscreen");
        if (!owner.Bounds.Contains(marker.Bounds))
            reasons.Add($"owner bounds {owner.Bounds} do not enclose marker bounds {marker.Bounds}");

        if (shape.OwnerAncestorDepth == 0 && !Equals(marker, owner))
            reasons.Add("signed depth zero requires the exact marker element to be the actionable owner");

        return reasons.ToImmutable();
    }
}
