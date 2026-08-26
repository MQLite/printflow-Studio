using System.IO;
using System.Reflection;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// Where Stop and Take Over are allowed to live, and what they are structurally unable to do
/// (Epic 11300 Part D2A §3, §8, §36.19, §38 and Part D2B policy).
/// </summary>
/// <remarks>
/// The rules here are about <b>absence</b>, which is the only kind of rule a codebase forgets.
/// D2A adds an operator control that stops external work; the risks it introduces are a
/// process-termination shortcut, a generic "click this element" API arriving alongside the
/// signed cancel, and stop <i>policy</i> drifting down into Infrastructure where the phase rules
/// would be restated per adapter.
/// </remarks>
public sealed class StopAndTakeOverBoundaryTests
{
    // -----------------------------------------------------------------------------
    // §3, §36.19 — no process termination anywhere
    // -----------------------------------------------------------------------------

    /// <summary>
    /// No force-termination API exists anywhere in the product source (§3).
    /// </summary>
    /// <remarks>
    /// The same scan D1 introduced, kept and re-stated for D2A rather than relaxed. Stop is
    /// deliberately not Kill: the whole slice is built on asking Meitu to abandon its work
    /// through a control it exposes, and an available <c>Kill</c> would make every one of those
    /// careful refusals optional. D2B made that decision: force termination remains absent by
    /// production policy.
    /// <para>
    /// Comment lines are exempt because this rule is itself documented in prose that has to name
    /// what it forbids — including in this file.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("TerminateProcess")]
    [InlineData("Process.Kill")]
    [InlineData(".Kill(")]
    [InlineData("taskkill")]
    [InlineData("ExitProcess")]
    public void Production_contains_no_force_process_termination_token(string bannedToken)
    {
        List<string> offenders = [];

        foreach (string project in new[]
                 {
                     "PrintFlow.Domain", "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App",
                 })
        {
            foreach ((string file, string[] lines) in SourceOf(project))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (!trimmed.StartsWith("//", StringComparison.Ordinal) &&
                        !trimmed.StartsWith("///", StringComparison.Ordinal) &&
                        !trimmed.StartsWith("*", StringComparison.Ordinal) &&
                        lines[i].Contains(bannedToken, StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{project}/{Path.GetFileName(file)}:{i + 1}: {trimmed}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "Stop is not Kill. D2B policy permits no way to force-terminate Meitu.");
    }

    /// <summary>
    /// The stop vocabulary contains no member an adapter could read as permission to terminate
    /// (§3).
    /// </summary>
    /// <remarks>
    /// The token scan above catches an implementation; this catches an <i>intention</i>. A third
    /// <see cref="AutomationStopMode"/> called <c>ForceTerminate</c> would compile, would pass
    /// every scan, and would violate D2B's permanent two-mode policy.
    /// </remarks>
    [Fact]
    public void The_stop_vocabulary_names_no_termination_mode()
    {
        string[] modes = [.. Enum.GetNames<AutomationStopMode>()];

        modes.ShouldBe(["StopOperation", "TakeOver"], ignoreOrder: true);

        foreach (string forbidden in new[] { "Kill", "Terminate", "Force", "Abort" })
        {
            modes.ShouldNotContain(
                mode => mode.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"'{forbidden}' is not something PrintFlow may express as a stop mode.");
        }
    }

    // -----------------------------------------------------------------------------
    // §8 — no generic cancel surface
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The cancel seam takes an operation and a file name, and nothing an arbitrary element
    /// could be named through (§8).
    /// </summary>
    /// <remarks>
    /// §8 forbids <c>FindElementByName("取消") → Invoke</c>, and the way to make that
    /// structural rather than a discipline is to leave no signature in which a caller could
    /// express it. The parameter list is asserted exactly for that reason: a <c>controlName</c>
    /// or <c>automationId</c> parameter appearing here would be the generic API arriving.
    /// </remarks>
    [Fact]
    public void The_cancel_seam_exposes_no_arbitrary_element_surface()
    {
        MethodInfo method = typeof(IMeituUiDriver).GetMethod(
            nameof(IMeituUiDriver.CancelRunningOperationAsync))!;

        method.GetParameters().Select(parameter => parameter.Name).ShouldBe(
            ["target", "operation", "expectedWorkingCopyFileName", "cancellationToken"]);

        method.GetParameters().ShouldNotContain(parameter =>
            parameter.Name!.Contains("control", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Contains("element", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Contains("coordinate", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Contains("shortcut", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Contains("key", StringComparison.OrdinalIgnoreCase) ||
            parameter.ParameterType == typeof(KnownShortcut));
    }

    /// <summary>
    /// The cancel route sends no keystroke and reaches no coordinate (§8).
    /// </summary>
    /// <remarks>
    /// Explicitly including Escape. "Just press Escape" is the tempting shortcut when a cancel
    /// cannot be resolved, and it is blind input: it goes to whatever has focus, which during a
    /// stop is exactly what PrintFlow has stopped being sure about.
    /// </remarks>
    [Fact]
    public void The_cancel_route_sends_no_keystroke_and_names_no_coordinate()
    {
        string source = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Meitu", "GuardedMeituUiDriver.cs"));

        int start = source.IndexOf(
            "public async Task<OperationResult<MeituCancelOutcome>> CancelRunningOperationAsync",
            StringComparison.Ordinal);
        int end = source.IndexOf(
            "private static Dictionary<string, string> CancelContext(", start, StringComparison.Ordinal);

        start.ShouldBeGreaterThan(-1);
        end.ShouldBeGreaterThan(start);

        string route = source[start..end];
        route.ShouldNotContain(nameof(IMeituUiDriver.SendVerifiedShortcutAsync), Case.Sensitive);
        route.ShouldNotContain("SendShortcut", Case.Sensitive);
        route.ShouldNotContain("KnownShortcut", Case.Sensitive);
        route.ShouldNotContain("Escape", Case.Insensitive);
        route.ShouldNotContain("SendKeys", Case.Insensitive);
        route.ShouldNotContain("mouse", Case.Insensitive);
        route.ShouldNotContain("coordinate", Case.Insensitive);
        route.ShouldNotContain("SetValue", Case.Sensitive);
    }

    /// <summary>
    /// The cancel rule refuses a substring match on the control's name (§8).
    /// </summary>
    /// <remarks>
    /// A behavioural assertion about the pure rule rather than a text scan, because the failure
    /// this prevents is subtle: a name that merely <i>contains</i> the signed one would match a
    /// button labelled "取消全部", which is not the control the evidence describes.
    /// </remarks>
    [Fact]
    public void The_cancel_rule_requires_an_exact_name_rather_than_a_substring()
    {
        MeituBusyCancelSignature signature = Fixtures.MeituFakes.BusyCancel();

        UiElementIdentity nearMiss = new(
            ControlTypeName: signature.Control.ControlTypeName,
            AutomationId: Fixtures.MeituFakes.BusyCancelAutomationId,
            Name: signature.Control.Name + "全部",
            ClassName: signature.Control.ClassName,
            ProcessId: 4242,
            SupportedPatterns: [UiPatternKind.Invoke],
            Bounds: new UiBounds(0, 0, 10, 10),
            IsEnabled: true,
            IsOffscreen: false);

        MeituBusyCancelRule.SelectCancelControl(
            signature,
            expectedProcessId: 4242,
            [new MeituBusyCancelCandidate(
                nearMiss, ["LoadingMaskWidget", "SpecialMaskWidget", "MaskDialog"])])
            .IsFailure.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // §38 — where the policy lives
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Stop policy is declared in the workflow layer, not in Infrastructure (§38).
    /// </summary>
    /// <remarks>
    /// What stopping <i>means</i> at each phase is a product decision. Declaring the types in
    /// Infrastructure would mean each adapter carried its own copy, and the first consequence of
    /// two copies is two answers to "may this be cancelled".
    /// </remarks>
    [Theory]
    [InlineData(typeof(AutomationStopMode))]
    [InlineData(typeof(ExternalOperationPhase))]
    [InlineData(typeof(RetainedExternalState))]
    [InlineData(typeof(AutomationStopPolicy))]
    [InlineData(typeof(AutomationStopResolution))]
    [InlineData(typeof(IAutomationStopSignal))]
    [InlineData(typeof(AutomationStopAudit))]
    public void The_stop_policy_types_belong_to_the_workflow_layer(Type policyType)
    {
        policyType.Assembly.ShouldBe(
            typeof(WorkflowEngine).Assembly,
            $"{policyType.Name} is stop policy and belongs above Infrastructure.");
    }

    /// <summary>
    /// Infrastructure asks the policy rather than restating it (§38).
    /// </summary>
    /// <remarks>
    /// A source scan for the phase names in a conditional would catch the drift this guards
    /// against — an adapter growing its own "if the phase is Busy then…" ladder. The driver may
    /// <i>report</i> phases, which is what <c>ReportPhase</c> does; what it must not do is decide
    /// what they permit.
    /// </remarks>
    [Fact]
    public void Infrastructure_resolves_stop_permission_through_the_policy()
    {
        string driver = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Meitu", "GuardedMeituUiDriver.cs"));

        driver.ShouldContain(
            nameof(AutomationStopPolicy) + "." + nameof(AutomationStopPolicy.Resolve),
            Case.Sensitive,
            "the driver must ask the policy what a stop permits rather than deciding for itself");
    }

    // -----------------------------------------------------------------------------
    // §38 — no Revision, no adapter reference, no file work in the wrong places
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Nothing in Infrastructure creates a Revision (§38).
    /// </summary>
    /// <remarks>
    /// Restated for D2A because the stop paths are new places where an implementation might be
    /// tempted to record something, and §12 is explicit that a cancelled operation produces no
    /// Revision of any kind.
    /// </remarks>
    [Fact]
    public void Infrastructure_creates_no_revision()
    {
        List<string> offenders = [];

        foreach ((string file, string[] lines) in SourceOf("PrintFlow.Infrastructure"))
        {
            // The SQLite mappers legitimately read and write persisted Revision rows; they are
            // the persistence of a decision the workflow layer already made, not the making of
            // one.
            if (file.Contains($"{Path.DirectorySeparatorChar}Sqlite{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (!trimmed.StartsWith("//", StringComparison.Ordinal) &&
                    !trimmed.StartsWith("///", StringComparison.Ordinal) &&
                    lines[i].Contains("Revision.Create", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {trimmed}");
                }
            }
        }

        offenders.ShouldBeEmpty("only the workflow layer decides that a Revision exists.");
    }

    /// <summary>
    /// The system command that closes a stopped attempt cannot be constructed by the UI (§12).
    /// </summary>
    /// <remarks>
    /// The same protection every other system command has. A screen able to synthesise "this
    /// attempt was cancelled" could close an attempt without a run having stopped — and the
    /// attempt would then be Cancelled while the adapter was still driving Meitu.
    /// </remarks>
    [Fact]
    public void The_cancelled_system_command_exposes_no_public_constructor()
    {
        typeof(WorkflowCommand.System.AttemptCancelled)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(c => c.GetParameters().Length > 0)
            .Where(c => c.GetParameters().Length != 1 ||
                        c.GetParameters()[0].ParameterType != typeof(WorkflowCommand.System.AttemptCancelled))
            .ShouldBeEmpty("the UI must not be able to declare that an attempt was cancelled.");
    }

    /// <summary>
    /// The runtime registry is not the automation lock and does not pretend to be (§32).
    /// </summary>
    /// <remarks>
    /// Worth asserting because the two are easy to conflate: both are about "who is driving
    /// Meitu". The registry is in-process and disappears with the process; the lock is persisted
    /// and is what startup recovery reasons about. A registry that released the lock would leave
    /// a crashed run holding nothing, and recovery with nothing to find.
    /// </remarks>
    [Fact]
    public void The_runtime_registry_does_not_touch_the_persisted_automation_lock()
    {
        string source = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Workflow"), "Services", "AutomationRuntime.cs"));

        source.ShouldNotContain(nameof(AutomationLockChange), Case.Sensitive);
        source.ShouldNotContain("GetAutomationLockAsync", Case.Sensitive);
        source.ShouldNotContain(nameof(ISessionRepository), Case.Sensitive);
    }

    // -----------------------------------------------------------------------------

    private static IEnumerable<(string File, string[] Lines)> SourceOf(string project)
    {
        foreach (string file in Directory.EnumerateFiles(
                     ProjectDirectory(project), "*.cs", SearchOption.AllDirectories))
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
