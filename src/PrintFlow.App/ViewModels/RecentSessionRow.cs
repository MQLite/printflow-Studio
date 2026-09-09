using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Ids;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// One "Recent Processing" row, already localised and formatted for display.
/// </summary>
/// <remarks>
/// Computed once from a <see cref="SessionListItem"/>: a row is a snapshot of what persistence
/// said, so Home refreshes by rebuilding the list rather than by mutating rows that might no
/// longer match the database.
/// <para>
/// It shows what an operator needs to recognise their work — a picture of it, their name for
/// it, the workflow, where it got to, state, when it last changed. It deliberately shows no
/// workspace path, no original source path and no identifier: <see cref="Id"/> exists only so
/// an entry action knows which session to act on (Part 3C2 §8).
/// </para>
/// <para>
/// The one thing that arrives later is <see cref="Thumbnail"/>, which is why this is observable
/// at all. Everything else is fixed at construction; the picture is decoded off the UI thread
/// after the list is on screen, so opening Home never waits on image decoding (Jira 11602).
/// A row that never receives one simply stays without a picture — that is a display state, not
/// a problem with the job, and every other action on the row is unaffected.
/// </para>
/// </remarks>
public sealed partial class RecentSessionRow : ObservableObject
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
        CanRemoveRecord = item.CanRemoveRecord;
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
    /// Whether Home offers to take this record off the list, as reported by the workflow layer.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="SessionListItem.CanRemoveRecord"/> rather than decided here: the
    /// rule about which jobs are safe to take off Recent Processing belongs to the workflow
    /// layer, and a screen that re-derived it would be a second copy of it (Jira 11602).
    /// </remarks>
    public bool CanRemoveRecord { get; }

    /// <summary>
    /// The encoded PNG bytes of this job's picture, empty until — or unless — one arrives.
    /// </summary>
    /// <remarks>
    /// Turned into a bitmap at bind time by <c>PreviewPayloadConverter</c>, exactly as a review
    /// pane's payload is, so no view model opens a stream and dropping the row drops the last
    /// reference to the bytes (Epic 11200 Part C1 §6, §29).
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    [NotifyPropertyChangedFor(nameof(ThumbnailState))]
    private ReadOnlyMemory<byte> _thumbnail;

    /// <summary>True exactly when there is a picture to draw.</summary>
    public bool HasThumbnail => !Thumbnail.IsEmpty;

    /// <summary>
    /// What the picture area should say when there is no picture.
    /// </summary>
    /// <remarks>
    /// One neutral line, and the same one whether the artefact is still being decoded, was never
    /// displayable, or has been cleaned up. An operator's next action does not differ between
    /// those, and a list that spelled out three shades of "no preview" would be noise
    /// (Epic 11200 Part C1 §21).
    /// </remarks>
    public string ThumbnailState => HasThumbnail ? string.Empty : Strings.Home_RecentNoThumbnail;

    /// <summary>The accessible name of this row's picture, or of the space where one would be.</summary>
    public string ThumbnailName =>
        string.Format(CultureInfo.CurrentCulture, Strings.Home_RecentThumbnailOf, DisplayName);

    /// <summary>
    /// "Resume" for a session that can still progress, "Details" for a finished or abandoned
    /// one — the honest label for what the button actually does (Part 3C2 §11).
    /// </summary>
    public string OpenActionLabel =>
        CanContinueProcessing ? Strings.Home_Resume : Strings.Home_Details;
}
