namespace PrintFlow.App.Navigation;

/// <summary>
/// Asks the operator for one destination folder for a final save (SCRUM-11145).
/// </summary>
/// <remarks>
/// A pick only edits the displayed draft. It returns a path or null on cancel and never
/// approves, saves, checks or remembers anything.
/// </remarks>
public interface IDeliveryFolderPicker
{
    /// <param name="initialFolder">The folder already visible in the draft, or null; never hidden history.</param>
    string? PickFolder(string dialogTitle, string? initialFolder);
}
