using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Automation;
using PrintFlow.Tests.Fixtures;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// SCRUM-11092-A: drives the retained synthetic sessions through the real desktop application
/// with UI Automation, so the live proof needs no screenshot and no coordinate (§12–§15, §20, §21).
/// </summary>
/// <remarks>
/// Opt-in, like the seed and verifier passes beside it: it does nothing unless
/// <c>PRINTFLOW_MANUAL_RESULT_DRIVE</c> names the evidence directory holding
/// <c>DesktopApp\</c>, <c>expectation.json</c> and the synthetic <c>*-manual.png</c> files. It
/// starts and stops that copy of the application itself, and it touches no other window: the
/// only file dialog it operates is one the application opened in answer to the action it had
/// just invoked, identified by owner and process before a path is typed into it.
/// <para>
/// It proves the operator's path twice over and deliberately differently. Live A reaches Submit
/// Manual Result <b>by keyboard</b> — Tab until it has focus, then space — which is the §7
/// contract; Live B reaches it by <c>InvokePattern</c>, which is the §12 contract. Both then
/// choose their file through the real Windows dialog.
/// </para>
/// <para>
/// What it does not do is decide whether the run was correct. Everything it asserts is about
/// the application being operable; the truth of the result — revisions, hashes, review state,
/// downstream input, lock — is read from the database afterwards by
/// <c>Verify_desktop_results_after_restart_and_authoritative_downstream_input</c>, in its own
/// process.
/// </para>
/// </remarks>
public sealed class ManualResultDesktopDriveSmoke(ITestOutputHelper output)
{
    [Fact]
    public void Drive_both_retained_sessions_through_the_real_session_screen()
    {
        string? evidence = Environment.GetEnvironmentVariable("PRINTFLOW_MANUAL_RESULT_DRIVE");
        if (evidence is null)
        {
            return;
        }

        Transcript transcript = new(Path.Combine(evidence, "live-drive-transcript.txt"));
        DesktopAutomation.RegisterClientSideProviders();

        try
        {
            LiveA(evidence, transcript);
            LiveB(evidence, transcript);
        }
        finally
        {
            transcript.Save();
            output.WriteLine(transcript.ToString());
        }
    }

    /// <summary>
    /// Live A: keyboard to Submit Manual Result, import, approve, skip and run the next step (§20).
    /// </summary>
    private static void LiveA(string evidence, Transcript transcript)
    {
        using Desktop desktop = Desktop.Start(evidence, transcript);
        AutomationElement session = desktop.Resume("PF_MANUAL_LIVE_A");

        IReadOnlyList<string> route = desktop.TabTo("Session.SubmitManualResult");
        transcript.Write($"Live A tab route ({route.Count} presses): {DesktopAutomation.Describe(route)}");
        transcript.Write(
            $"Live A keyboard focus rests on \"{AutomationElement.FocusedElement.Current.Name}\", "
            + $"id=\"{AutomationElement.FocusedElement.Current.AutomationId}\"");

        desktop.ChooseThroughTheRealDialog(
            () => DesktopAutomation.PressSpace(),
            Path.Combine(evidence, "PF_MANUAL_LIVE_A-manual.png"),
            "Live A (keyboard: space on the focused button)");

        session = desktop.Session("PF_MANUAL_LIVE_A");
        transcript.Write($"Live A imported, awaiting review. {desktop.ArtefactFacts(session)}");

        DesktopAutomation.Invoke(DesktopAutomation.Element(session, "Session.Approve"));
        transcript.Write("Live A approved the manual result.");

        session = desktop.Session("PF_MANUAL_LIVE_A");
        DesktopAutomation.Invoke(DesktopAutomation.Element(session, "Session.Skip"));
        transcript.Write("Live A skipped the next automated step.");

        session = desktop.Session("PF_MANUAL_LIVE_A");
        DesktopAutomation.Invoke(DesktopAutomation.Element(session, "Session.RunStep"));

        session = desktop.Session("PF_MANUAL_LIVE_A");
        _ = DesktopAutomation.Element(session, "Session.Approve");
        transcript.Write($"Live A ran the following step from the manual result. {desktop.ArtefactFacts(session)}");
    }

    /// <summary>
    /// Live B: invoke Submit Manual Result, import, leave it for review, restart and look again (§21).
    /// </summary>
    private static void LiveB(string evidence, Transcript transcript)
    {
        string before;

        using (Desktop desktop = Desktop.Start(evidence, transcript))
        {
            AutomationElement session = desktop.Resume("PF_MANUAL_LIVE_B");
            AutomationElement submit = DesktopAutomation.Element(session, "Session.SubmitManualResult");
            transcript.Write(
                $"Live B found Session.SubmitManualResult: enabled={submit.Current.IsEnabled}, "
                + $"name=\"{submit.Current.Name}\", invokable={submit.GetSupportedPatterns()
                    .Any(p => p == InvokePattern.Pattern)}");

            desktop.ChooseThroughTheRealDialog(
                () => DesktopAutomation.Invoke(submit),
                Path.Combine(evidence, "PF_MANUAL_LIVE_B-manual.png"),
                "Live B (UI Automation: InvokePattern)");

            session = desktop.Session("PF_MANUAL_LIVE_B");
            _ = DesktopAutomation.Element(session, "Session.Approve");
            before = desktop.ArtefactFacts(session);
            transcript.Write($"Live B imported, left awaiting review, NOT approved. {before}");
        }

        transcript.Write("Live B: application closed normally.");

        using (Desktop desktop = Desktop.Start(evidence, transcript))
        {
            AutomationElement session = desktop.Resume("PF_MANUAL_LIVE_B");
            _ = DesktopAutomation.Element(session, "Session.Approve");
            string after = desktop.ArtefactFacts(session);
            transcript.Write($"Live B after restart. {after}");

            after.ShouldBe(before, "the restarted session shows a different manual result");
        }
    }

    // -------------------------------------------------------------------------------------
    // The application under test
    // -------------------------------------------------------------------------------------

    /// <summary>One run of the retained desktop copy, from launch to normal close.</summary>
    private sealed class Desktop : IDisposable
    {
        private readonly Process _process;
        private readonly Transcript _transcript;

        private Desktop(Process process, Transcript transcript)
        {
            _process = process;
            _transcript = transcript;
            Window = DesktopAutomation.MainWindow(process);
        }

        /// <summary>The application's top-level window.</summary>
        public AutomationElement Window { get; }

        /// <summary>Launches the retained copy and waits for Home.</summary>
        public static Desktop Start(string evidence, Transcript transcript)
        {
            string directory = Path.Combine(evidence, "DesktopApp");
            Process process = Process.Start(new ProcessStartInfo(Path.Combine(directory, "PrintFlow.App.exe"))
            {
                WorkingDirectory = directory,
                UseShellExecute = true,
            })!;

            Desktop desktop = new(process, transcript);
            try
            {
                _ = DesktopAutomation.Element(desktop.Window, "Screen.Home");
            }
            catch
            {
                // A launch that never reached Home must not leave the application running: the
                // next attempt would then find two of them and drive the wrong one.
                desktop.Dispose();
                throw;
            }

            transcript.Write(
                $"started {process.Id}: window \"{desktop.Window.Current.Name}\" "
                + $"id=\"{desktop.Window.Current.AutomationId}\"");

            return desktop;
        }

        /// <summary>Tabs to one control of this application, refusing a route taken elsewhere.</summary>
        public IReadOnlyList<string> TabTo(string automationId) =>
            DesktopAutomation.TabTo(_process, Window, automationId);

        /// <summary>Opens one retained session from Home and returns its Session screen.</summary>
        public AutomationElement Resume(string outputName)
        {
            AutomationElement home = DesktopAutomation.Element(Window, "Screen.Home");
            AutomationElement row = DesktopAutomation.Row(home, "Home.RecentSession", outputName);
            DesktopAutomation.Invoke(DesktopAutomation.Element(row, "Home.ResumeSession"));

            return Session(outputName);
        }

        /// <summary>The Session screen, once it is showing the session it should be.</summary>
        public AutomationElement Session(string outputName) => DesktopAutomation.Wait(
            $"the Session screen for {outputName}",
            () =>
            {
                AutomationElement? screen = DesktopAutomation.Look(Window, "Screen.Session");
                return screen?.Current.Name == outputName ? screen : null;
            });

        /// <summary>
        /// Invokes the action that opens the picker, then chooses a file in the dialog it opened.
        /// </summary>
        /// <remarks>
        /// The dialogs the application already owned are captured first, so the one this
        /// operates is provably the one the action produced, and no window belonging to anything
        /// else on the desktop is looked at (§15).
        /// </remarks>
        public void ChooseThroughTheRealDialog(System.Action open, string absolutePath, string what)
        {
            File.Exists(absolutePath).ShouldBeTrue($"{absolutePath} is not there to choose");

            IReadOnlyList<IntPtr> before = DesktopAutomation.OwnedDialogs(_process);
            open();

            AutomationElement dialog = DesktopAutomation.FileDialogOpenedBy(_process, before);
            _transcript.Write(
                $"{what}: the application opened its own dialog \"{dialog.Current.Name}\" "
                + $"(class {dialog.Current.ClassName}); choosing {Path.GetFileName(absolutePath)} "
                + "with ValuePattern and confirming with InvokePattern.");

            DesktopAutomation.Choose(dialog, absolutePath);
            DesktopAutomation.Wait(
                "the dialog to close", () => DesktopAutomation.OwnedDialogs(_process).Count == 0 ? "closed" : null);
        }

        /// <summary>What the artefact panel says about the file currently on screen.</summary>
        /// <remarks>
        /// Read as text, in reading order, so it means the same thing before and after a
        /// restart. "SHA-256" is a literal on both language builds, which is what makes the
        /// value next to it findable without depending on the workstation's display language.
        /// </remarks>
        public string ArtefactFacts(AutomationElement session)
        {
            List<string> text =
            [
                .. session.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                .Cast<AutomationElement>()
                .Select(element => element.Current.Name),
            ];

            int label = text.IndexOf("SHA-256");
            return label >= 0 && label + 1 < text.Count
                ? $"SHA-256 on screen: {text[label + 1]}"
                : "SHA-256 is not on screen";
        }

        /// <summary>Closes the application the way an operator would, and waits for it to go.</summary>
        public void Dispose()
        {
            if (_process.HasExited)
            {
                return;
            }

            _process.CloseMainWindow();
            if (!_process.WaitForExit((int)DesktopAutomation.Patience.TotalMilliseconds))
            {
                _transcript.Write("the application did not close on request; killing it");
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit();
            }

            _process.Dispose();
        }
    }

    /// <summary>The live operation record this slice keeps in place of screenshots (§23).</summary>
    private sealed class Transcript(string path)
    {
        private readonly StringBuilder _lines = new();

        public void Write(string line)
        {
            _lines.AppendLine($"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}  {line}");
        }

        public void Save() => File.WriteAllText(path, _lines.ToString());

        public override string ToString() => _lines.ToString();
    }
}
