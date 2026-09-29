using PrintFlow.Domain.Results;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// The one failure a screen's notice currently describes, so its in-place Error details show that
/// failure's exact code and no other (SCRUM-11151).
/// </summary>
/// <remarks>
/// Presentation only. The main sentence carries no bare code, but a support call still needs the
/// stable identifier, and the Error Details screen is bound to one persisted failed attempt: a
/// refused command, or a Home, Workflow Selection or Settings failure, has none. The failure is
/// remembered exactly as long as the notice still shows the text written for it; any other notice
/// forgets it, so an earlier failure's code can never sit under a later sentence.
/// </remarks>
internal sealed class NoticeFailure
{
    private string? _text;

    /// <summary>The failure the notice describes, or null when the notice describes none.</summary>
    public OperationFailure? Failure { get; private set; }

    /// <summary>The exact stable code, as the Error Details screen and diagnostic exports show it.</summary>
    public string? Code => Failure?.Code.ToString();

    /// <summary>Remembers <paramref name="failure"/> for <paramref name="text"/> and returns the text to show.</summary>
    public string Describe(string text, OperationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _text = text;
        Failure = failure;
        return text;
    }

    /// <summary>Forgets the failure once the notice no longer shows its text.</summary>
    public void Track(string? notice)
    {
        if (Failure is null || string.Equals(notice, _text, StringComparison.Ordinal)) return;
        Failure = null;
        _text = null;
    }
}
