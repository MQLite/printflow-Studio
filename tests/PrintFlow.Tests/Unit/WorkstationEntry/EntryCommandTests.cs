using System.IO;
using PrintFlow.WorkstationEntry;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

public sealed class EntryCommandTests
{
    [Theory]
    [InlineData("")]
    [InlineData("--Mode Validate")]
    [InlineData("--Mode Interactive")]
    [InlineData("--Mode Production")]
    [InlineData("--Mode Validate --Mode PrepareAndSmoke")]
    [InlineData("--Unknown value")]
    public void Invalid_options_refuse_without_entering_a_runtime_phase(string command)
    {
        using StringWriter output = new();
        EntryCommand.Execute(command.Split(' ', StringSplitOptions.RemoveEmptyEntries), output).ShouldBe(2);
        output.ToString().ShouldContain("REFUSED");
    }

    // SCRUM-11154 F-V7: the fault-path proof can never reach a visible, resumed or restarted run.
    [Theory]
    [InlineData("--Mode Interactive --SafeDesktopConfirmed")]
    [InlineData("--Mode Validate")]
    [InlineData("--Mode PrepareAndSmoke --Resume")]
    [InlineData("--Mode PrepareAndSmoke --OwnedRestart")]
    public void Host_fault_injection_is_refused_outside_a_fresh_PrepareAndSmoke_run(string mode)
    {
        string[] args = [.. mode.Split(' '), "--Root", @"C:\r", "--ScenarioManifest", @"C:\s", "--CandidateManifest", @"C:\c", "--InjectHostFault"];
        Should.Throw<ArgumentException>(() => EntryOptions.Parse(args)).Message.ShouldContain("fault injection");
    }

    [Fact]
    public void Host_fault_injection_parses_for_a_fresh_PrepareAndSmoke_run_and_defaults_off()
    {
        string[] args = ["--Mode", "PrepareAndSmoke", "--Root", @"C:\r", "--ScenarioManifest", @"C:\s", "--CandidateManifest", @"C:\c"];
        EntryOptions.Parse(args).InjectHostFault.ShouldBeFalse();
        EntryOptions.Parse([.. args, "--InjectHostFault"]).InjectHostFault.ShouldBeTrue();
    }
}
