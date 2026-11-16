using System.Diagnostics;
using System.Text.Json;
using PrintFlow.Infrastructure.Adapters.Photoshop;

namespace PrintFlow.Tests.Integration.Automation;

public sealed class PhotoshopMoveToolProgramTests
{
    [Theory]
    [InlineData("cropTool", 1)]
    [InlineData("bucketTool", 1)]
    [InlineData("moveTool", 0)]
    public async Task The_loaded_document_uses_the_Move_tool_before_processing(string initialTool, int selections)
    {
        JsonElement result = await RunAsync(initialTool);

        result.GetProperty("response").GetString().ShouldBe("PF-MOVE-1\nmoveTool");
        result.GetProperty("tool").GetString().ShouldBe("moveTool");
        result.GetProperty("selections").GetInt32().ShouldBe(selections);
    }

    [Fact]
    public async Task A_tool_selection_that_does_not_take_effect_is_refused()
    {
        JsonElement result = await RunAsync("cropTool", refuseSelection: true);

        result.GetProperty("response").GetString().ShouldStartWith("PF-MOVE-1\nERROR\n");
        result.GetProperty("tool").GetString().ShouldBe("cropTool");
        result.GetProperty("selections").GetInt32().ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_missing_or_different_document_is_refused_without_selecting_a_tool(bool noDocument)
    {
        JsonElement result = await RunAsync("cropTool", noDocument: noDocument, wrongDocument: !noDocument);

        result.GetProperty("response").GetString().ShouldStartWith("PF-MOVE-1\nERROR\n");
        result.GetProperty("tool").GetString().ShouldBe("cropTool");
        result.GetProperty("selections").GetInt32().ShouldBe(0);
    }

    private static async Task<JsonElement> RunAsync(
        string tool, bool refuseSelection = false, bool noDocument = false, bool wrongDocument = false)
    {
        // Execute the actual ExtendScript with only the external Photoshop object substituted.
        const string runner = """
            const fs = require('node:fs'), vm = require('node:vm');
            const input = JSON.parse(fs.readFileSync(0, 'utf8'));
            let tool = input.tool, selections = 0;
            const document = { fullName: { fsName: input.wrongDocument ? 'C:\\other.png' : input.path } };
            const app = { documents: input.noDocument ? [] : [document], activeDocument: document };
            Object.defineProperty(app, 'currentTool', {
                get: () => tool,
                set: value => { selections++; if (!input.refuseSelection) tool = value; }
            });
            const response = vm.runInNewContext(input.program, { app }, { timeout: 1000 });
            process.stdout.write(JSON.stringify({ response, tool, selections }));
            """;
        ProcessStartInfo start = new("node")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(runner);
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        const string path = @"C:\managed\Working\image.png";
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new
        {
            tool, refuseSelection, noDocument, wrongDocument, path,
            program = PhotoshopMoveToolProgram.Create(path),
        }));
        process.StandardInput.Close();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        process.ExitCode.ShouldBe(0, await stderr);
        using JsonDocument result = JsonDocument.Parse(await stdout);
        return result.RootElement.Clone();
    }
}
