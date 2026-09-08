using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Drives the real PrintFlow desktop application through Windows UI Automation
/// (SCRUM-11092-A §12–§15).
/// </summary>
/// <remarks>
/// Controls are located by <c>AutomationId</c>, control type and UI Automation pattern, and
/// never by screen position: the first live attempt at this proof was blocked because
/// screenshot capture and coordinate input were unavailable on the workstation, and a driver
/// that depends on either can be blocked the same way again. Nothing here reads a pixel or
/// sends a click at a point.
/// <para>
/// Two things about this desktop are worked around rather than assumed, because both were
/// observed here and both silently produce an empty search result:
/// </para>
/// <list type="number">
/// <item>
/// A modal common file dialog does not appear among <see cref="AutomationElement.RootElement"/>'s
/// children, so it is found by its <b>owner window</b> — the identification §13 asks for — and
/// opened with <see cref="AutomationElement.FromHandle"/>.
/// </item>
/// <item>
/// The standard Win32 controls inside that dialog expose no patterns at all until UI
/// Automation's client-side providers are registered, which makes the filename field look like
/// an unusable <c>Pane</c>. <see cref="RegisterClientSideProviders"/> is what turns them back
/// into an <c>Edit</c> with <c>ValuePattern</c> and a <c>Button</c> with <c>InvokePattern</c>.
/// </item>
/// </list>
/// </remarks>
internal static class DesktopAutomation
{
    /// <summary>How long any "wait for the application to catch up" step will wait.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static bool _providersRegistered;

    /// <summary>
    /// Makes the standard Win32 dialog controls expose their documented patterns.
    /// </summary>
    /// <remarks>
    /// Without this, a common file dialog's filename field and Open button are reported as
    /// <c>Pane</c> elements supporting nothing, and the only ways left to operate them would be
    /// the two this slice exists to remove — coordinates and blind keystrokes.
    /// </remarks>
    public static void RegisterClientSideProviders()
    {
        if (_providersRegistered)
        {
            return;
        }

        // Touching the root first is not optional: registration throws if the automation client
        // has not been started yet.
        _ = AutomationElement.RootElement;
        ClientSettings.RegisterClientSideProviderAssembly(new AssemblyName("UIAutomationClientsideProviders"));
        _providersRegistered = true;
    }

    /// <summary>The application's own top-level window, once it has one.</summary>
    public static AutomationElement MainWindow(Process application) =>
        Wait($"the main window of process {application.Id}", () =>
        {
            application.Refresh();
            return AutomationElement.RootElement.FindFirst(
                TreeScope.Children,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ProcessIdProperty, application.Id),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)));
        });

    /// <summary>The one element carrying <paramref name="automationId"/>, once it is on screen.</summary>
    public static AutomationElement Element(AutomationElement scope, string automationId) =>
        Wait($"an element with AutomationId '{automationId}'", () => Look(scope, automationId));

    /// <summary>The element carrying <paramref name="automationId"/>, or null if it is not offered.</summary>
    public static AutomationElement? Look(AutomationElement scope, string automationId)
    {
        foreach (AutomationElement candidate in scope.FindAll(
            TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)))
        {
            if (!candidate.Current.IsOffscreen)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The row whose accessible name is <paramref name="name"/>, among rows of one kind.</summary>
    /// <remarks>
    /// Home offers Resume once per session, so the identity that separates two of them is the
    /// row, not the button: the row is selected by the operator's own output name, and the
    /// action is then located inside it by <c>AutomationId</c>.
    /// </remarks>
    public static AutomationElement Row(AutomationElement scope, string rowAutomationId, string name) =>
        Wait($"a '{rowAutomationId}' row named '{name}'", () => scope.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, rowAutomationId),
                new PropertyCondition(AutomationElement.NameProperty, name))));

    /// <summary>Presses a control the way an assistive technology would.</summary>
    public static void Invoke(AutomationElement element)
    {
        element.Current.IsEnabled.ShouldBeTrue(
            $"'{element.Current.AutomationId}' is on screen but disabled");

        object pattern = element.GetCurrentPattern(InvokePattern.Pattern);
        ((InvokePattern)pattern).Invoke();
    }

    // -------------------------------------------------------------------------------------
    // Keyboard
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Tabs forward until the control with <paramref name="automationId"/> has keyboard focus,
    /// returning the route it walked.
    /// </summary>
    /// <remarks>
    /// The route is read back from <see cref="AutomationElement.FocusedElement"/> after each
    /// press rather than predicted, so what it records is where focus actually went — and the
    /// walk refuses to start, or to continue, unless focus is inside the application. On a
    /// shared desktop <c>SetFocus</c> does not always win the foreground immediately, and a
    /// route recorded while another window had the keyboard would say nothing about this
    /// screen's tab order at all.
    /// </remarks>
    public static IReadOnlyList<string> TabTo(
        Process application, AutomationElement window, string automationId, int limit = 40)
    {
        Wait("the application to take keyboard focus", () =>
        {
            window.SetFocus();
            Thread.Sleep(300);
            return Focused()?.Current.ProcessId == application.Id ? "focused" : null;
        });

        List<string> route = [];
        for (int press = 0; press < limit; press++)
        {
            Press(VkTab);
            Thread.Sleep(120);

            AutomationElement? focused = Focused();
            if (focused is null)
            {
                route.Add("<focus unreadable>");
                continue;
            }

            if (focused.Current.ProcessId != application.Id)
            {
                throw new InvalidOperationException(
                    $"keyboard focus left the application after {press} presses, so this route says nothing "
                    + $"about the session screen. Route: {Describe(route)}");
            }

            string id = focused.Current.AutomationId;
            route.Add(id.Length > 0 ? id : $"<{focused.Current.ControlType.ProgrammaticName
                .Replace("ControlType.", string.Empty)}: {focused.Current.Name}>");

            if (id == automationId)
            {
                return route;
            }
        }

        throw new InvalidOperationException($"Tab never reached '{automationId}'. Route: {Describe(route)}");

        static AutomationElement? Focused()
        {
            try
            {
                return AutomationElement.FocusedElement;
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }
    }

    /// <summary>A tab route as one line of evidence.</summary>
    public static string Describe(IEnumerable<string> route) => string.Join(" -> ", route);

    /// <summary>Presses the space bar, which is how a keyboard operator invokes a focused button.</summary>
    public static void PressSpace() => Press(VkSpace);

    // -------------------------------------------------------------------------------------
    // The system file dialog (§13, §14, §15)
    // -------------------------------------------------------------------------------------

    /// <summary>The handles of every visible dialog the application currently owns.</summary>
    public static IReadOnlyList<IntPtr> OwnedDialogs(Process application)
    {
        application.Refresh();
        return OwnedDialogs(application, application.MainWindowHandle);
    }

    /// <summary>Allows a labelled synthetic WPF test window to supply its exact owner handle.</summary>
    public static IReadOnlyList<IntPtr> OwnedDialogs(Process application, IntPtr owner)
    {
        List<IntPtr> found = [];

        EnumWindows((handle, unused) =>
        {
            _ = GetWindowThreadProcessId(handle, out uint processId);
            if (processId == (uint)application.Id && IsWindowVisible(handle) && GetWindow(handle, GwOwner) == owner)
            {
                StringBuilder className = new(64);
                _ = GetClassName(handle, className, className.Capacity);
                if (className.ToString() == CommonDialogClass)
                {
                    found.Add(handle);
                }
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>
    /// The file dialog that the preceding action opened, and only that one (§15).
    /// </summary>
    /// <param name="before">
    /// The dialogs the application already owned, captured immediately before the action.
    /// </param>
    /// <remarks>
    /// Three things have to agree before this returns: the window belongs to the application's
    /// own process, it is owned by the application's own main window, and it was not there a
    /// moment ago. An unrelated Explorer window fails the first two, and a dialog left over from
    /// an earlier step fails the third. Anything less than all three and this throws rather than
    /// guessing, because the next thing the caller does is choose a file in it.
    /// </remarks>
    public static AutomationElement FileDialogOpenedBy(Process application, IReadOnlyList<IntPtr> before)
        => FileDialogOpenedBy(application, before, application.MainWindowHandle);

    public static AutomationElement FileDialogOpenedBy(Process application, IReadOnlyList<IntPtr> before, IntPtr owner)
    {
        IntPtr handle = Wait("the file dialog the application just opened", () =>
        {
            List<IntPtr> appeared = [.. OwnedDialogs(application, owner).Where(h => !before.Contains(h))];
            return appeared.Count == 1 ? appeared[0] : IntPtr.Zero;
        }, IntPtr.Zero);

        AutomationElement dialog = AutomationElement.FromHandle(handle)
            ?? throw new InvalidOperationException($"the dialog {handle} could not be reached by UI Automation");

        // It must actually look like a file dialog before anything is typed into it.
        _ = FileNameField(dialog);
        _ = ConfirmButton(dialog);

        return dialog;
    }

    /// <summary>Chooses <paramref name="absolutePath"/> in an already-identified file dialog.</summary>
    /// <remarks>
    /// The path is set with <c>ValuePattern</c> and the dialog confirmed with
    /// <c>InvokePattern</c> — the documented patterns for these two standard controls — so no
    /// keystroke is guessed and no coordinate is used.
    /// </remarks>
    public static void Choose(AutomationElement dialog, string absolutePath)
    {
        AutomationElement field = FileNameField(dialog);
        ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).SetValue(absolutePath);

        // Read it back: a dialog that silently refused the path must not be confirmed.
        ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).Current.Value
            .ShouldBe(absolutePath, "the file dialog did not accept the path");

        ((InvokePattern)ConfirmButton(dialog).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }

    /// <summary>The filename field: a direct child of the dialog, identified by its control id.</summary>
    private static AutomationElement FileNameField(AutomationElement dialog) =>
        Child(dialog, FileNameFieldId, "the dialog's file name field");

    /// <summary>The Open/confirm button: a direct child of the dialog, identified by its control id.</summary>
    private static AutomationElement ConfirmButton(AutomationElement dialog) =>
        Child(dialog, ConfirmButtonId, "the dialog's confirm button");

    /// <summary>
    /// One direct child of a dialog, by Win32 control id.
    /// </summary>
    /// <remarks>
    /// Direct children only, and never a descendant search: the shell's file list gives its
    /// items numeric automation ids of their own, so a descendant search for "1" matches a file
    /// in the current folder rather than the Open button.
    /// <para>
    /// The ids are the documented common-dialog control ids, which is what makes this work in
    /// any Windows display language — the button that reads "Open" here reads "打开" on the
    /// accepted workstation, and neither wording appears in this driver.
    /// </para>
    /// </remarks>
    private static AutomationElement Child(AutomationElement dialog, string controlId, string what)
    {
        TreeWalker walker = TreeWalker.ControlViewWalker;
        for (AutomationElement? child = walker.GetFirstChild(dialog);
             child is not null;
             child = walker.GetNextSibling(child))
        {
            if (child.Current.AutomationId == controlId)
            {
                return child;
            }
        }

        throw new InvalidOperationException(
            $"{what} (control id {controlId}) is not there — this window was not positively identified as the "
            + "file dialog, so nothing was selected in it");
    }

    // -------------------------------------------------------------------------------------
    // Waiting
    // -------------------------------------------------------------------------------------

    /// <summary>Polls <paramref name="attempt"/> until it produces something, then returns it.</summary>
    public static T Wait<T>(string what, Func<T?> attempt, T? absent = default)
        where T : notnull
    {
        DateTime deadline = DateTime.UtcNow + Patience;
        do
        {
            T? value = attempt();
            if (value is not null && !EqualityComparer<T?>.Default.Equals(value, absent))
            {
                return value;
            }

            Thread.Sleep(200);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"waited {Patience.TotalSeconds:0}s for {what}, and it never appeared");
    }

    // -------------------------------------------------------------------------------------
    // Win32
    // -------------------------------------------------------------------------------------

    /// <summary>The window class every Windows common dialog uses.</summary>
    private const string CommonDialogClass = "#32770";

    /// <summary>The documented common-dialog control ids: the file name field and Open.</summary>
    private const string FileNameFieldId = "1148";
    private const string ConfirmButtonId = "1";

    private const uint GwOwner = 4;
    private const byte VkTab = 0x09;
    private const byte VkSpace = 0x20;
    private const uint KeyUp = 0x0002;

    private static void Press(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KeyUp, UIntPtr.Zero);
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr unused);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint relationship);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scanCode, uint flags, UIntPtr extraInfo);
}
