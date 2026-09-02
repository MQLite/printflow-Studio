using System.Diagnostics;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Counts the external applications a Production adapter could put on the operator's screen
/// (Epic 11500 Part D §4.4, §6).
/// </summary>
/// <remarks>
/// Process names only. No path, version or digest of the accepted workstation appears in this
/// repository — those live in the signed preset, and a fixture that copied them would be a second
/// place for them to drift. A name is enough for the question being asked here, which is whether
/// something appeared that was not there a moment ago.
/// <para>
/// The count is compared against itself, before and after, rather than asserted to be zero: on a
/// developer's machine Photoshop may well already be open, and the claim under test is that
/// PrintFlow did not start one, not that nobody else did.
/// </para>
/// </remarks>
internal static class ExternalApplicationProbe
{
    private static readonly string[] ProcessNames = ["Photoshop", "XiuXiu"];

    /// <summary>How many Photoshop or Meitu processes are running right now.</summary>
    public static int RunningCount()
    {
        int total = 0;

        foreach (string name in ProcessNames)
        {
            Process[] running = Process.GetProcessesByName(name);
            total += running.Length;

            foreach (Process process in running)
            {
                process.Dispose();
            }
        }

        return total;
    }
}
