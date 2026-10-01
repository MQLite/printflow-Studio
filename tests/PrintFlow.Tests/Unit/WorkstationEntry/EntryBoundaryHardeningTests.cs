using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using PrintFlow.App.Navigation;
using PrintFlow.Tests.Fixtures;
using PrintFlow.WorkstationEntry;
using PrintFlow.WorkstationEntry.Scenarios;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

/// <summary>
/// SCRUM-11154 F-V7 (2026-10-01): the remaining Interactive entry paths that could end the visible
/// host — a folder created inside a native dialog, an unbound prepared fixture ledger and an
/// unexpected dispatcher fault. Owned, nonvisible fixtures only.
/// </summary>
public sealed class EntryBoundaryHardeningTests
{
    [Fact]
    public void New_folder_made_in_the_delivery_dialog_is_refused_as_no_selection_without_mutation()
    {
        using OwnedFixture fixture = new();
        string created = Path.Combine(fixture.Paths.At("delivery"), "新建文件夹");
        Directory.CreateDirectory(created);
        List<string> notices = [];
        ContainedNativePorts ports = new(fixture.Paths, folders: new FixedFolderPicker(created), refused: notices.Add);

        ports.PickFolder("Save to", null).ShouldBeNull();

        ports.NativeDispatches.ShouldBe(1);
        ports.PickerRefusals.ShouldBe(1);
        notices.Single().ShouldContain("测试");
        Directory.EnumerateFileSystemEntries(created).ShouldBeEmpty("an unprepared folder is neither adopted nor marked");
    }

    [Fact]
    public void New_folder_made_in_the_package_save_dialog_is_refused_as_no_selection()
    {
        using OwnedFixture fixture = new();
        string created = Path.Combine(fixture.Paths.At("diagnostics", "export"), "New folder");
        Directory.CreateDirectory(created);
        ContainedNativePorts ports = new(fixture.Paths, packages: new FixedDestinationPicker(Path.Combine(created, "pkg.zip")));

        ports.PickDestination("Save", "ZIP|*.zip", "pkg.zip").ShouldBeNull();

        ports.PickerRefusals.ShouldBe(1);
        Directory.EnumerateFileSystemEntries(created).ShouldBeEmpty();
    }

    [Fact]
    public void Prepared_delivery_and_export_destinations_remain_usable()
    {
        using OwnedFixture fixture = new();
        string delivery = fixture.Paths.At("delivery");
        string package = fixture.Paths.At("diagnostics", "export", "pkg.zip");

        new ContainedNativePorts(fixture.Paths, folders: new FixedFolderPicker(delivery)).PickFolder("Save to", null).ShouldBe(delivery);
        new ContainedNativePorts(fixture.Paths, packages: new FixedDestinationPicker(package))
            .PickDestination("Save", "ZIP|*.zip", "pkg.zip").ShouldBe(package);
    }

    [Fact]
    public void Cancelled_folder_dialog_is_no_selection_and_no_refusal()
    {
        using OwnedFixture fixture = new();
        ContainedNativePorts ports = new(fixture.Paths, folders: new FixedFolderPicker(null));
        ports.PickFolder("Save to", null).ShouldBeNull();
        ports.NativeDispatches.ShouldBe(1);
        ports.PickerRefusals.ShouldBe(0);
    }

    [Fact]
    public void Junction_inside_an_owned_delivery_tree_still_refuses_the_run()
    {
        using OwnedFixture fixture = new();
        string outside = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "prerequisite-sentinels", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        string link = Path.Combine(fixture.Paths.At("delivery"), "linked");
        ProcessStartInfo start = new("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + outside.Replace("'", "''") + "' | Out-Null");
        using (Process setup = Process.Start(start)!)
        { setup.WaitForExit(10000).ShouldBeTrue(); setup.ExitCode.ShouldBe(0, setup.StandardError.ReadToEnd()); }

        try
        {
            ContainedNativePorts ports = new(fixture.Paths, folders: new FixedFolderPicker(link));
            Should.Throw<IOException>(() => ports.PickFolder("Save to", null));
            ports.PickerRefusals.ShouldBe(0);
            ContainedNativePorts nested = new(fixture.Paths, folders: new FixedFolderPicker(Path.Combine(link, "新建文件夹")));
            Directory.CreateDirectory(Path.Combine(outside, "新建文件夹"));
            Should.Throw<IOException>(() => nested.PickFolder("Save to", null), "a new folder reached through an alias is not a refusal");
            nested.PickerRefusals.ShouldBe(0);
        }
        finally { Directory.Delete(link); } // Removes the junction only, never its target.
    }

    [Fact]
    public void Prepared_record_without_a_bound_scenario_ledger_digest_refuses_Interactive()
    {
        (string root, RunOwnership owner) = PreparedRoot();
        File.WriteAllText(Path.Combine(root, "state", "prepared.json"), JsonSerializer.Serialize(new
        {
            owner.CandidateHash, owner.ScenarioHash, owner.RunToken, owner.OwnerToken,
            PreparedUtc = DateTimeOffset.UtcNow, Evidence = "NONINTERACTIVE_ONLY",
        }));
        Should.Throw<ArgumentException>(() => OwnedRun.VerifyResume(root, owner.CandidateHash, owner.ScenarioHash, true));
    }

    [Fact]
    public void Matching_prepared_record_returns_its_bound_ledger_digest()
    {
        (string root, RunOwnership owner) = PreparedRoot();
        string digest = new('C', 64);
        File.WriteAllText(Path.Combine(root, "state", "prepared.json"), JsonSerializer.Serialize(new PreparedRun(owner.CandidateHash, owner.ScenarioHash,
            owner.RunToken, owner.OwnerToken, DateTimeOffset.UtcNow, "NONINTERACTIVE_ONLY", digest)));
        OwnedRun.VerifyPrepared(root, owner, owner.CandidateHash, owner.ScenarioHash).ScenarioLedgerSha256.ShouldBe(digest);
        File.WriteAllText(Path.Combine(root, "state", "prepared.json"), JsonSerializer.Serialize(new PreparedRun(owner.CandidateHash, owner.ScenarioHash,
            owner.RunToken, owner.OwnerToken, DateTimeOffset.UtcNow, "NONINTERACTIVE_ONLY", "not-a-digest")));
        Should.Throw<ArgumentException>(() => OwnedRun.VerifyPrepared(root, owner, owner.CandidateHash, owner.ScenarioHash));
    }

    [Fact]
    public void Ledger_save_returns_the_digest_of_the_exact_bytes_written()
    {
        string root = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "refusal-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "scenario-ledger.json");
        string digest = new ScenarioLedger().Save(path, _ => { });
        digest.ShouldBe(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("missing")]
    public void Ledger_whose_bytes_do_not_match_the_prepared_digest_admits_nothing(string variant)
    {
        string root = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "refusal-fixtures", Guid.NewGuid().ToString("N"));
        string inputs = Path.Combine(root, "fixtures", "inputs"); Directory.CreateDirectory(inputs); Directory.CreateDirectory(Path.Combine(root, "evidence"));
        string fixture = Path.Combine(inputs, "asset.png"); File.WriteAllText(fixture, "asset");
        ScenarioLedger ledger = new(); ScenarioEntry entry = new("F1", "fixture");
        entry.Facts.Add(new ScenarioFact("immutable synthetic fixture", new() { ["role"] = "inputs", ["path"] = fixture,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture))) }));
        ledger.Scenarios.Add(entry);
        string ledgerPath = Path.Combine(root, "evidence", "scenario-ledger.json");
        string bound = ledger.Save(ledgerPath, _ => { });
        if (variant == "tampered") File.AppendAllText(ledgerPath, " ");
        using OwnedPaths paths = new(root);
        Should.Throw<IOException>(() => paths.AdmitPreparedFixtures(ledgerPath, variant == "missing" ? null : bound));
        paths.SelectionRefusal(fixture, "fixtures").ShouldNotBeNull("no fixture was admitted");
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(InvalidOperationException))]
    public void Dispatcher_fault_ends_the_owned_wait_is_recorded_and_reports_nonzero(Type faultType)
    {
        WpfRendering.OnStaThread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            using HostFaultGate faults = new(dispatcher);
            using CancellationTokenSource run = new();
            System.Windows.Controls.Border content = new();
            faults.CancelOnFault(run);
            faults.DisableOnFault(content);
            TaskCompletionSource closed = new();
            DispatcherFrame frame = new();
            Task ended = faults.WhenClosedOrFaulted(closed.Task);
            ended.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            DispatcherTimer bound = new(TimeSpan.FromSeconds(10), DispatcherPriority.Normal, (_, _) => frame.Continue = false, dispatcher);
            dispatcher.BeginInvoke(() => throw (Exception)Activator.CreateInstance(faultType, "unexpected command fault")!);

            Should.NotThrow(() => Dispatcher.PushFrame(frame));
            bound.Stop();

            ended.IsCompleted.ShouldBeTrue("the wait ended without a Closed event");
            closed.Task.IsCompleted.ShouldBeFalse();
            faults.Fault.ShouldBeOfType(faultType);
            faults.FaultCount.ShouldBe(1);
            run.IsCancellationRequested.ShouldBeTrue("every mode's owned awaits stop on the first fault");
            content.IsEnabled.ShouldBeFalse("no further operator input reaches the product");
            faults.Outcome(0).ShouldBe(HostFaultGate.ExitCode);
            faults.Outcome(3).ShouldBe(HostFaultGate.ExitCode);
        });
    }

    [Fact]
    public void Normal_close_reports_the_normal_code_and_a_later_fault_still_reports_nonzero()
    {
        WpfRendering.OnStaThread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            using HostFaultGate faults = new(dispatcher);
            TaskCompletionSource closed = new();
            closed.SetResult();
            faults.WhenClosedOrFaulted(closed.Task).IsCompleted.ShouldBeTrue();
            faults.Outcome(0).ShouldBe(0);
            faults.Outcome(3).ShouldBe(3);

            DispatcherFrame frame = new();
            dispatcher.BeginInvoke(() => throw new IOException("fault during shutdown"));
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            faults.Outcome(0).ShouldBe(HostFaultGate.ExitCode);
        });
    }

    internal static (string Root, RunOwnership Owner) PreparedRoot()
    {
        string root = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "refusal-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "state"));
        using NativePathLease held = NativePathLease.Ancestors(root);
        RunOwnership owner = new(Path.GetFileName(root), held.Identity, "candidate", "scenario", int.MaxValue, 0, "run", "owner");
        File.WriteAllText(Path.Combine(root, "ownership.json"), JsonSerializer.Serialize(owner));
        return (root, owner);
    }

    /// <summary>A real owner claim over a fresh run root with the entry's prepared role directories.</summary>
    internal sealed class OwnedFixture : IDisposable
    {
        private readonly NativePathLease chain;
        private readonly OwnedInstanceClaim claim;
        public OwnedPaths Paths { get; }
        public OwnedFixture()
        {
            string id = "hardening-" + Guid.NewGuid().ToString("N")[..12];
            string root = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "runs", id);
            EntryPlan.Validate(root, new(EntryTestPaths.Repository, "3999287c7921a0322072603dcf0dfd0f4ea5d043", [@"D:\PrintFlowStudio"], []), new(id, "Fake", ["F1", "F2", "F3", "F4", "F5", "F6"]));
            using (NativePathLease before = NativePathLease.Ancestors(root)) Directory.CreateDirectory(root);
            chain = NativePathLease.Ancestors(root);
            using Process self = Process.GetCurrentProcess();
            RunOwnership owner = new(id, chain.Identity, "fixture-candidate", "fixture-scenarios", Environment.ProcessId,
                self.StartTime.ToUniversalTime().Ticks, Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
            claim = OwnedInstanceClaim.Acquire(root, owner, true);
            Paths = new(root);
            Paths.AttachOwner(claim);
            foreach (string area in new[] { "delivery", "diagnostics", Path.Combine("diagnostics", "export") })
                Paths.EnsureDirectory(Paths.At(area));
        }
        public void Dispose() { Paths.Dispose(); claim.Dispose(); chain.Dispose(); }
    }

    private sealed class FixedFolderPicker(string? path) : IDeliveryFolderPicker
    {
        public string? PickFolder(string dialogTitle, string? initialFolder) => path;
    }
    private sealed class FixedDestinationPicker(string path) : IDiagnosticPackageDestinationPicker
    {
        public string? PickDestination(string dialogTitle, string filter, string suggestedFileName) => path;
    }
}
