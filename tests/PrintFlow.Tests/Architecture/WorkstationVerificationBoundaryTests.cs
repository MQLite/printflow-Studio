using System.IO;
using System.Reflection;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// The boundaries Epic 11500 Part A must not cross (§23).
/// </summary>
/// <remarks>
/// Environment verification is the part of the system with the strongest pull towards "just read
/// whatever you need": a registry key here, a <c>powershell -c</c> there, a process started to
/// ask it its version. Each of these tests exists because the pull is real and the seam that
/// resists it has to be checked rather than intended.
/// </remarks>
public sealed class WorkstationVerificationBoundaryTests
{
    private const string VerificationNamespace = "PrintFlow.Infrastructure.Verification";

    /// <summary>The verifier and everything it reads with live in Infrastructure.</summary>
    [Fact]
    public void The_verifier_and_its_readers_live_in_Infrastructure()
    {
        foreach (Type type in new[]
                 {
                     typeof(IProductionWorkstationVerifier),
                     typeof(ProductionWorkstationVerifier),
                     typeof(IWorkstationFactReader),
                     typeof(IWorkstationArtifactReader),
                     typeof(WorkstationVerificationResult),
                     typeof(WorkstationVerificationCheck),
                 })
        {
            type.Assembly.GetName().Name.ShouldBe("PrintFlow.Infrastructure");
            type.Namespace.ShouldBe(VerificationNamespace);
        }
    }

    /// <summary>
    /// Nothing above Infrastructure may name a verification type (§23).
    /// </summary>
    /// <remarks>
    /// Domain and Workflow stating environment facts would put registry, Win32 and file-system
    /// concepts inside the layers that are supposed to be portable and deterministic. The App
    /// shell is included because Part A adds no operator surface at all: if a read-model seam is
    /// wanted later, it is a deliberate slice, not something that appears by a view model
    /// happening to reference a verifier (§21).
    /// </remarks>
    [Theory]
    [InlineData("PrintFlow.Domain")]
    [InlineData("PrintFlow.Workflow")]
    [InlineData("PrintFlow.App")]
    public void No_project_above_Infrastructure_names_a_verification_type(string project)
    {
        string[] tokens =
        [
            VerificationNamespace,
            nameof(IProductionWorkstationVerifier),
            nameof(ProductionWorkstationVerifier),
            nameof(IWorkstationFactReader),
            nameof(IWorkstationArtifactReader),
            nameof(WorkstationVerificationResult),
        ];

        AssertAbsent(project, tokens, "verification types belong to PrintFlow.Infrastructure.");
    }

    /// <summary>
    /// Domain and Workflow contain no registry, Win32 or shell environment code at all (§23).
    /// </summary>
    [Theory]
    [InlineData("PrintFlow.Domain")]
    [InlineData("PrintFlow.Workflow")]
    public void Domain_and_Workflow_contain_no_environment_reading_code(string project)
    {
        string[] tokens =
        [
            // "RegistryKey" and the two hive roots rather than the bare word "Registry", which
            // is a perfectly ordinary noun elsewhere in the solution (AutomationRunRegistry).
            "Microsoft.Win32", "RegistryKey", "Registry.LocalMachine", "Registry.CurrentUser",
            "DllImport", "LibraryImport",
            "Process.Start", "ProcessStartInfo", "Environment.OSVersion", "GetSystemMetrics",
        ];

        AssertAbsent(project, tokens, "environment facts are read only in PrintFlow.Infrastructure.");
    }

    /// <summary>
    /// No arbitrary shell, script or command surface exists anywhere in the solution (§18).
    /// </summary>
    /// <remarks>
    /// The check is on the whole of <c>src</c> rather than on the verification namespace: a
    /// <c>RunPowerShell(string)</c> is no less dangerous for living beside the code that wanted
    /// it. Verification reads named facts through named readers, and the way to keep that true
    /// is for the general-purpose alternative not to exist.
    /// </remarks>
    [Theory]
    [InlineData("powershell")]
    [InlineData("PowerShell")]
    [InlineData("cmd.exe")]
    [InlineData("RunPowerShell")]
    [InlineData("RunCommand")]
    [InlineData("RunScript")]
    public void No_shell_or_script_execution_surface_exists_anywhere_in_src(string bannedToken)
    {
        foreach (string project in AllProjects)
        {
            AssertAbsent(project, [bannedToken], $"'{bannedToken}' is an arbitrary execution surface.");
        }
    }

    /// <summary>
    /// Starting a process is confined to the one guarded launch site Epic 11300 established.
    /// </summary>
    /// <remarks>
    /// Not banned outright, because production automation genuinely does start the accepted
    /// Meitu and Photoshop binaries — through
    /// <c>Win32ExternalAppWindowLocator</c>, which launches only the accepted executable path and
    /// re-identifies the process it got back. What must stay true is that no <i>other</i> place
    /// acquires the ability: not the layers above Infrastructure, and above all not verification,
    /// which establishes static facts and must never bring an application onto the operator's
    /// screen to do it (§17).
    /// </remarks>
    [Theory]
    [InlineData("ProcessStartInfo")]
    [InlineData("Process.Start")]
    public void Only_the_guarded_launch_site_may_start_a_process(string bannedToken)
    {
        foreach (string project in new[] { "PrintFlow.Domain", "PrintFlow.Workflow", "PrintFlow.App" })
        {
            AssertAbsent(project, [bannedToken], $"'{bannedToken}' belongs to the guarded launch site alone.");
        }

        AssertAbsentUnder(
            Path.Combine(ProjectDirectory("PrintFlow.Infrastructure"), "Verification"),
            [bannedToken],
            "static verification launches nothing.");
    }

    /// <summary>
    /// The registry is reached only through the one fixed value the edition name needs (§18).
    /// </summary>
    /// <remarks>
    /// Not "no registry at all" — the Windows edition name has no other source — but "no reader
    /// that takes a path". Exactly one file may open a registry key, it is not public, and it
    /// exposes no method through which a caller could name a different one.
    /// </remarks>
    [Fact]
    public void Only_the_workstation_fact_reader_touches_the_registry_and_it_takes_no_path()
    {
        List<string> offenders = [];
        foreach (string project in AllProjects)
        {
            foreach ((string file, string[] lines) in SourceOf(project))
            {
                if (Path.GetFileName(file) == "Win32WorkstationFactReader.cs")
                {
                    continue;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    if (!IsComment(lines[i]) &&
                        (lines[i].Contains("Microsoft.Win32.Registry", StringComparison.Ordinal) ||
                         lines[i].Contains("RegistryKey", StringComparison.Ordinal)))
                    {
                        offenders.Add($"{project}/{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty("the registry is read in exactly one narrowly scoped reader.");

        typeof(IWorkstationFactReader).GetMethods()
            .SelectMany(m => m.GetParameters())
            .ShouldBeEmpty("a fact reader that took an argument could be pointed at anything.");
    }

    /// <summary>Static verification never starts an application (§17).</summary>
    [Theory]
    [InlineData(".Start(")]
    [InlineData("Launch")]
    public void No_verification_source_file_starts_a_process(string bannedToken) =>
        AssertAbsentUnder(
            VerificationDirectory,
            [bannedToken],
            "verification inspects signed machine facts and launches nothing.");

    /// <summary>No verification code terminates a process or automates a coordinate (§23).</summary>
    [Theory]
    [InlineData("Kill")]
    [InlineData("TerminateProcess")]
    [InlineData("SetCursorPos")]
    [InlineData("mouse_event")]
    [InlineData("SendInput")]
    [InlineData("keybd_event")]
    public void Verification_neither_terminates_a_process_nor_automates_a_coordinate(string bannedToken) =>
        AssertAbsentUnder(
            VerificationDirectory,
            [bannedToken],
            $"'{bannedToken}' has no place in environment verification.");

    /// <summary>
    /// No adapter can reach the verifier to vouch for itself (§23).
    /// </summary>
    /// <remarks>
    /// Self-authorisation is the failure mode the whole gate exists to prevent: a production
    /// adapter that could consult the verifier could decide it had passed. Verification is
    /// consulted by the gate, and the gate is consulted about the adapter — never the reverse.
    /// </remarks>
    [Fact]
    public void No_adapter_source_file_names_the_verifier()
    {
        string adapters = Path.Combine(ProjectDirectory("PrintFlow.Infrastructure"), "Adapters");
        List<string> offenders = [];

        foreach (string file in Directory.EnumerateFiles(adapters, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!IsComment(lines[i]) &&
                    (lines[i].Contains(VerificationNamespace, StringComparison.Ordinal) ||
                     lines[i].Contains(nameof(IProductionWorkstationVerifier), StringComparison.Ordinal)))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        offenders.ShouldBeEmpty("an adapter that could consult the verifier could authorise itself.");
    }

    /// <summary>
    /// The foundation gate is exactly what Epic 11100 wrote, and still refuses Production (§20).
    /// </summary>
    [Fact]
    public void The_foundation_environment_gate_is_unchanged_and_still_refuses_production()
    {
        MethodInfo[] methods = typeof(FoundationEnvironmentGate)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        methods.ShouldHaveSingleItem();
        methods[0].Name.ShouldBe(nameof(IEnvironmentGate.Verify));

        // No dependency of any kind: the gate cannot have quietly acquired a verifier.
        typeof(FoundationEnvironmentGate).GetConstructors().ShouldHaveSingleItem();
        typeof(FoundationEnvironmentGate).GetConstructors()[0].GetParameters().ShouldBeEmpty();
        typeof(FoundationEnvironmentGate)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldBeEmpty("the foundation gate holds no state and consults nothing.");
    }

    /// <summary>Part A adds no view model, and none may touch the file system (§21, §23).</summary>
    [Fact]
    public void Shell_view_models_reference_neither_verification_nor_System_IO()
    {
        string viewModels = Path.Combine(ProjectDirectory("PrintFlow.App"), "ViewModels");
        List<string> offenders = [];

        foreach (string file in Directory.EnumerateFiles(viewModels, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("System.IO", StringComparison.Ordinal) ||
                    lines[i].Contains("Verification", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        offenders.ShouldBeEmpty("Part A adds no operator surface and view models do no file I/O.");
    }

    private static readonly string[] AllProjects =
        ["PrintFlow.Domain", "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App"];

    private static string VerificationDirectory =>
        Path.Combine(ProjectDirectory("PrintFlow.Infrastructure"), "Verification");

    private static void AssertAbsent(string project, string[] tokens, string because) =>
        AssertAbsentUnder(ProjectDirectory(project), tokens, because);

    private static void AssertAbsentUnder(string directory, string[] tokens, string because)
    {
        List<string> offenders = [];
        foreach ((string file, string[] lines) in SourceUnder(directory))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsComment(lines[i]))
                {
                    continue;
                }

                foreach (string token in tokens)
                {
                    if (lines[i].Contains(token, StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(because);
    }

    private static bool IsComment(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal) ||
               trimmed.StartsWith("///", StringComparison.Ordinal) ||
               trimmed.StartsWith("*", StringComparison.Ordinal);
    }

    private static IEnumerable<(string File, string[] Lines)> SourceOf(string project) =>
        SourceUnder(ProjectDirectory(project));

    private static IEnumerable<(string File, string[] Lines)> SourceUnder(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (file, File.ReadAllLines(file));
        }
    }

    private static string ProjectDirectory(string project)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException("Could not locate the repository root.")
            : Path.Combine(current.FullName, "src", project);
    }
}
