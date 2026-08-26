using System.Collections.Immutable;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// One candidate cancel element and the control-view ancestors walked up from it.
/// </summary>
/// <param name="Control">The element whose automation name matched the signed cancel name.</param>
/// <param name="AncestorClassNames">
/// The classes of its ancestors, innermost first, as far as the walk reached. Short is a
/// refusal rather than a licence to accept what was found.
/// </param>
public sealed record MeituBusyCancelCandidate(
    UiElementIdentity Control, ImmutableArray<string> AncestorClassNames);

/// <summary>
/// Decides whether an operation Meitu is running may be cancelled, and through which exact
/// element (Epic 11300 Part D2A §6, §7, §8, §9, §10).
/// </summary>
/// <remarks>
/// Pure and static, like <see cref="MeituStateClassifier"/> and <see cref="MeituCardTargetRule"/>,
/// and for the sharper version of the same reason: every interesting case here is a
/// <b>refusal</b>, and a refusal is only worth trusting if a test can construct the situation
/// that must produce it. Every rule below therefore has a unit test built from records, with no
/// Meitu, no window and no running operation.
///
/// Three independent things must hold before a cancel is eligible, and none of them substitutes
/// for another (§7):
/// <list type="number">
///   <item><b>The operation is one the evidence covers.</b> The signed evidence lists the
///   operations whose cancellation was positively observed; anything else is refused
///   outright.</item>
///   <item><b>The operation's own Busy signature matches, now.</b> This is the load correlation
///   §7 requires, and it is the <i>only</i> thing that makes the cancel operation-specific —
///   because the control is not. Meitu raises one shared progress mask for both operations, so
///   the element that cancels an enhancement is the same element that cancels a cutout.</item>
///   <item><b>The control resolves uniquely and structurally.</b> Exact name, the signed
///   automation-id path, control type, class, pattern, process, enabled, on-screen, the signed
///   ancestor chain, and exactly one match.</item>
/// </list>
///
/// What is <b>absent</b> is the point. There is no <c>FindCancelByName</c>, no substring match,
/// no "first invokable element", no coordinate, no keystroke and no Escape fallback (§8). The
/// reason is a decoy this workstation actually produces: the Windows file picker Meitu opens has
/// its own Cancel button whose automation name is <i>exactly</i> 取消, so a name search would
/// find it and dismiss the file dialog instead of cancelling anything. It is separated here by
/// the automation id and, independently, by the class.
/// </remarks>
public static class MeituBusyCancelRule
{
    /// <summary>
    /// Whether the signed evidence and the current observation together permit a cancel to be
    /// resolved at all (§7).
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="SelectCancelControl"/> and deliberately called
    /// first. Asking "may this operation be cancelled from this screen?" before asking "which
    /// element cancels it?" means a walk of Meitu's tree never happens for an operation that was
    /// not eligible — and, more importantly, that a successful walk can never be mistaken for an
    /// answer to the first question.
    /// </remarks>
    public static OperationResult<Unit> Eligible(
        MeituBaseline baseline, MeituOperation operation, MeituObservation observation)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(observation);

        if (baseline.BusyCancel is not { } signature)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                "The verified preset vouches for no Meitu cancel evidence, so PrintFlow has no proven " +
                "control to invoke. Orchestration stopped; the external operation may still be running " +
                "and no input was sent.",
                isRetryable: false,
                context: Context(operation, "no signed cancel evidence")));
        }

        if (!signature.Covers(operation.ToString()))
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                $"The signed cancel evidence does not cover the '{operation}' operation, so PrintFlow " +
                "will not invoke it. No input was sent.",
                isRetryable: false,
                context: Context(operation, "operation not covered by signed evidence")));
        }

        // The load correlation. Note that this asks the operation's OWN rule rather than the
        // classifier's generic Busy state: MeituStateClassifier reports Busy when *either*
        // operation's signature matches, and "something is running" is not the claim that
        // licenses cancelling *this* operation (§7).
        bool busy = operation == MeituOperation.Enhance
            ? baseline.Enhancement is { } enhancement && MeituEnhancementRule.IsBusy(enhancement, observation)
            : baseline.BackgroundRemoval is { } removal &&
              MeituBackgroundRemovalRule.Classify(removal, observation) == MeituBackgroundRemovalPhase.Busy;

        if (!busy)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                $"The signed Busy signature for '{operation}' does not match the current screen, so " +
                "PrintFlow cannot establish that this operation is the one running. Nothing was cancelled " +
                "and no input was sent.",
                isRetryable: false,
                context: Context(operation, "current-load Busy correlation absent")));
        }

        return OperationResult.Ok();
    }

    /// <summary>
    /// Returns the index of the one candidate that may be invoked, or a failure (§8, §10).
    /// </summary>
    /// <param name="signature">The signed cancel evidence.</param>
    /// <param name="expectedProcessId">The verified Meitu process the element must belong to.</param>
    /// <param name="candidates">Every element beneath the verified window whose name matched, with its ancestry.</param>
    /// <remarks>
    /// Refuses in both directions, exactly as <see cref="MeituCardTargetRule.SelectOwningCard"/>
    /// does: zero acceptable candidates and more than one acceptable candidate are both
    /// failures. Two structurally valid cancels beneath one window means the screen is not the
    /// screen the evidence describes, and §10 is explicit that PrintFlow does not guess.
    /// </remarks>
    public static OperationResult<int> SelectCancelControl(
        MeituBusyCancelSignature signature,
        int expectedProcessId,
        IReadOnlyList<MeituBusyCancelCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(candidates);

        List<int> accepted = [];
        List<string> rejections = [];

        for (int index = 0; index < candidates.Count; index++)
        {
            ImmutableArray<string> reasons = Reject(signature, expectedProcessId, candidates[index]);
            if (reasons.IsEmpty)
            {
                accepted.Add(index);
            }
            else
            {
                rejections.Add($"candidate {index} ({candidates[index].Control}): {string.Join("; ", reasons)}");
            }
        }

        if (accepted.Count == 1)
        {
            return OperationResult.Ok(accepted[0]);
        }

        return OperationResult.Fail<int>(OperationFailure.Create(
            FailureCode.PreconditionNotMet,
            accepted.Count > 1
                ? $"'{signature.Control.Name}' resolved to {accepted.Count} structurally valid cancel controls " +
                  "beneath the verified Meitu window; PrintFlow will not choose between them. No input was sent."
                : candidates.Count == 0
                    ? $"No element named '{signature.Control.Name}' exists beneath the verified Meitu window, so " +
                      "the running operation's cancel control could not be located. No input was sent."
                    : $"None of the {candidates.Count} element(s) named '{signature.Control.Name}' matches the " +
                      "signed cancel structure. No input was sent.",
            isRetryable: false,
            context: new Dictionary<string, string>
            {
                ["control"] = signature.Control.Name,
                ["candidates"] = candidates.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["accepted"] = accepted.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["rejections"] = rejections.Count == 0 ? "(none)" : string.Join(" | ", rejections),
                ["inputSent"] = "false",
                ["meituCancelInvoked"] = "false",
            }));
    }

    /// <summary>
    /// Every reason this candidate may not be invoked. Empty means it may.
    /// </summary>
    /// <remarks>
    /// All reasons are collected rather than returned at the first failure, so a Meitu upgrade
    /// that moves the progress mask produces the whole list of what stopped matching rather than
    /// whichever check happens to be written first.
    /// </remarks>
    private static ImmutableArray<string> Reject(
        MeituBusyCancelSignature signature, int expectedProcessId, MeituBusyCancelCandidate candidate)
    {
        ImmutableArray<string>.Builder reasons = ImmutableArray.CreateBuilder<string>();
        UiElementIdentity control = candidate.Control;
        MeituControlSignature expected = signature.Control;

        if (!string.Equals(control.Name, expected.Name, StringComparison.Ordinal))
        {
            reasons.Add($"name is '{control.Name}', not exactly '{expected.Name}'");
        }

        // The check that separates the operation's cancel from the file picker's. The picker's
        // Cancel is named exactly 取消 and carries the automation id '2'; it fails here, and
        // independently on the class below.
        if (expected.AutomationIdContains.Length == 0 ||
            !control.AutomationId.Contains(expected.AutomationIdContains, StringComparison.Ordinal))
        {
            reasons.Add($"id '{control.AutomationId}' does not contain '{expected.AutomationIdContains}'");
        }

        if (!string.Equals(control.ControlTypeName, expected.ControlTypeName, StringComparison.Ordinal))
        {
            reasons.Add($"control type is {control.ControlTypeName}, not {expected.ControlTypeName}");
        }

        if (!string.Equals(control.ClassName, expected.ClassName, StringComparison.Ordinal))
        {
            reasons.Add($"class is '{control.ClassName}', not '{expected.ClassName}'");
        }

        if (control.ProcessId != expectedProcessId)
        {
            reasons.Add($"belongs to process {control.ProcessId}, not the verified {expectedProcessId}");
        }

        if (!control.Supports(expected.RequiredPattern))
        {
            reasons.Add($"does not support {expected.RequiredPattern}");
        }

        if (!control.IsEnabled)
        {
            reasons.Add("is disabled");
        }

        if (control.IsOffscreen)
        {
            reasons.Add("is offscreen");
        }

        AppendAncestryRejections(signature, candidate, reasons);
        return reasons.ToImmutable();
    }

    /// <summary>
    /// Checks the resolved element actually sits beneath the signed progress-mask chain.
    /// </summary>
    /// <remarks>
    /// A second, independent statement of what the automation id already encodes. That
    /// redundancy is the value: the id is a string Meitu happens to compose from the widget
    /// path, and an element whose id merely <i>contains</i> the expected substring while living
    /// somewhere else entirely would pass the id check and fail here.
    /// </remarks>
    private static void AppendAncestryRejections(
        MeituBusyCancelSignature signature,
        MeituBusyCancelCandidate candidate,
        ImmutableArray<string>.Builder reasons)
    {
        if (signature.RequiredAncestorClassNames.IsDefaultOrEmpty)
        {
            // A signature with no ancestry would accept the element on its own properties, and
            // "the button says 取消 and has a plausible id" is exactly the evidence-free
            // shortcut this rule exists to prevent.
            reasons.Add("the signed evidence records no required ancestor chain");
            return;
        }

        if (candidate.AncestorClassNames.IsDefaultOrEmpty ||
            candidate.AncestorClassNames.Length < signature.RequiredAncestorClassNames.Length)
        {
            reasons.Add(
                $"ancestry is {candidate.AncestorClassNames.Length} level(s) deep, fewer than the signed " +
                $"{signature.RequiredAncestorClassNames.Length}");
            return;
        }

        for (int depth = 0; depth < signature.RequiredAncestorClassNames.Length; depth++)
        {
            string wanted = signature.RequiredAncestorClassNames[depth];
            string actual = candidate.AncestorClassNames[depth];
            if (!string.Equals(actual, wanted, StringComparison.Ordinal))
            {
                reasons.Add($"ancestor at depth {depth + 1} is '{actual}', not '{wanted}'");
            }
        }
    }

    private static Dictionary<string, string> Context(MeituOperation operation, string why) => new()
    {
        ["operation"] = operation.ToString(),
        ["reason"] = why,
        ["inputSent"] = "false",
        ["meituCancelInvoked"] = "false",
        ["forceTerminationInvoked"] = "false",
    };
}
