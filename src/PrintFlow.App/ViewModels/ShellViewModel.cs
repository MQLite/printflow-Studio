using CommunityToolkit.Mvvm.ComponentModel;
using PrintFlow.App.Resources;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// The shell view model for the Part 1 slice.
/// </summary>
/// <remarks>
/// Its only job is to prove that the projects compose and the application starts. It holds
/// no session, touches no file, and issues no command — the Studio UI is a later slice
/// (Epic 11100 plan §16.2).
///
/// Every operator-visible string comes from <c>Strings.resx</c> so the Chinese-first
/// localisation in a later slice is a translation job, not a string-extraction refactor.
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject
{
    public string Heading => Strings.Shell_Heading;

    public string Subheading => Strings.Shell_Subheading;

    public string FoundationNotice => Strings.Shell_FoundationNotice;
}
