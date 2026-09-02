using System.IO;
using System.Reflection;
using PrintFlow.App.Composition;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// The rules the open Production branch has to keep obeying (Epic 11500 Part D §7).
/// </summary>
/// <remarks>
/// Until this slice, the composition root's Production case was a <c>throw</c>, and a throw needs
/// no rules. Now that it composes real adapters, the shape of that branch is load-bearing: a
/// fallback added inside it on a bad afternoon, a second production adapter written to "just get
/// this job out", or a mode read from anywhere but the committed configuration would each undo
/// the control this whole epic exists to place.
/// <para>
/// Source scans skip comment lines, following the convention the earlier boundary tests use: a
/// comment promising the absence of a behaviour must not read as the behaviour (Part B §18).
/// </para>
/// </remarks>
public sealed class ProductionActivationBoundaryTests
{
    private const string CompositionRoot = "ServiceRegistration.cs";

    // ---------------------------------------------------------------- §7.1

    /// <summary>
    /// One mode parameter decides both processors, in one switch (§7.1).
    /// </summary>
    /// <remarks>
    /// Read off the method rather than off the file: <c>RegisterAdapters</c> takes the mode once
    /// and there is no second selector for it to disagree with. A per-adapter mode would have to
    /// appear as another parameter or another read of configuration, and both are visible here.
    /// </remarks>
    [Fact]
    public void One_method_takes_one_mode_and_registers_both_ports()
    {
        MethodInfo register = typeof(ServiceRegistration)
            .GetMethod("RegisterAdapters", BindingFlags.NonPublic | BindingFlags.Static)
            .ShouldNotBeNull();

        register.GetParameters()
            .Count(p => p.Name is not null && p.Name.Contains("mode", StringComparison.OrdinalIgnoreCase))
            .ShouldBe(1, "a second mode parameter would be a per-adapter selector.");

        string body = ProductionBranch();
        body.ShouldContain(nameof(IMeituProcessor));
        body.ShouldContain(nameof(IPhotoshopOutputProcessor));
    }

    /// <summary>
    /// No hybrid or per-adapter mode vocabulary exists anywhere in the product (§7.1, §2).
    /// </summary>
    /// <remarks>
    /// Each of these is the name somebody would reach for on the day one of the two applications
    /// is misbehaving and the other is fine. The answer to that day is a workstation repair and a
    /// restart, not a configuration that runs half of Production.
    /// <para>
    /// <c>SessionService.AdapterModeFor</c> is deliberately not on this list, and is not what it
    /// forbids: it reads the mode the composed adapter <i>declares</i> so the gate can be asked
    /// about the thing that would actually run. That is the accepted mechanism — one
    /// configuration, two adapters that agree, one question. What is banned is a second
    /// <b>selector</b>, which decides what gets composed.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Hybrid")]
    [InlineData("MeituMode")]
    [InlineData("PhotoshopMode")]
    [InlineData("PerAdapterMode")]
    [InlineData("MeituAdaptersMode")]
    [InlineData("PhotoshopAdaptersMode")]
    public void No_product_source_offers_a_per_adapter_mode(string bannedToken)
    {
        foreach (string project in AllProjects)
        {
            AssertAbsentUnder(
                ProjectDirectory(project),
                [bannedToken],
                $"'{bannedToken}' would let one configuration run two different worlds.");
        }
    }

    // ---------------------------------------------------------------- §7.2, §7.3

    /// <summary>
    /// The Production branch composes through the accepted factories (§7.2).
    /// </summary>
    /// <remarks>
    /// Named explicitly because the alternative is so easy to write: the composition root could
    /// hand-assemble a locator, a driver and a processor here and it would work, and it would
    /// also be a second object graph nobody accepted, drifting from the one every workstation
    /// smoke has proved since Epic 11300.
    /// </remarks>
    [Fact]
    public void The_production_branch_uses_the_accepted_composition_factories()
    {
        string branch = ProductionBranch();

        branch.ShouldContain($"{nameof(MeituAutomationComposition)}.{nameof(MeituAutomationComposition.CreateProductionProcessor)}");
        branch.ShouldContain($"{nameof(PhotoshopAutomationComposition)}.{nameof(PhotoshopAutomationComposition.CreateProductionProcessor)}");
    }

    /// <summary>
    /// Exactly one production implementation of each port ships (§7.3).
    /// </summary>
    /// <remarks>
    /// Enumerated from the assembly rather than asserted by name, so a duplicate written next
    /// week fails this test rather than passing it. Two adapters declaring
    /// <see cref="AdapterExecutionMode.Production"/> would mean the gate's single answer about
    /// the workstation no longer identifies what is about to run.
    /// </remarks>
    [Fact]
    public void There_is_exactly_one_production_adapter_per_port()
    {
        ImplementationsOf<IMeituProcessor>()
            .ShouldBe([typeof(FakeMeituProcessor), typeof(ProductionMeituProcessor)], ignoreOrder: true);

        ImplementationsOf<IPhotoshopOutputProcessor>()
            .ShouldBe(
                [typeof(FakePhotoshopOutputProcessor), typeof(ProductionPhotoshopOutputProcessor)],
                ignoreOrder: true);
    }

    // ---------------------------------------------------------------- §7.4

    /// <summary>
    /// The Production branch contains no fallback to a fake (§7.4, §2).
    /// </summary>
    /// <remarks>
    /// The failure this forbids is the one that would never be noticed: a
    /// <c>try</c>/<c>catch</c> around Production initialisation that quietly registers the
    /// deterministic double, producing an installation that believes it is printing production
    /// TIFFs and is not. The branch may not name a fake at all, and it may not catch.
    /// </remarks>
    [Theory]
    [InlineData("Fake")]
    [InlineData("catch")]
    [InlineData("??")]
    public void The_production_branch_has_no_fallback(string bannedToken) =>
        ProductionBranch().ShouldNotContain(bannedToken,
            Case.Sensitive,
            $"'{bannedToken}' inside the Production branch would be a silent substitution.");

    /// <summary>
    /// The Fake branch has no path to a production adapter either (§7.4).
    /// </summary>
    /// <remarks>
    /// The mirror image, and worth asserting for a different reason: a Fake installation that
    /// could reach a real adapter would be a development machine driving a real Photoshop.
    /// </remarks>
    [Fact]
    public void The_fake_branch_names_no_production_adapter()
    {
        string branch = Branch("case \"Fake\":", "case \"Production\":");

        branch.ShouldNotContain(nameof(ProductionMeituProcessor));
        branch.ShouldNotContain(nameof(ProductionPhotoshopOutputProcessor));
        branch.ShouldNotContain(nameof(MeituAutomationComposition));
        branch.ShouldNotContain(nameof(PhotoshopAutomationComposition));
    }

    // ---------------------------------------------------------------- §7.5, §7.11

    /// <summary>
    /// Configuration is the only activation mechanism (§7.11).
    /// </summary>
    /// <remarks>
    /// The adapter-mode literals exist in exactly one file. Anywhere else — a view model, a
    /// service, a helper — would be a second place that could decide what runs, and the first
    /// step towards a runtime switch.
    /// </remarks>
    [Fact]
    public void Only_the_composition_root_names_an_adapter_mode_literal()
    {
        foreach (string project in AllProjects)
        {
            AssertAbsentUnder(
                ProjectDirectory(project),
                ["\"Production\"", "\"Fake\""],
                "the mode is read once, in the composition root, from the committed configuration.",
                except: CompositionRoot);
        }
    }

    /// <summary>
    /// Nothing in the product can change a composed mode at runtime (§7.9, §7.11).
    /// </summary>
    /// <remarks>
    /// Names rather than shapes, because the shape a mode switch would take is an ordinary
    /// method. What makes it findable is that it has to be called something, and every plausible
    /// name is here.
    /// </remarks>
    [Theory]
    [InlineData("SetAdapterMode")]
    [InlineData("SwitchToProduction")]
    [InlineData("UseProductionAdapters")]
    [InlineData("ActivateProduction")]
    [InlineData("ChangeAdapterMode")]
    [InlineData("ReconfigureAdapters")]
    public void No_product_source_offers_a_runtime_mode_switch(string bannedToken)
    {
        foreach (string project in AllProjects)
        {
            AssertAbsentUnder(
                ProjectDirectory(project),
                [bannedToken],
                $"'{bannedToken}' would make production mode something a running process decides.");
        }
    }

    /// <summary>
    /// The shell cannot see an adapter, in either mode (§7.9).
    /// </summary>
    /// <remarks>
    /// Part C forbade the shell naming <c>AdapterExecutionMode</c> or the configuration key.
    /// The concrete adapter types are added now for the reason they only now matter: they exist
    /// in a live graph, so a screen that could name one could resolve one, and a screen that
    /// could resolve one could call it without ever passing the gate.
    /// </remarks>
    [Theory]
    [InlineData("ViewModels")]
    [InlineData("Views")]
    [InlineData("Navigation")]
    public void No_shell_screen_names_a_production_adapter(string area) =>
        AssertAbsentUnder(
            Path.Combine(ProjectDirectory("PrintFlow.App"), area),
            [
                nameof(ProductionMeituProcessor),
                nameof(ProductionPhotoshopOutputProcessor),
                nameof(MeituAutomationComposition),
                nameof(PhotoshopAutomationComposition),
                nameof(IMeituProcessor),
                nameof(IPhotoshopOutputProcessor),
            ],
            "a screen that could reach an adapter could run one without the gate.");

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private static readonly string[] AllProjects =
        ["PrintFlow.Domain", "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App"];

    /// <summary>The source text of the composition root's Production case, and nothing else.</summary>
    private static string ProductionBranch() => Branch("case \"Production\":", "default:");

    private static string Branch(string from, string to)
    {
        string source = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.App"), "Composition", CompositionRoot));

        int start = source.IndexOf(from, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, $"the composition root must still contain {from}");

        int end = source.IndexOf(to, start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(-1, $"{from} must still be followed by {to}");

        return string.Join(
            '\n',
            source[start..end].Split('\n').Where(line => !IsComment(line)));
    }

    private static Type[] ImplementationsOf<TPort>() =>
        [.. typeof(ProductionMeituProcessor).Assembly.GetTypes()
            .Where(type => typeof(TPort).IsAssignableFrom(type)
                           && type is { IsAbstract: false, IsInterface: false })];

    private static void AssertAbsentUnder(
        string directory, string[] tokens, string because, string? except = null)
    {
        List<string> offenders = [];

        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                (except is not null && string.Equals(Path.GetFileName(file), except, StringComparison.Ordinal)))
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

        current.ShouldNotBeNull("the repository root must be locatable from the test output.");
        return Path.Combine(current.FullName, "src", project);
    }
}
