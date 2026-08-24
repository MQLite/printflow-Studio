using System.Collections.Immutable;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// The signed structural description of a Meitu start-page card and the text marker that names
/// it (Epic 11300 Part B1 §3, §4).
/// </summary>
/// <param name="LabelControlType">The marker element's control type, e.g. <c>Text</c>.</param>
/// <param name="LabelClassName">The marker element's framework class, e.g. <c>QLabel</c>.</param>
/// <param name="LabelAutomationIdSuffix">
/// What the marker's automation id ends with, e.g. <c>.titleLabel</c>. This is also the exact
/// string the owning card's id must gain to become the marker's id, which is the strongest
/// single check in the rule.
/// </param>
/// <param name="CardControlType">The owning card's control type, e.g. <c>CheckBox</c>.</param>
/// <param name="CardClassName">The owning card's framework class, e.g. <c>CardButton</c>.</param>
/// <param name="CardAutomationIdSuffix">What the card's automation id ends with, e.g. <c>.CardButton</c>.</param>
/// <param name="RequiredCardPattern">The pattern the card must expose before it may be invoked.</param>
/// <remarks>
/// Every field comes from signed workstation evidence, never from a constant in the adapter.
/// The reason is the defect this rule exists to fix: Part A configured the target by <i>name</i>
/// and got Meitu's title label, which advertises <c>InvokePattern</c> and does nothing when
/// invoked. A name alone cannot distinguish a label from the control it labels, so the shape
/// has to be described — and a description PrintFlow could edit for itself would be an
/// assumption wearing evidence's clothes.
/// </remarks>
public sealed record MeituCardShape(
    string LabelControlType,
    string LabelClassName,
    string LabelAutomationIdSuffix,
    string CardControlType,
    string CardClassName,
    string CardAutomationIdSuffix,
    UiPatternKind RequiredCardPattern);

/// <summary>One signed marker element and the ancestor put forward as its owning card.</summary>
/// <param name="Marker">The element whose name matched the signed marker.</param>
/// <param name="Owner">
/// The candidate owner walked to from <paramref name="Marker"/>, or <c>null</c> when the walk
/// found none — which is a refusal, not a licence to look further afield.
/// </param>
public sealed record MeituCardCandidate(UiElementIdentity Marker, UiElementIdentity? Owner);

/// <summary>
/// Decides which — if any — candidate card may be invoked for a signed start-page marker.
/// </summary>
/// <remarks>
/// Pure and static, like <see cref="MeituStateClassifier"/> and for the same reason: the
/// interesting cases are all refusals, and a refusal is only worth trusting if a test can
/// construct the situation that must produce it. Every case in §5 — no owner, two plausible
/// owners, an owner in the wrong process, an owner whose shape does not match — is therefore a
/// unit test over records rather than a manual check against a running Meitu.
///
/// Two properties are worth stating because they are what make the rule safe rather than merely
/// specific:
/// <list type="bullet">
///   <item><b>It never picks.</b> Zero acceptable candidates and two acceptable candidates both
///   fail. There is no "first clickable ancestor" fallback, which matters more than it sounds:
///   in Meitu's Qt tree <i>every</i> widget from the label up to the window advertises
///   <c>InvokePattern</c>, so "the first invokable ancestor" would have accepted the label
///   itself — exactly the Part A defect.</item>
///   <item><b>It anchors on the marker.</b> The eight cards on Meitu's start page all share the
///   automation id <c>…functionWidget.CardButton</c>; the id alone identifies a card only up to
///   an eight-way ambiguity that includes 海报设计 and 批处理. What disambiguates is the signed
///   marker text, so the walk starts there and the id is a shape check rather than an
///   address.</item>
/// </list>
/// </remarks>
public static class MeituCardTargetRule
{
    /// <summary>
    /// Returns the index of the one candidate whose owner may be invoked, or a failure.
    /// </summary>
    /// <param name="shape">The signed structural description.</param>
    /// <param name="markerName">The signed marker text the card must be titled with.</param>
    /// <param name="expectedProcessId">The verified Meitu process both elements must belong to.</param>
    /// <param name="candidates">Every marker match found beneath the verified window, with its walked owner.</param>
    public static OperationResult<int> SelectOwningCard(
        MeituCardShape shape,
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
                rejections.Add($"candidate {index} ({Describe(candidates[index])}): {string.Join("; ", reasons)}");
            }
        }

        if (accepted.Count == 1)
        {
            return OperationResult.Ok(accepted[0]);
        }

        if (accepted.Count > 1)
        {
            // Two structurally valid owners for one signed marker is not a tie to break. It
            // means the page is not the page the evidence describes, and invoking either would
            // be a guess about which of two identical-looking cards the operator's work depends
            // on (§5).
            return OperationResult.Fail<int>(OperationFailure.Create(
                FailureCode.MeituOpenInputFailed,
                $"'{markerName}' resolved to {accepted.Count} structurally valid cards beneath the verified " +
                "Meitu window; PrintFlow will not choose between them. No input was sent.",
                isRetryable: false,
                context: Context(markerName, candidates.Count, accepted.Count, rejections)));
        }

        return OperationResult.Fail<int>(OperationFailure.Create(
            FailureCode.MeituOpenInputFailed,
            candidates.Count == 0
                ? $"No element named '{markerName}' exists beneath the verified Meitu window, so the " +
                  "start-page card it titles could not be located. No input was sent."
                : $"None of the {candidates.Count} element(s) named '{markerName}' is titled by a card " +
                  "matching the signed structure. No input was sent.",
            isRetryable: false,
            context: Context(markerName, candidates.Count, accepted.Count, rejections)));
    }

    /// <summary>
    /// Every reason this candidate may not be invoked. Empty means it may.
    /// </summary>
    /// <remarks>
    /// All reasons are collected rather than returned at the first failure: when a Meitu upgrade
    /// moves the start page, the useful diagnostic is the whole list of what stopped matching,
    /// not whichever check happened to be written first.
    /// </remarks>
    private static ImmutableArray<string> Reject(
        MeituCardShape shape, string markerName, int expectedProcessId, MeituCardCandidate candidate)
    {
        ImmutableArray<string>.Builder reasons = ImmutableArray.CreateBuilder<string>();
        UiElementIdentity marker = candidate.Marker;

        // The marker itself first. An element that is not the signed marker cannot lend its
        // authority to whatever happens to be above it.
        if (!string.Equals(marker.Name, markerName, StringComparison.Ordinal))
        {
            // Exact, never a prefix or a substring: the classifier's Part A defect was an
            // accepted prefix, and "图片编辑历史" is not "图片编辑".
            reasons.Add($"marker name is '{marker.Name}', not exactly '{markerName}'");
        }

        if (!string.Equals(marker.ControlTypeName, shape.LabelControlType, StringComparison.Ordinal))
        {
            reasons.Add($"marker control type is {marker.ControlTypeName}, not {shape.LabelControlType}");
        }

        if (!string.Equals(marker.ClassName, shape.LabelClassName, StringComparison.Ordinal))
        {
            reasons.Add($"marker class is '{marker.ClassName}', not '{shape.LabelClassName}'");
        }

        if (!marker.AutomationId.EndsWith(shape.LabelAutomationIdSuffix, StringComparison.Ordinal))
        {
            reasons.Add($"marker id '{marker.AutomationId}' does not end with '{shape.LabelAutomationIdSuffix}'");
        }

        if (marker.ProcessId != expectedProcessId)
        {
            reasons.Add($"marker belongs to process {marker.ProcessId}, not the verified {expectedProcessId}");
        }

        if (candidate.Owner is not { } owner)
        {
            reasons.Add("no owning element was reachable from the marker");
            return reasons.ToImmutable();
        }

        if (!string.Equals(owner.ControlTypeName, shape.CardControlType, StringComparison.Ordinal))
        {
            reasons.Add($"owner control type is {owner.ControlTypeName}, not {shape.CardControlType}");
        }

        if (!string.Equals(owner.ClassName, shape.CardClassName, StringComparison.Ordinal))
        {
            reasons.Add($"owner class is '{owner.ClassName}', not '{shape.CardClassName}'");
        }

        if (!owner.AutomationId.EndsWith(shape.CardAutomationIdSuffix, StringComparison.Ordinal))
        {
            reasons.Add($"owner id '{owner.AutomationId}' does not end with '{shape.CardAutomationIdSuffix}'");
        }

        // The tie that makes this a structural relation and not two independent shape checks:
        // Meitu's Qt automation ids encode the widget path, so the marker's id is the card's id
        // plus the label's own segment. An unrelated card that merely has the right class cannot
        // satisfy this against *this* marker.
        if (!string.Equals(
                marker.AutomationId,
                owner.AutomationId + shape.LabelAutomationIdSuffix,
                StringComparison.Ordinal))
        {
            reasons.Add(
                $"marker id '{marker.AutomationId}' is not owner id '{owner.AutomationId}' plus " +
                $"'{shape.LabelAutomationIdSuffix}'");
        }

        if (owner.ProcessId != expectedProcessId)
        {
            // The wrong-process case (§5). It cannot arise from a walk that started inside the
            // verified window, which is exactly why it is checked: if it ever does arise, the
            // assumption the walk rested on has been broken and nothing may be invoked.
            reasons.Add($"owner belongs to process {owner.ProcessId}, not the verified {expectedProcessId}");
        }

        if (!owner.Supports(shape.RequiredCardPattern))
        {
            reasons.Add($"owner does not support {shape.RequiredCardPattern}");
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

    /// <summary>
    /// Returns the index of the one candidate matching a signed control signature, or a failure.
    /// </summary>
    /// <param name="signature">The signed description of the control.</param>
    /// <param name="expectedProcessId">The verified process the control must belong to.</param>
    /// <param name="candidates">Every element considered.</param>
    /// <remarks>
    /// Same shape as <see cref="SelectOwningCard"/> and same two refusals — none and more than
    /// one — for the same reason. A control PrintFlow is about to invoke has to be the control
    /// the evidence describes, and "there were two of these" means the screen is not the screen
    /// the evidence describes.
    /// </remarks>
    public static OperationResult<int> SelectSignedControl(
        MeituControlSignature signature,
        int expectedProcessId,
        IReadOnlyList<UiElementIdentity> candidates)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(candidates);

        List<int> accepted = [];
        List<string> rejections = [];

        for (int index = 0; index < candidates.Count; index++)
        {
            ImmutableArray<string> reasons = RejectControl(signature, expectedProcessId, candidates[index]);
            if (reasons.IsEmpty)
            {
                accepted.Add(index);
            }
            else
            {
                rejections.Add($"candidate {index} ({candidates[index]}): {string.Join("; ", reasons)}");
            }
        }

        if (accepted.Count == 1)
        {
            return OperationResult.Ok(accepted[0]);
        }

        return OperationResult.Fail<int>(OperationFailure.Create(
            FailureCode.MeituOpenInputFailed,
            accepted.Count > 1
                ? $"The signed control '{signature.Name}' matched {accepted.Count} elements; PrintFlow will not " +
                  "choose between them. No input was sent."
                : $"No element matching the signed control '{signature.Name}' " +
                  $"('{signature.AutomationIdContains}', {signature.ControlTypeName}/{signature.ClassName}) " +
                  "was found. No input was sent.",
            isRetryable: false,
            context: Context(signature.Name, candidates.Count, accepted.Count, rejections)));
    }

    private static ImmutableArray<string> RejectControl(
        MeituControlSignature signature, int expectedProcessId, UiElementIdentity candidate)
    {
        ImmutableArray<string>.Builder reasons = ImmutableArray.CreateBuilder<string>();

        if (!string.Equals(candidate.Name, signature.Name, StringComparison.Ordinal))
        {
            reasons.Add($"name is '{candidate.Name}', not exactly '{signature.Name}'");
        }

        if (signature.AutomationIdContains.Length > 0 &&
            !candidate.AutomationId.Contains(signature.AutomationIdContains, StringComparison.Ordinal))
        {
            reasons.Add($"id '{candidate.AutomationId}' does not contain '{signature.AutomationIdContains}'");
        }

        if (!string.Equals(candidate.ControlTypeName, signature.ControlTypeName, StringComparison.Ordinal))
        {
            reasons.Add($"control type is {candidate.ControlTypeName}, not {signature.ControlTypeName}");
        }

        if (!string.Equals(candidate.ClassName, signature.ClassName, StringComparison.Ordinal))
        {
            reasons.Add($"class is '{candidate.ClassName}', not '{signature.ClassName}'");
        }

        if (candidate.ProcessId != expectedProcessId)
        {
            reasons.Add($"belongs to process {candidate.ProcessId}, not the verified {expectedProcessId}");
        }

        if (!candidate.Supports(signature.RequiredPattern))
        {
            reasons.Add($"does not support {signature.RequiredPattern}");
        }

        if (!candidate.IsEnabled)
        {
            reasons.Add("is disabled");
        }

        if (candidate.IsOffscreen)
        {
            reasons.Add("is offscreen");
        }

        return reasons.ToImmutable();
    }

    private static string Describe(MeituCardCandidate candidate) =>
        candidate.Owner is null
            ? $"{candidate.Marker} → (no owner)"
            : $"{candidate.Marker} → {candidate.Owner}";

    private static Dictionary<string, string> Context(
        string markerName, int candidateCount, int acceptedCount, IReadOnlyList<string> rejections) =>
        new()
        {
            ["marker"] = markerName,
            ["candidates"] = candidateCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["accepted"] = acceptedCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["rejections"] = rejections.Count == 0 ? "(none)" : string.Join(" | ", rejections),
            ["inputSent"] = "false",
        };
}
