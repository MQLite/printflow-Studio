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
    /// Nothing above Infrastructure may name a verification type (§23; Part B §3).
    /// </summary>
    /// <remarks>
    /// Domain and Workflow stating environment facts would put registry, Win32 and file-system
    /// concepts inside the layers that are supposed to be portable and deterministic.
    /// <para>
    /// The App shell is checked too, and Part B narrows the exemption rather than dropping it:
    /// <c>PrintFlow.App.Composition</c> now names the verifier, because §14 requires the
    /// composition root to build and register it, and the composition root is the one place in
    /// App already permitted to see Infrastructure. Everything else in the shell — every view
    /// model, every screen — still may not, so an operator surface onto verification remains a
    /// deliberate slice through <c>IEnvironmentDiagnostics</c> rather than something that appears
    /// by a view model happening to reference a verifier (§21).
    /// </para>
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

        AssertAbsentUnder(
            ProjectDirectory(project),
            tokens,
            "verification types belong to PrintFlow.Infrastructure.",
            excludedDirectory: project == "PrintFlow.App" ? "Composition" : null);
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
    [InlineData("Process.Start(")]
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
    /// Epic 11100's foundation gate is gone, superseded rather than left beside its replacement
    /// (Part B §5).
    /// </summary>
    /// <remarks>
    /// Part A asserted the foundation gate was <i>unchanged</i>, because nothing had yet verified
    /// a workstation and "Production is always denied" was the whole of the contract. Part B
    /// replaces that with a gate that denies on evidence, and the risk the assertion now has to
    /// cover is the opposite one: two <see cref="IEnvironmentGate"/> implementations shipping side
    /// by side, one of which authorises nothing and the other of which nobody is quite sure is
    /// registered.
    /// </remarks>
    [Fact]
    public void The_temporary_foundation_gate_no_longer_ships()
    {
        typeof(VerifiedEnvironmentGate).Assembly.GetTypes()
            .Where(t => typeof(IEnvironmentGate).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .ShouldHaveSingleItem()
            .ShouldBe(typeof(VerifiedEnvironmentGate),
                "exactly one production gate exists, and it is the verified one (§5).");

        File.Exists(Path.Combine(ProjectDirectory("PrintFlow.Infrastructure"), "Gate", "FoundationEnvironmentGate.cs"))
            .ShouldBeFalse("the superseded foundation gate is removed, not left dormant.");
    }

    /// <summary>
    /// The verified gate consults the verifier and nothing else about the machine (Part B §3).
    /// </summary>
    /// <remarks>
    /// The dependency direction the whole slice rests on, asserted rather than intended: the gate
    /// holds a verifier, and it holds no fact reader, no artefact reader and no configuration it
    /// could use to reach the machine around the verifier's back.
    /// </remarks>
    [Fact]
    public void The_verified_gate_holds_a_verifier_and_no_other_machine_seam()
    {
        ConstructorInfo constructor = typeof(VerifiedEnvironmentGate).GetConstructors().ShouldHaveSingleItem();
        constructor.GetParameters().ShouldHaveSingleItem()
            .ParameterType.ShouldBe(typeof(IProductionWorkstationVerifier));

        typeof(VerifiedEnvironmentGate)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(f => f.FieldType)
            .ShouldBe([typeof(IProductionWorkstationVerifier)]);
    }

    /// <summary>
    /// Only the gate implementation consumes the verifier (Part B §3, §29).
    /// </summary>
    /// <remarks>
    /// The companion to <see cref="No_adapter_source_file_names_the_verifier"/>, widened to the
    /// whole of Infrastructure: an adapter cannot self-authorise, and neither can anything else
    /// that might later be tempted to ask the workstation directly instead of asking the gate.
    /// The composition root names the verifier to construct it, which is registration rather than
    /// consumption and lives in App.
    /// </remarks>
    [Fact]
    public void Only_the_gate_implementation_consumes_the_verifier()
    {
        string infrastructure = ProjectDirectory("PrintFlow.Infrastructure");
        List<string> offenders = [];

        foreach ((string file, string[] lines) in SourceUnder(infrastructure))
        {
            if (Path.GetFileName(file) is "VerifiedEnvironmentGate.cs" or "ProductionWorkstationVerifier.cs")
            {
                continue;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                if (!IsComment(lines[i]) &&
                    lines[i].Contains(nameof(IProductionWorkstationVerifier), StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        offenders.ShouldBeEmpty("the gate is the only consumer of workstation verification.");
    }

    /// <summary>
    /// No adapter takes a dependency on environment permission either (Part B §29).
    /// </summary>
    /// <remarks>
    /// The companion rule to "no adapter names the verifier". An adapter that held an
    /// <see cref="IEnvironmentGate"/> could ask it and act on the answer, which sounds harmless
    /// and is not: authorisation would then be checked in two places, and the day they disagree
    /// is the day a production adapter runs on an unverified workstation because the copy it
    /// consulted said yes. The gate is asked <i>about</i> the adapter, by the workflow, before
    /// the adapter exists in the call stack. Doc comments may name it; code may not.
    /// </remarks>
    [Fact]
    public void No_adapter_holds_an_environment_gate()
    {
        string adapters = Path.Combine(ProjectDirectory("PrintFlow.Infrastructure"), "Adapters");
        List<string> offenders = [];

        foreach ((string file, string[] lines) in SourceUnder(adapters))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (!IsComment(lines[i]) &&
                    (lines[i].Contains(nameof(IEnvironmentGate), StringComparison.Ordinal) ||
                     lines[i].Contains(nameof(IEnvironmentDiagnostics), StringComparison.Ordinal)))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        offenders.ShouldBeEmpty("an adapter that held the gate could authorise the step it is running.");
    }

    /// <summary>
    /// No product bypass of workstation verification exists anywhere in <c>src</c> (Part B §24).
    /// </summary>
    /// <remarks>
    /// Structural, and deliberately by name. Each of these is a plausible thing to add in a hurry
    /// on the day the workstation fails verification and a job is due, and each would permanently
    /// convert the gate from an authority into a suggestion. A controlled test may substitute an
    /// explicitly test-only gate; nothing that ships may offer one.
    /// </remarks>
    [Theory]
    [InlineData("SkipEnvironmentCheck")]
    [InlineData("IgnoreWorkstationVerification")]
    [InlineData("ForceProduction")]
    [InlineData("AllowUnsafeProduction")]
    [InlineData("BypassEnvironmentGate")]
    [InlineData("SkipVerification")]
    [InlineData("OverrideEnvironment")]
    public void No_product_source_offers_a_verification_bypass(string bannedToken)
    {
        foreach (string project in AllProjects)
        {
            AssertAbsent(project, [bannedToken], $"'{bannedToken}' would make the gate optional.");
        }
    }

    /// <summary>
    /// No view model names verification or touches the file system (§21, §23; Part C §11.1).
    /// </summary>
    /// <remarks>
    /// Part A asserted this while there was no operator surface at all. Part C adds one — the
    /// Production Readiness screen — and the rule is unchanged and now load-bearing: the screen
    /// that exists to explain verification reads the App-safe
    /// <c>IEnvironmentDiagnostics</c> report, and names no verification type to do it. The scan
    /// covers comments too, deliberately: this one is about vocabulary rather than about calls,
    /// and a view model discussing <c>WorkstationVerificationResult</c> in prose is a view model
    /// whose author was thinking about reaching for it.
    /// </remarks>
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

        offenders.ShouldBeEmpty(
            "the readiness screen reads the App-safe report; no view model names a verification " +
            "type or does file I/O.");
    }

    private static readonly string[] AllProjects =
        ["PrintFlow.Domain", "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App"];

    private static string VerificationDirectory =>
        Path.Combine(ProjectDirectory("PrintFlow.Infrastructure"), "Verification");

    private static void AssertAbsent(string project, string[] tokens, string because) =>
        AssertAbsentUnder(ProjectDirectory(project), tokens, because);

    private static void AssertAbsentUnder(
        string directory, string[] tokens, string because, string? excludedDirectory = null)
    {
        List<string> offenders = [];
        foreach ((string file, string[] lines) in SourceUnder(directory))
        {
            if (excludedDirectory is not null &&
                file.Contains(
                    $"{Path.DirectorySeparatorChar}{excludedDirectory}{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

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
