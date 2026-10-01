using System.IO;
using System.Security.Cryptography;
using PrintFlow.App.Navigation;
using PrintFlow.WorkstationEntry;
using PrintFlow.WorkstationEntry.Scenarios;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

/// <summary>
/// Visible-run regression (2026-09-30): a contained refusal of a native pick escaped the product
/// command and ended the Interactive host before its settled shutdown path.
/// </summary>
public sealed class InteractivePickerContainmentTests
{
    [Fact]
    public void Native_pick_of_an_unadmitted_fixture_is_refused_as_no_selection()
    {
        string root = NewRoot(); string file = Fixture(root, "returns", "wrong-size.png", "wrong");
        using OwnedPaths paths = new(root); List<string> refused = [];
        ContainedNativePorts ports = new(paths, new FixedFilePicker(file), refused: refused.Add);
        ports.PickSingleFile("Import", "PNG|*.png", null).ShouldBeNull();
        ports.NativeDispatches.ShouldBe(1); ports.PickerRefusals.ShouldBe(1); refused.Count.ShouldBe(1);
    }

    [Fact]
    public void Scripted_pick_of_an_unadmitted_fixture_still_throws()
    {
        string root = NewRoot(); string file = Fixture(root, "inputs", "source.png", "source");
        using OwnedPaths paths = new(root);
        ContainedNativePorts ports = new(paths) { NextFixture = file };
        Should.Throw<IOException>(() => ports.PickSingleFile("Import", "PNG|*.png"));
    }

    [Fact]
    public void Native_folder_pick_outside_delivery_is_refused_as_no_selection()
    {
        string root = NewRoot(); Directory.CreateDirectory(Path.Combine(root, "workspace"));
        using OwnedPaths paths = new(root);
        ContainedNativePorts ports = new(paths, folders: new FixedFolderPicker(Path.Combine(root, "workspace")));
        ports.PickFolder("Save to", null).ShouldBeNull();
        ports.PickerRefusals.ShouldBe(1);
    }

    [Fact]
    public void Native_package_destination_outside_export_and_noncanonical_file_are_refused_as_no_selection()
    {
        string root = NewRoot(); Directory.CreateDirectory(Path.Combine(root, "diagnostics", "export"));
        using OwnedPaths paths = new(root);
        new ContainedNativePorts(paths, packages: new FixedDestinationPicker(Path.Combine(root, "evidence", "pkg.zip")))
            .PickDestination("Save", "ZIP|*.zip", "pkg.zip").ShouldBeNull();
        ContainedNativePorts files = new(paths, new FixedFilePicker(root.Replace('\\', '/') + "/fixtures/inputs/a.png"));
        files.PickSingleFile("Import", "PNG|*.png", null).ShouldBeNull();
        files.PickerRefusals.ShouldBe(1);
    }

    [Fact]
    public void Native_pick_inside_its_role_through_a_junction_still_refuses_the_run()
    {
        string root = NewRoot(); string outside = Path.Combine(root, "..", Guid.NewGuid().ToString("N"));
        outside = Path.GetFullPath(outside); Directory.CreateDirectory(outside);
        Directory.CreateDirectory(Path.Combine(root, "delivery"));
        string link = Path.Combine(root, "delivery", "linked");
        System.Diagnostics.ProcessStartInfo start = new("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + outside.Replace("'", "''") + "' | Out-Null");
        using (System.Diagnostics.Process setup = System.Diagnostics.Process.Start(start)!)
        { setup.WaitForExit(10000).ShouldBeTrue(); setup.ExitCode.ShouldBe(0, setup.StandardError.ReadToEnd()); }
        using OwnedPaths paths = new(root);
        ContainedNativePorts ports = new(paths, folders: new FixedFolderPicker(link));
        Should.Throw<IOException>(() => ports.PickFolder("Save to", null));
        ports.PickerRefusals.ShouldBe(0);
    }

    [Fact]
    public void Prepared_fixtures_are_admitted_by_recorded_path_and_hash()
    {
        string root = NewRoot();
        string input = Fixture(root, "inputs", "F1-asset.png", "asset");
        string returned = Fixture(root, "returns", "corrected.png", "corrected");
        string ledger = Ledger(root, (input, Hash(input)), (returned, Hash(returned)));
        using OwnedPaths paths = new(root);
        paths.AdmitPreparedFixtures(ledger).ShouldBe(2);
        new ContainedNativePorts(paths, new FixedFilePicker(returned)).PickSingleFile("Import", "PNG|*.png", null).ShouldBe(returned);
        Should.Throw<IOException>(() => File.WriteAllText(returned, "replaced"));
    }

    [Fact]
    public void Prepared_fixture_with_changed_bytes_refuses_admission()
    {
        string root = NewRoot(); string file = Fixture(root, "returns", "corrected.png", "corrected");
        string ledger = Ledger(root, (file, Convert.ToHexString(SHA256.HashData("other"u8.ToArray()))));
        using OwnedPaths paths = new(root);
        Should.Throw<IOException>(() => paths.AdmitPreparedFixtures(ledger));
    }

    [Fact]
    public void Ledger_entry_outside_picker_subtrees_refuses_admission()
    {
        string root = NewRoot(); string file = Fixture(root, "", "loose.png", "loose");
        string ledger = Ledger(root, (file, Hash(file)));
        using OwnedPaths paths = new(root);
        Should.Throw<IOException>(() => paths.AdmitPreparedFixtures(ledger));
    }

    private static string NewRoot()
    {
        string root = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "refusal-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "evidence")); return root;
    }
    private static string Fixture(string root, string role, string leaf, string content)
    {
        string directory = Path.Combine(root, "fixtures", role); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, leaf); File.WriteAllText(path, content); return path;
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    // Written through the real ledger type so a schema change breaks this test, not a visible run.
    private static string Ledger(string root, params (string Path, string Sha256)[] fixtures)
    {
        ScenarioLedger ledger = new(); ScenarioEntry entry = new("F4", "Bound correction return");
        foreach ((string path, string sha) in fixtures)
            entry.Facts.Add(new ScenarioFact("immutable synthetic fixture", new() { ["role"] = "returns", ["path"] = path, ["sha256"] = sha }));
        ledger.Scenarios.Add(entry);
        string ledgerPath = Path.Combine(root, "evidence", "scenario-ledger.json");
        ledger.Save(ledgerPath, _ => { });
        return ledgerPath;
    }
    private sealed class FixedFilePicker(string path) : IFilePicker
    {
        public string? PickSingleFile(string dialogTitle, string filter) => path;
    }
    private sealed class FixedFolderPicker(string path) : IDeliveryFolderPicker
    {
        public string? PickFolder(string dialogTitle, string? initialFolder) => path;
    }
    private sealed class FixedDestinationPicker(string path) : IDiagnosticPackageDestinationPicker
    {
        public string? PickDestination(string dialogTitle, string filter, string suggestedFileName) => path;
    }
}
