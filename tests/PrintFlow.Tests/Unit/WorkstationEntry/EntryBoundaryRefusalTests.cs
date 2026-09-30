using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;
using PrintFlow.WorkstationEntry;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

public sealed class EntryBoundaryRefusalTests
{
    [Fact]
    public void Protected_session_name_matches_actual_internal_product_helper()
    {
        foreach (int offset in new[] { -720, 0, 780 })
        {
            PrintFlow.Domain.Ids.SessionId id = new(Guid.NewGuid());
            DateTimeOffset at = new DateTimeOffset(2026, 9, 30, 12, 34, 56, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(offset));
            ContainedWorkspace.SessionDirectoryName(id, at).ShouldBe(PrintFlow.Infrastructure.Workspace.FileWorkspace.BuildSessionDirectoryName(id, at));
        }
    }
    [Fact]
    public void Unknown_factory_and_duplicate_service_refuse_without_factory_invocation()
    {
        bool invoked = false;
        ServiceDescriptor factory = ServiceDescriptor.Singleton<IWorkspace>(_ => { invoked = true; throw new Exception("must not execute"); });
        Should.Throw<ArgumentException>(() => EntryComposition.ValidateDescriptors([factory]));
        Should.Throw<ArgumentException>(() => EntryComposition.ValidateDescriptors([factory, factory]));
        invoked.ShouldBeFalse();
    }
    [Fact]
    public void Missing_and_unwrapped_decoder_graph_refuse_before_resolution()
    {
        Should.Throw<ArgumentException>(() => EntryComposition.ValidateDescriptors([]));
        Should.Throw<ArgumentException>(() => EntryComposition.ValidateDescriptors([ServiceDescriptor.Singleton<IDiagnosticImagePreviewDecoder, DecoderSpy>()]));
    }
    [Fact]
    public async Task Outside_diagnostic_path_refuses_before_inner_decoder()
    {
        string parent = NewFixtureParent();
        string root = Path.Combine(parent, "owned"); Directory.CreateDirectory(Path.Combine(root, "evidence"));
        string sentinel = Path.Combine(parent, "outside.png"); File.WriteAllText(sentinel, "harmless sentinel");
        using OwnedPaths paths = new(root); DecoderSpy spy = new();
        ContainedDiagnosticDecoder decoder = new(paths, spy);
        (await decoder.DecodeDiagnosticAsync(sentinel, CancellationToken.None)).IsFailure.ShouldBeTrue();
        spy.Calls.ShouldBe(0); File.ReadAllText(sentinel).ShouldBe("harmless sentinel");
    }
    [Fact]
    public void Admitted_source_is_held_against_replacement_and_hardlinks_refuse()
    {
        string parent = NewFixtureParent(); string root = Path.Combine(parent, "owned");
        Directory.CreateDirectory(Path.Combine(root, "fixtures", "inputs"));
        string source = Path.Combine(root, "fixtures", "inputs", "source.png"); File.WriteAllText(source, "source");
        string alias = Path.Combine(parent, "alias.png");
        CreateHardLinkW(alias, source, 0).ShouldBeTrue();
        using OwnedPaths paths = new(root);
        Should.Throw<IOException>(() => paths.Admit(source));
        File.Delete(alias); paths.Admit(source);
        Should.Throw<IOException>(() => File.Move(source, source + ".replaced"));
        using NativePathLease held = paths.Read(source, "fixtures");
    }
    [Fact]
    public void Directory_reparse_is_refused_before_any_child_read()
    {
        string parent = NewFixtureParent(); string root = Path.Combine(parent, "owned");
        Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(parent, "outside"));
        string link = Path.Combine(root, "evidence"); string target = Path.Combine(parent, "outside");
        System.Diagnostics.ProcessStartInfo start = new("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' | Out-Null");
        using System.Diagnostics.Process setup = System.Diagnostics.Process.Start(start)!;
        setup.WaitForExit(10000).ShouldBeTrue(); setup.ExitCode.ShouldBe(0, setup.StandardError.ReadToEnd());
        using OwnedPaths paths = new(root);
        Should.Throw<IOException>(() => paths.Require(Path.Combine(root, "evidence", "capture.png"), "evidence"));
    }
    private static string NewFixtureParent()
    {
        string path = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "refusal-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    [Fact]
    public void Mutable_state_allows_content_activity_but_blocks_replacement()
    {
        string root = NewFixtureParent(); Directory.CreateDirectory(Path.Combine(root, "state"));
        using OwnedPaths paths = new(root);
        string file = Path.Combine(root, "state", "owned.lock");
        paths.ProtectMutable(file, createNew: true);
        using (FileStream writer = new(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) writer.WriteByte(42);
        paths.ProtectMutable(file, createNew: false);
        Should.Throw<IOException>(() => File.Move(file, file + ".replaced"));
    }
    [Fact]
    public void Changed_managed_identity_is_refused_even_when_path_is_unchanged()
    {
        string root = NewFixtureParent(); Directory.CreateDirectory(Path.Combine(root, "workspace"));
        string file = Path.Combine(root, "workspace", "revision.png"); File.WriteAllText(file, "first");
        using OwnedPaths paths = new(root);
        using (NativePathLease read = paths.Read(file, "workspace")) { }
        File.Move(file, file + ".old"); File.WriteAllText(file, "replacement");
        Should.Throw<IOException>(() => paths.Read(file, "workspace"));
    }
    [Fact]
    public void Held_directory_allows_owned_child_creation_but_blocks_ancestor_rename()
    {
        string root = NewFixtureParent(); string directory = Path.Combine(root, "owned"); Directory.CreateDirectory(directory);
        using NativePathLease held = NativePathLease.Ancestors(directory);
        File.WriteAllText(Path.Combine(directory, "child.txt"), "owned mutation");
        Should.Throw<IOException>(() => Directory.Move(directory, directory + "-replaced"));
    }
    private sealed class DecoderSpy : IDiagnosticImagePreviewDecoder
    {
        public int Calls { get; private set; }
        public Task<OperationResult<DecodedPreview>> DecodeDiagnosticAsync(string path, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Forbidden decoder callback."); }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newFile, string existing, nint security);
}
