using System.IO;
using System.Reflection;
using PrintFlow.App.Composition;
using PrintFlow.App.ViewModels;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// The boundaries the operator diagnostics surface must not cross (Epic 11500 Part C §11).
/// </summary>
/// <remarks>
/// Part B's boundary tests kept the shell away from the workstation verifier while there was no
/// operator surface at all. Part C adds one, which is exactly when the rules stop being
/// theoretical: a readiness screen is the natural place for somebody to add "check again as
/// Production", "continue anyway", or a quiet second copy of which checks matter.
/// <para>
/// Every source scan here skips comment lines, following the convention the workstation boundary
/// tests already use. Documentation that promises the absence of a behaviour must not read as
/// the behaviour (Part B §18).
/// </para>
/// </remarks>
public sealed class EnvironmentDiagnosticsBoundaryTests
{
    private const string VerificationNamespace = "PrintFlow.Infrastructure.Verification";

    // ---------------------------------------------------------------- §11.1

    /// <summary>
    /// The readiness screen consumes the diagnostics seam and nothing that could authorise
    /// (§11.1).
    /// </summary>
    /// <remarks>
    /// Asserted on the constructor rather than on a doc comment: a view model that could be
    /// handed an <see cref="IEnvironmentGate"/> could call <c>Verify</c>, and a screen that
    /// calls <c>Verify</c> is a screen that has asked for permission on somebody's behalf.
    /// </remarks>
    [Fact]
    public void The_readiness_screen_holds_diagnostics_and_no_authorisation_seam()
    {
        ConstructorInfo constructor =
            typeof(EnvironmentReadinessViewModel).GetConstructors().ShouldHaveSingleItem();

        constructor.GetParameters().Select(p => p.ParameterType)
            .ShouldContain(typeof(IEnvironmentDiagnostics));

        typeof(EnvironmentReadinessViewModel)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(field => field.FieldType)
            .ShouldNotContain(typeof(IEnvironmentGate));

        typeof(EnvironmentReadinessViewModel)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(field => field.FieldType)
            .ShouldNotContain(typeof(IProductionWorkstationVerifier));
    }

    /// <summary>
    /// No view model or screen names the workstation verifier (§11.1).
    /// </summary>
    /// <remarks>
    /// Part B's version of this covered the whole shell while the composition root was the only
    /// exemption. The exemption is unchanged; what has changed is that there is now a screen
    /// about verification, and the rule it must obey is that it reads the App-safe report rather
    /// than reaching past it to the thing that produced it.
    /// </remarks>
    [Theory]
    [InlineData("ViewModels")]
    [InlineData("Views")]
    [InlineData("Navigation")]
    public void No_shell_screen_names_the_workstation_verifier(string area)
    {
        string[] tokens =
        [
            VerificationNamespace,
            nameof(IProductionWorkstationVerifier),
            nameof(ProductionWorkstationVerifier),
            nameof(VerifiedEnvironmentGate),
            nameof(WorkstationVerificationResult),
        ];

        AssertAbsentUnder(
            Path.Combine(ProjectDirectory("PrintFlow.App"), area),
            tokens,
            "the shell reads the App-safe readiness report, never the verifier behind it.");
    }

    /// <summary>
    /// The shell never asks the gate for permission (§11.1, §2).
    /// </summary>
    /// <remarks>
    /// <c>IEnvironmentGate</c> is a Workflow port and the shell can see Workflow, so nothing but
    /// this test stops a screen resolving it. The direction the whole epic rests on is that the
    /// workflow asks the gate about an adapter; a screen that asked the gate about itself would
    /// be inventing a second authorisation path with a person on the end of it.
    /// </remarks>
    [Theory]
    [InlineData("ViewModels")]
    [InlineData("Views")]
    [InlineData("Navigation")]
    public void No_shell_screen_names_the_environment_gate(string area) =>
        AssertAbsentUnder(
            Path.Combine(ProjectDirectory("PrintFlow.App"), area),
            [nameof(IEnvironmentGate)],
            "a screen that could ask the gate could act on the answer.");

    // ---------------------------------------------------------------- §11.2, §11.3, §11.4

    /// <summary>
    /// No adapter names the diagnostics seam, the gate or the verifier (§11.2–§11.4).
    /// </summary>
    /// <remarks>
    /// Part B asserted the gate and the verifier. The diagnostics seam is added here for the
    /// reason it was worth separating in the first place: it is read-only, which makes it look
    /// harmless to inject, and an adapter that read a readiness report could branch on it — which
    /// is self-authorisation wearing a support screen's clothes.
    /// </remarks>
    [Theory]
    [InlineData(nameof(IEnvironmentDiagnostics))]
    [InlineData(nameof(IEnvironmentGate))]
    [InlineData(nameof(IProductionWorkstationVerifier))]
    [InlineData(VerificationNamespace)]
    public void No_adapter_names_verification_authorisation_or_diagnostics(string token) =>
        AssertAbsentUnder(
            Path.Combine(ProjectDirectory("PrintFlow.Infrastructure"), "Adapters"),
            [token],
            $"'{token}' has no place inside an adapter.");

    // ---------------------------------------------------------------- §11.5

    /// <summary>Exactly one product gate ships, and it is the verified one (§11.5).</summary>
    [Fact]
    public void Exactly_one_product_environment_gate_exists()
    {
        typeof(VerifiedEnvironmentGate).Assembly.GetTypes()
            .Where(type => typeof(IEnvironmentGate).IsAssignableFrom(type)
                           && type is { IsAbstract: false, IsInterface: false })
            .ShouldHaveSingleItem()
            .ShouldBe(typeof(VerifiedEnvironmentGate));
    }

    /// <summary>
    /// The one gate is also the one diagnostics implementation (§11.5, §2).
    /// </summary>
    /// <remarks>
    /// A separate diagnostics implementation would be a second reader of the workstation, and
    /// the day it disagreed with the gate is the day the screen says Ready and the workflow
    /// refuses. Same type, therefore same answer.
    /// </remarks>
    [Fact]
    public void The_only_diagnostics_implementation_is_the_gate_itself()
    {
        typeof(VerifiedEnvironmentGate).Assembly.GetTypes()
            .Where(type => typeof(IEnvironmentDiagnostics).IsAssignableFrom(type)
                           && type is { IsAbstract: false, IsInterface: false })
            .ShouldHaveSingleItem()
            .ShouldBe(typeof(VerifiedEnvironmentGate));
    }

    // ---------------------------------------------------------------- §11.6

    /// <summary>
    /// The diagnostics contract exposes one read and no mutation (§11.6, §2).
    /// </summary>
    /// <remarks>
    /// Enumerated from the interface rather than asserted by name, so a method added later has
    /// to be a reader to survive: the contract carries exactly one member, it returns a report,
    /// and it takes nothing a caller could use to steer it.
    /// </remarks>
    [Fact]
    public void The_diagnostics_seam_offers_exactly_one_read_and_no_mutation()
    {
        MethodInfo read = typeof(IEnvironmentDiagnostics).GetMethods().ShouldHaveSingleItem();

        read.Name.ShouldBe(nameof(IEnvironmentDiagnostics.Read));
        read.ReturnType.ShouldBe(typeof(EnvironmentReadinessReport));
        read.GetParameters().ShouldBeEmpty("a reader that took an argument could be steered.");
    }

    /// <summary>
    /// Neither the report nor a row offers a way to act on what it says (§11.6, §2).
    /// </summary>
    /// <remarks>
    /// Property and method names, both records, both directions. The report is the object a
    /// screen holds while an operator is looking at a refusal, which makes it the most tempting
    /// place in the system to put an "Enable anyway".
    /// </remarks>
    [Theory]
    [InlineData("Enable")]
    [InlineData("Override")]
    [InlineData("Ignore")]
    [InlineData("Continue")]
    [InlineData("Bypass")]
    [InlineData("Skip")]
    [InlineData("Force")]
    [InlineData("Authorise")]
    [InlineData("Authorize")]
    public void Neither_the_readiness_report_nor_a_check_report_offers_a_way_past_it(string verb)
    {
        foreach (Type type in new[]
                 {
                     typeof(EnvironmentReadinessReport),
                     typeof(EnvironmentCheckReport),
                     typeof(IEnvironmentDiagnostics),
                 })
        {
            type.GetMembers()
                .Where(member => member.Name.Contains(verb, StringComparison.OrdinalIgnoreCase))
                .ShouldBeEmpty($"'{verb}' on {type.Name} would make the report a control.");
        }
    }

    /// <summary>
    /// The readiness screen exposes no command that could change anything (§11.6, §13).
    /// </summary>
    /// <remarks>
    /// Two commands, by name: look again, and go back. Everything else on the screen is a string
    /// or a list. A "repair", "install" or "enable" command would be the moment the diagnostics
    /// surface stopped observing.
    /// </remarks>
    [Fact]
    public void The_readiness_screen_offers_only_refresh_and_back()
    {
        string[] commands =
        [
            .. typeof(EnvironmentReadinessViewModel)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.Name.EndsWith("Command", StringComparison.Ordinal))
                .Select(property => property.Name),
        ];

        commands.ShouldBe(["RefreshCommand", "BackToHomeCommand"], ignoreOrder: true);
    }

    // ---------------------------------------------------------------- §11.7

    /// <summary>
    /// No production enable, override or bypass vocabulary ships anywhere in <c>src</c> (§11.7).
    /// </summary>
    /// <remarks>
    /// Part B's list, extended with the four names Part C's own brief calls out. Each is a
    /// plausible thing to add on the day the workstation fails and a job is due, and each would
    /// permanently convert the gate from an authority into a suggestion.
    /// </remarks>
    [Theory]
    [InlineData("VerifyAndEnable")]
    [InlineData("ContinueAnyway")]
    [InlineData("RetryAsProduction")]
    [InlineData("EnableProduction")]
    [InlineData("IgnoreEnvironment")]
    [InlineData("OverrideReadiness")]
    [InlineData("SkipReadiness")]
    public void No_product_source_offers_production_activation_vocabulary(string bannedToken)
    {
        foreach (string project in AllProjects)
        {
            AssertAbsentUnder(
                ProjectDirectory(project),
                [bannedToken],
                $"'{bannedToken}' would turn a support screen into an activation switch.");
        }
    }

    /// <summary>
    /// The shell has no way to change which adapters are composed (§11.7, §9).
    /// </summary>
    /// <remarks>
    /// <c>Adapters:Mode</c> is read once, in the composition root, from the committed
    /// configuration. A screen that could write it — or an <see cref="AdapterExecutionMode"/>
    /// a view model could hand to something — would be an operator switch for production mode,
    /// which is the one thing this slice must not ship.
    /// </remarks>
    [Theory]
    [InlineData("ViewModels")]
    [InlineData("Views")]
    [InlineData("Navigation")]
    public void No_shell_screen_can_choose_an_adapter_mode(string area) =>
        AssertAbsentUnder(
            Path.Combine(ProjectDirectory("PrintFlow.App"), area),
            [nameof(AdapterExecutionMode), "Adapters:Mode", "Adapters.Mode"],
            "which adapters run is decided by configuration at composition, not on a screen.");

    // ---------------------------------------------------------------- §11.9

    /// <summary>
    /// The adapter registration is still the one place a mode is decided, and it still fails
    /// closed on an unrecognised one (§11.9; Epic 11500 Part D §2).
    /// </summary>
    /// <remarks>
    /// Part C asserted that the Production case was a <c>throw</c>, because in Part C it was.
    /// Part D opened it deliberately, and what survives that change is the rule the assertion was
    /// really about: there is exactly one method that chooses adapters, it lives in the
    /// composition root, and a mode it does not recognise stops the application rather than
    /// defaulting to something. What replaced the closed branch — which adapters Production
    /// composes, and that it has no fallback to a fake — is asserted by
    /// <c>ProductionActivationBoundaryTests</c>.
    /// </remarks>
    [Fact]
    public void The_adapter_registration_lives_in_the_composition_root_and_fails_closed()
    {
        string source = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.App"), "Composition", "ServiceRegistration.cs"));

        source.ShouldContain("case \"Production\":");
        source.ShouldContain("throw new NotSupportedException(");

        // And the type really is the one the application composes through.
        typeof(ServiceRegistration).GetMethod(
                "RegisterAdapters", BindingFlags.NonPublic | BindingFlags.Static)
            .ShouldNotBeNull("the one adapter-mode decision lives in the composition root.");
    }

    // ---------------------------------------------------------------- §11.10

    /// <summary>
    /// The shell states no workstation policy of its own (§11.10).
    /// </summary>
    /// <remarks>
    /// The duplication this guards against is not a copied file — it is a screen that decides,
    /// say, that a display mismatch "is only a warning really", or that readiness means "no
    /// failures I recognise". Those decisions are made of the verifier's own vocabulary, so the
    /// absence of that vocabulary in the shell is what makes the duplication impossible.
    /// </remarks>
    [Theory]
    [InlineData(nameof(WorkstationVerificationCheck))]
    [InlineData(nameof(WorkstationCheckOutcome))]
    [InlineData(nameof(WorkstationCheckKind))]
    [InlineData("WorkstationRequirements")]
    [InlineData("WorkstationCheckResult")]
    public void No_shell_screen_restates_the_verification_vocabulary(string token) =>
        AssertAbsentUnder(
            Path.Combine(ProjectDirectory("PrintFlow.App"), "ViewModels"),
            [token],
            "readiness policy lives in one place, and the shell is not it.");

    /// <summary>
    /// The screen derives blocking-versus-advisory from the report's own flag (§11.10, §5).
    /// </summary>
    /// <remarks>
    /// A source scan, because this is the one rule a screen can plausibly re-implement without
    /// naming a verification type: hard-coding which check keys "really" matter would compile,
    /// pass every behavioural test on today's preset, and silently disagree with the gate the
    /// day a check is added.
    /// </remarks>
    [Fact]
    public void The_screen_names_no_individual_check_when_classifying()
    {
        string[] checkNames =
            [.. Enum.GetNames<WorkstationVerificationCheck>()];

        AssertAbsentUnder(
            Path.Combine(ProjectDirectory("PrintFlow.App"), "ViewModels"),
            checkNames,
            "the screen classifies by the report's IsBlocking flag, never by a list of checks.");
    }

    private static readonly string[] AllProjects =
        ["PrintFlow.Domain", "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App"];

    private static void AssertAbsentUnder(string directory, string[] tokens, string because)
    {
        List<string> offenders = [];

        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
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

    /// <summary>
    /// The established convention: a promise that a behaviour is absent is not the behaviour.
    /// </summary>
    private static bool IsComment(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal) ||
               trimmed.StartsWith("///", StringComparison.Ordinal) ||
               trimmed.StartsWith("*", StringComparison.Ordinal);
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
