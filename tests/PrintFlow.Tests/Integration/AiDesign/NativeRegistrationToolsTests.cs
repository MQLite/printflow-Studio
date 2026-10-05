using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.AiDesign;
public sealed class NativeRegistrationToolsTests
{
    [Fact]
    public async Task Registration_preview_pins_origin_roots_version_without_writing_registration()
    {
        using TempWorkspace workspace=new();using TempWorkspace downloads=new();
        string repo=FindRepository(),host=Path.Combine(Path.GetDirectoryName(typeof(PrintFlow.NativeHost.Program).Assembly.Location)!,"PrintFlow.NativeHost.exe");
        var result=await Run(Path.Combine(repo,"build","ai-design","Register-NativeHost.ps1"),"-ValidateOnly","-ExtensionId",new string('a',32),"-HostPath",host,"-WorkspaceRoot",workspace.Root,"-DownloadRoot",downloads.Root);
        result.Code.ShouldBe(0,result.Error);using var json=JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("manifest").GetProperty("allowed_origins")[0].GetString().ShouldBe("chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/");
        json.RootElement.GetProperty("configuration").GetProperty("verifiedWorkContractHash").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("configuration").GetProperty("workspaceRoot").GetString().ShouldBe(workspace.Root);
        json.RootElement.GetProperty("writesPerformed").GetBoolean().ShouldBeFalse();
    }
    [Fact]
    public async Task Registration_rejects_nonexistent_host_in_preview()
    {
        using TempWorkspace root=new();var result=await Run(Path.Combine(FindRepository(),"build","ai-design","Register-NativeHost.ps1"),"-ValidateOnly","-ExtensionId",new string('a',32),"-HostPath",Path.Combine(root.Root,"missing.exe"),"-WorkspaceRoot",root.Root,"-DownloadRoot",root.Root);
        result.Code.ShouldNotBe(0);
    }
    private static async Task<(int Code,string Output,string Error)> Run(string script,params string[] args)
    {
        var info=new ProcessStartInfo("powershell"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",script}.Concat(args))info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));return(process.ExitCode,await output,await error);
    }
    private static string FindRepository()
    {for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir is not null;dir=dir.Parent)if(File.Exists(Path.Combine(dir.FullName,"PrintFlowStudio.sln")))return dir.FullName;throw new InvalidOperationException();}
}
