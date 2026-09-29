using System.Globalization;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Results;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// The failed-action notice: what happened, what is known, where to look next, and the exact code
/// under Error details (SCRUM-11151).
/// </summary>
/// <remarks>
/// Presentation only. It names no action of its own. The next step is the status line at the top,
/// which is built from the commands legal now, so Retry is named only where Retry is offered, and a
/// correction-bound job keeps pointing at Import corrected image. The notice never enables, runs or
/// records anything; re-wording it on a language change reads nothing and sends nothing.
/// </remarks>
public partial class SessionViewModel
{
    /// <summary>What the notice may say about the screen after a failure.</summary>
    private enum ActionFailureScreen
    {
        /// <summary>The job is being re-read; no next step is named yet.</summary>
        Pending,

        /// <summary>The screen shows the job as it now is, so the status line is authoritative.</summary>
        Current,

        /// <summary>Re-reading the job failed, so the screen may be out of date.</summary>
        Stale,
    }

    private readonly NoticeFailure _noticeFailure = new();
    private ActionFailureScreen _actionFailureScreen;

    /// <summary>A sentence the screen added to this failure's notice, such as a trim editor closing.</summary>
    private string? _actionFailureAdditionKey;

    /// <summary>The exact stable code of the failure the notice describes; null when it describes none.</summary>
    public string? NoticeErrorCode => _noticeFailure.Code;

    public bool HasNoticeErrorCode => NoticeErrorCode is not null;

    public string NoticeErrorDetailsLabel => Strings.ErrorDetails_Heading;

    public string NoticeErrorCodeLabel => Strings.ErrorDetails_Code;

    public string NoticeErrorCodeHint => Strings.ErrorDetails_CodeHint;

    partial void OnNoticeChanged(string? value)
    {
        _noticeFailure.Track(value);
        NotifyNoticeFailureChanged();
    }

    /// <summary>Shows <paramref name="failure"/> as the notice.</summary>
    private void ShowActionFailure(OperationFailure failure, ActionFailureScreen screen)
    {
        if (!ReferenceEquals(_noticeFailure.Failure, failure)) _actionFailureAdditionKey = null;
        _actionFailureScreen = screen;
        Notice = _noticeFailure.Describe(DescribeActionFailure(failure,
            _actionFailureAdditionKey is { } key ? Strings.Resolve(key) : null, screen), failure);
        NotifyNoticeFailureChanged();
    }

    /// <summary>
    /// Adds the localized <paramref name="messageKey"/> to the notice, keeping that failure's code
    /// and next-step line. Returns false when the notice describes no failure.
    /// </summary>
    private bool AppendToActionFailure(string messageKey)
    {
        if (_noticeFailure.Failure is not { } failure) return false;
        _actionFailureAdditionKey = messageKey;
        ShowActionFailure(failure, _actionFailureScreen);
        return true;
    }

    /// <summary>
    /// Names the next step once the job has been re-read, if the notice still describes
    /// <paramref name="failure"/>. A notice something else has replaced is left alone.
    /// </summary>
    private void SettleActionFailure(OperationFailure failure, bool screenIsCurrent)
    {
        if (!ReferenceEquals(_noticeFailure.Failure, failure)) return;
        ShowActionFailure(failure, screenIsCurrent ? ActionFailureScreen.Current : ActionFailureScreen.Stale);
    }

    /// <summary>The same failure, re-worded in the operator's current language. Reads nothing.</summary>
    private void RefreshActionFailureLanguage()
    {
        if (_noticeFailure.Failure is { } failure) ShowActionFailure(failure, _actionFailureScreen);
    }

    /// <summary>
    /// The failure's own sentence inside the wrapper, and the next-step line.
    /// </summary>
    /// <remarks>
    /// <see cref="OperationFailure.TechnicalDetail"/> is never shown: it is English log text that
    /// can name a path. The code is not in the sentence either; it is under Error details. A
    /// persistence failure is worded as unconfirmed, because an operation with more than one commit
    /// can fail after an earlier commit landed (Part 3C3A §15; SCRUM-11151).
    /// </remarks>
    private static string DescribeActionFailure(OperationFailure failure, string? addition, ActionFailureScreen screen)
    {
        string sentence = string.Format(
            CultureInfo.CurrentCulture,
            failure.Code == FailureCode.PersistenceError ? Strings.Session_ActionUnconfirmed : Strings.Session_ActionFailed,
            DisplayNames.FailureNotice(failure));
        string? next = screen switch
        {
            ActionFailureScreen.Current => Strings.Session_ActionFailedNext,
            ActionFailureScreen.Stale => Strings.Session_ActionFailedNextStale,
            _ => null,
        };
        return JoinSentences(new[] { sentence, addition, next }.OfType<string>().ToList());
    }

    private void NotifyNoticeFailureChanged()
    {
        OnPropertyChanged(nameof(NoticeErrorCode));
        OnPropertyChanged(nameof(HasNoticeErrorCode));
    }
}
