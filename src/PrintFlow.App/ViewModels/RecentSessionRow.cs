using System.Globalization;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Ids;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// One "Recent Processing" row, already localised and formatted for display.
/// </summary>
/// <remarks>
/// Immutable and computed once from a <see cref="SessionListItem"/>: a row is a snapshot of
/// what persistence said, so Home refreshes by rebuilding the list rather than by mutating
/// rows that might no longer match the database.
/// <para>
/// It shows what an operator needs to recognise their work — name, workflow, where it got to,
/// state, when it last changed. It deliberately shows no workspace path, no original source
/// path and no identifier: <see cref="Id"/> exists only so an entry action knows which session
/// to act on (Part 3C2 §8).
/// </para>
/// </remarks>
public sealed class RecentSessionRow
{
    internal RecentSessionRow(SessionListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Id = item.Id;
        DisplayName = item.OutputName.Value;
        Workflow = DisplayNames.Workflow(item.WorkflowType);
        CurrentStep = DisplayNames.Step(item.CurrentStep);
        State = DisplayNames.SessionState(item.State);
        UpdatedAt = item.UpdatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        CanAbandon = item.CanAbandon;
        CanContinueProcessing = item.CanContinueProcessing;
    }

    /// <summary>Which session an entry action applies to. Never displayed.</summary>
    public SessionId Id { get; }

    /// <summary>The output name — the operator's own name for this piece of work.</summary>
    public string DisplayName { get; }

    /// <summary>The localised workflow name.</summary>
    public string Workflow { get; }

    /// <summary>The localised step the session is waiting on.</summary>
    public string CurrentStep { get; }

    /// <summary>The localised session state.</summary>
    public string State { get; }

    /// <summary>When the session last changed, in the workstation's local time.</summary>
    public string UpdatedAt { get; }

    /// <summary>Whether Home offers Abandon, as reported by the workflow layer.</summary>
    public bool CanAbandon { get; }

    /// <summary>Whether opening this session means resuming work rather than reading a record.</summary>
    public bool CanContinueProcessing { get; }

    /// <summary>
    /// "Resume" for a session that can still progress, "Details" for a finished or abandoned
    /// one — the honest label for what the button actually does (Part 3C2 §11).
    /// </summary>
    public string OpenActionLabel =>
        CanContinueProcessing ? Strings.Home_Resume : Strings.Home_Details;
}
