namespace PrintFlow.App.Controls;

/// <summary>A display state, never a workflow or navigation permission.</summary>
public enum GuidedStepState
{
    Pending,
    Current,
    Completed,
    Attention,
    Skipped,
}

/// <summary>
/// Caller-owned, localized step presentation. Ordering and business authority stay with the caller.
/// </summary>
public sealed record GuidedStepItem(
    string Id, string Title, string Detail, string StatusText, GuidedStepState State);
