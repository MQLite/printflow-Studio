using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// Where desktop automation is allowed to live, asserted rather than hoped for
/// (Epic 11300 Part A §26).
/// </summary>
/// <remarks>
/// The rules below are blunt on purpose. A window handle, a P/Invoke, or a UI Automation type
/// appearing outside <c>PrintFlow.Infrastructure</c> would not be a style problem — it would be
/// the first step towards a view model or a workflow step being able to click something, which
/// is the arrangement this epic is built to prevent.
///
/// The banned-token scan is the widest net here: it covers the specific APIs that make blind
/// global input possible, wherever in the solution someone might reach for one.
/// </remarks>
public sealed class AutomationBoundaryTests
{
    private static Assembly Domain => typeof(FailureCode).Assembly;

    private static Assembly WorkflowLayer => typeof(WorkflowEngine).Assembly;

    private static Assembly Infrastructure => typeof(GuardedMeituUiDriver).Assembly;

    private static Assembly Shell => typeof(global::PrintFlow.App.App).Assembly;

    /// <summary>The automation types that must never be nameable outside Infrastructure.</summary>
    private static readonly string[] InfrastructureOnlyTypeNames =
    [
        nameof(WindowHandle),
        nameof(ExternalWindowRef),
        nameof(ExternalProcessRef),
        nameof(ForegroundIdentity),
        nameof(IExternalAppWindowLocator),
        nameof(IScopedInputSink),
        nameof(IUiElementProvider),
        nameof(IAutomationEvidenceSink),
        nameof(IMeituUiDriver),
        nameof(IMeituAutomationFoundation),
        nameof(MeituTarget),
        nameof(MeituStartingState),
        nameof(KnownMeituElement),
        nameof(KnownShortcut),
    ];

    // -----------------------------------------------------------------------------
    // Assembly-level dependencies
    // -----------------------------------------------------------------------------

    [Fact]
    public void Domain_and_Workflow_reference_no_UI_Automation_assembly()
    {
        string[] forbidden = ["UIAutomationClient", "UIAutomationTypes", "UIAutomationProvider"];

        foreach (Assembly assembly in new[] { Domain, WorkflowLayer })
        {
            assembly.GetReferencedAssemblies().Select(a => a.Name!).Intersect(forbidden)
                .ShouldBeEmpty($"{assembly.GetName().Name} must not see UI Automation.");
        }
    }

    /// <summary>
    /// The automation types are declared in Infrastructure and nowhere else.
    /// </summary>
    [Fact]
    public void The_automation_types_are_declared_only_in_Infrastructure()
    {
        foreach (string typeName in InfrastructureOnlyTypeNames)
        {
            Infrastructure.GetTypes().ShouldContain(
                t => t.Name == typeName, $"{typeName} should be declared in Infrastructure.");

            foreach (Assembly other in new[] { Domain, WorkflowLayer, Shell })
            {
                other.GetTypes().Where(t => t.Name == typeName).Select(t => t.FullName!)
                    .ShouldBeEmpty($"{typeName} must not be declared in {other.GetName().Name}.");
            }
        }
    }

    // -----------------------------------------------------------------------------
    // Source-level containment
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Domain, Workflow and the shell name no automation type at all.
    /// </summary>
    /// <remarks>
    /// Source text rather than reflection, because the interesting failure is a
    /// <c>using PrintFlow.Infrastructure.Automation;</c> that someone adds while wiring
    /// something up — which reflection over the compiled assembly would only catch once the
    /// type actually reached a signature.
    /// </remarks>
    [Theory]
    [InlineData("PrintFlow.Domain", null)]
    [InlineData("PrintFlow.Workflow", null)]
    [InlineData("PrintFlow.App", "ViewModels")]
    [InlineData("PrintFlow.App", "Views")]
    [InlineData("PrintFlow.App", "Navigation")]
    public void No_automation_type_is_named_outside_Infrastructure(string project, string? subdirectory)
    {
        List<string> offenders = [];

        foreach ((string file, string[] lines) in SourceOf(project, subdirectory))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (string typeName in InfrastructureOnlyTypeNames)
                {
                    // Whole-identifier matching: FailureCode.MeituTargetLost legitimately lives
                    // in the domain and merely starts with the same letters as MeituTarget.
                    if (Regex.IsMatch(
                            lines[i], $@"\b{Regex.Escape(typeName)}\b", RegexOptions.None, TimeSpan.FromSeconds(5)))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "desktop automation types belong to PrintFlow.Infrastructure; nothing above it may name one.");
    }

    /// <summary>
    /// The APIs that make blind global input possible appear nowhere in the solution.
    /// </summary>
    /// <remarks>
    /// <c>SendKeys</c>, <c>keybd_event</c>, <c>mouse_event</c> and <c>SetCursorPos</c> all
    /// deliver to whatever currently has focus or to a screen coordinate, with no way to state
    /// which window was intended. There is therefore no correct use of them here — the
    /// verified-target seam exists so that none is needed (§3, §4).
    /// </remarks>
    [Theory]
    [InlineData("SendKeys")]
    [InlineData("keybd_event")]
    [InlineData("mouse_event")]
    [InlineData("SetCursorPos")]
    [InlineData("mouse_input")]
    public void No_blind_global_input_API_is_referenced_anywhere_in_src(string bannedToken)
    {
        List<string> offenders = [];

        foreach (string project in new[] { "PrintFlow.Domain", "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App" })
        {
            foreach ((string file, string[] lines) in SourceOf(project, subdirectory: null))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    // The banned list is quoted in NativeMethods' own documentation, explaining
                    // why each is absent. A comment naming one is the opposite of a violation.
                    if (lines[i].Contains(bannedToken, StringComparison.Ordinal) &&
                        !lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal) &&
                        !lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal))
                    {
                        offenders.Add($"{project}/{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty($"'{bannedToken}' sends input with no verifiable target.");
    }

    /// <summary>P/Invoke declarations live in Infrastructure only.</summary>
    [Theory]
    [InlineData("PrintFlow.Domain")]
    [InlineData("PrintFlow.Workflow")]
    [InlineData("PrintFlow.App")]
    public void No_P_Invoke_is_declared_outside_Infrastructure(string project)
    {
        List<string> offenders = [];

        foreach ((string file, string[] lines) in SourceOf(project, subdirectory: null))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("[DllImport", StringComparison.Ordinal) ||
                    trimmed.StartsWith("[LibraryImport", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {trimmed}");
                }
            }
        }

        offenders.ShouldBeEmpty($"{project} must call no OS entry point directly.");
    }

    /// <summary>
    /// Every P/Invoke in Infrastructure is declared in the one file that lists them.
    /// </summary>
    /// <remarks>
    /// Keeping them together is what makes "which OS calls can this application make?"
    /// answerable by reading one file rather than trusting a grep.
    /// </remarks>
    [Fact]
    public void Every_P_Invoke_in_Infrastructure_lives_in_NativeMethods()
    {
        List<string> offenders = [];

        foreach ((string file, string[] lines) in SourceOf("PrintFlow.Infrastructure", subdirectory: null))
        {
            if (Path.GetFileName(file) == "NativeMethods.cs")
            {
                continue;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("[DllImport", StringComparison.Ordinal) ||
                    trimmed.StartsWith("[LibraryImport", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
        }

        offenders.ShouldBeEmpty("every OS entry point belongs in Automation\\NativeMethods.cs.");
    }

    /// <summary>
    /// <c>AllowUnsafeBlocks</c> is on for the P/Invoke generator, not for hand-written pointers.
    /// </summary>
    [Fact]
    public void No_hand_written_unsafe_code_exists()
    {
        List<string> offenders = [];

        foreach (string project in new[] { "PrintFlow.Domain", "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App" })
        {
            foreach ((string file, string[] lines) in SourceOf(project, subdirectory: null))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    {
                        // A comment explaining why there is no unsafe code is not unsafe code.
                        continue;
                    }

                    if (Regex.IsMatch(trimmed, @"\bunsafe\b", RegexOptions.None, TimeSpan.FromSeconds(5)))
                    {
                        offenders.Add($"{project}/{Path.GetFileName(file)}:{i + 1}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // The workflow seam
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Workflow's whole view of Meitu is <see cref="IMeituProcessor"/>.
    /// </summary>
    /// <remarks>
    /// Checked by signature rather than by naming convention: what matters is that no method
    /// the workflow layer can call takes or returns anything that could identify a window, a
    /// process or a control.
    /// </remarks>
    [Fact]
    public void The_workflow_layer_sees_only_the_Meitu_port()
    {
        string[] meituTypesInWorkflow = [.. WorkflowLayer.GetTypes()
            .Where(t => t.Name.Contains("Meitu", StringComparison.Ordinal))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)];

        meituTypesInWorkflow.ShouldBe(["IMeituProcessor", "MeituOperation", "MeituRequest"]);
    }

    /// <summary>
    /// The production adapter is not registered by the composition root.
    /// </summary>
    /// <remarks>
    /// Belt and braces alongside the environment gate: the gate stops a production adapter from
    /// running, and this stops one from being wired into a session at all while the production
    /// path is incomplete (§6).
    /// </remarks>
    [Fact]
    public void The_composition_root_registers_no_production_adapter()
    {
        string source = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.App"), "Composition", "ServiceRegistration.cs"));

        source.ShouldNotContain(
            "AddSingleton<IMeituProcessor, ProductionMeituProcessor>", Case.Sensitive);
    }

    /// <summary>The production adapter is what it says it is.</summary>
    [Fact]
    public void The_production_adapter_declares_Production_mode_and_a_distinct_identity()
    {
        Type type = typeof(ProductionMeituProcessor);

        typeof(IMeituProcessor).IsAssignableFrom(type).ShouldBeTrue();
        typeof(IMeituAutomationFoundation).IsAssignableFrom(type).ShouldBeTrue();

        // The foundation seam is Infrastructure-only, so a workflow caller holding an
        // IMeituProcessor cannot reach EnsureReadyAsync or OpenWorkingCopyAsync by casting to a
        // type it can name.
        typeof(IMeituAutomationFoundation).Assembly.ShouldBe(Infrastructure);
    }

    // -----------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------

    private static IEnumerable<(string File, string[] Lines)> SourceOf(string project, string? subdirectory)
    {
        string directory = ProjectDirectory(project);
        if (subdirectory is not null)
        {
            directory = Path.Combine(directory, subdirectory);
        }

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

    private static string ProjectDirectory(string projectName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root (PrintFlowStudio.sln) above " + AppContext.BaseDirectory);
        }

        return Path.Combine(current.FullName, "src", projectName);
    }
}
