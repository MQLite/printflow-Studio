using System.Globalization;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Ids;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// One Abandon waiting for the operator's explicit confirmation on Home (SCRUM-11154 F-V4).
/// </summary>
/// <remarks>
/// Presentation state only: it captures which job the operator chose and grants nothing. The
/// confirmation button's fresh-gesture guard targets <see cref="GestureTarget"/>, so a key or
/// click that began before this confirmation was shown cannot complete it.
/// </remarks>
public sealed record AbandonConfirmation(SessionId Id, string DisplayName, bool FromRecovery)
{
    /// <summary>A fresh identity per confirmation, never reused for another one.</summary>
    public string GestureTarget { get; } = Guid.NewGuid().ToString("N");

    /// <summary>The exact job name and the truthful consequence, in the operator's language.</summary>
    public string Question => string.Format(CultureInfo.CurrentCulture, Strings.Resolve("Home_AbandonConfirmQuestion"), DisplayName);
}
