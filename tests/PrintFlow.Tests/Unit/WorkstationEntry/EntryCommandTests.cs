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
}
