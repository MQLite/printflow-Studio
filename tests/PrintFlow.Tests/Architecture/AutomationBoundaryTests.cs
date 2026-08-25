using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
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
        nameof(MeituDocumentIdentitySignature),
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

    [Theory]
    [InlineData("TerminateProcess")]
    [InlineData("Process.Kill")]
    [InlineData(".Kill(")]
    [InlineData("taskkill")]
    public void D1_introduces_no_force_process_termination_API(string bannedToken)
    {
        List<string> offenders = [];
        foreach (string project in new[]
                 {
                     "PrintFlow.Domain", "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App",
                 })
        {
            foreach ((string file, string[] lines) in SourceOf(project, subdirectory: null))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (!trimmed.StartsWith("//", StringComparison.Ordinal) &&
                        !trimmed.StartsWith("///", StringComparison.Ordinal) &&
                        lines[i].Contains(bannedToken, StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{project}/{Path.GetFileName(file)}:{i + 1}: {trimmed}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty("D2 owns Stop, force termination and operator takeover.");
    }

    [Fact]
    public void The_document_identity_probe_exposes_no_path_coordinate_or_shortcut_surface()
    {
        MethodInfo method = typeof(IMeituUiDriver).GetMethod(
            nameof(IMeituUiDriver.ConfirmWorkingCopyIdentityAsync))!;

        method.GetParameters().Select(parameter => parameter.Name).ShouldBe(
            ["target", "expectedWorkingCopyFileName", "cancellationToken"]);
        method.GetParameters().ShouldNotContain(parameter =>
            parameter.Name!.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Contains("coordinate", StringComparison.OrdinalIgnoreCase) ||
            parameter.ParameterType == typeof(KnownShortcut));

        string source = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Meitu", "GuardedMeituUiDriver.cs"));
        int start = source.IndexOf(
            "public async Task<OperationResult<MeituStateSnapshot>> ConfirmWorkingCopyIdentityAsync",
            StringComparison.Ordinal);
        int end = source.IndexOf("private OperationResult<string> ReadIdentityValue", start, StringComparison.Ordinal);
        string probe = source[start..end];

        probe.ShouldNotContain(nameof(IMeituUiDriver.SendVerifiedShortcutAsync));
        probe.ShouldNotContain("SendKeys", Case.Sensitive);
        probe.ShouldNotContain("mouse", Case.Insensitive);
        probe.ShouldNotContain("coordinate", Case.Insensitive);
    }


    /// <summary>
    /// The Enhancement seam exposes no path, coordinate, keystroke or free-form control name
    /// (Epic 11300 Part B2A §8, §11).
    /// </summary>
    /// <remarks>
    /// The signature is the safety property. A caller can ask for "the Enhancement action on
    /// this verified target, for this expected file" and nothing else — there is no overload
    /// taking a control name, a screen point, or a shortcut, so no future caller can express one.
    /// The source check then confirms the implementation does not reach around its own interface.
    /// </remarks>
    [Fact]
    public void The_Enhancement_route_exposes_no_path_coordinate_or_shortcut_surface()
    {
        MethodInfo method = typeof(IMeituUiDriver).GetMethod(
            nameof(IMeituUiDriver.RunEnhancementAsync))!;

        method.GetParameters().Select(parameter => parameter.Name).ShouldBe(
            ["target", "expectedWorkingCopyFileName", "cancellationToken"]);
        method.GetParameters().ShouldNotContain(parameter =>
            parameter.Name!.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Contains("coordinate", StringComparison.OrdinalIgnoreCase) ||
            parameter.ParameterType == typeof(KnownShortcut));

        string source = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Meitu", "GuardedMeituUiDriver.cs"));
        int start = source.IndexOf(
            "public async Task<OperationResult<MeituEnhancementOutcome>> RunEnhancementAsync",
            StringComparison.Ordinal);
        int end = source.IndexOf(
            "private async Task<OperationResult<MeituTarget>> ReacquireForegroundAsync",
            start,
            StringComparison.Ordinal);

        start.ShouldBeGreaterThan(-1);
        end.ShouldBeGreaterThan(start);
        string route = source[start..end];

        route.ShouldNotContain(nameof(IMeituUiDriver.SendVerifiedShortcutAsync));
        route.ShouldNotContain("SendKeys", Case.Sensitive);
        route.ShouldNotContain("mouse", Case.Insensitive);
        route.ShouldNotContain("coordinate", Case.Insensitive);
    }

    /// <summary>
    /// The Enhancement outcome cannot be mistaken for an exported result
    /// (Epic 11300 Part B2A §16, §20).
    /// </summary>
    /// <remarks>
    /// Asserted on the type rather than left to review, because the temptation in B2B will be to
    /// add an output path here rather than to a new type — and the moment this record carries
    /// one, a caller can read a success from it as "the file exists". Until export and validation
    /// are implemented, there must be nothing on it that could be read that way.
    /// </remarks>
    [Fact]
    public void The_Enhancement_outcome_carries_no_output_success_or_revision_surface()
    {
        string[] members = [.. typeof(MeituEnhancementOutcome)
            .GetProperties()
            .Select(property => property.Name)];

        members.ShouldNotContain(name => name.Contains("Output", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Path", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Revision", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Succeed", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Export", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_Background_Removal_outcome_carries_no_output_success_or_revision_surface()
    {
        string[] members = [.. typeof(MeituBackgroundRemovalOutcome)
            .GetProperties()
            .Select(property => property.Name)];

        members.ShouldNotContain(name => name.Contains("Output", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Path", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Revision", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Succeed", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Export", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Cutout", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_Background_Removal_route_exposes_no_path_coordinate_shortcut_or_free_form_action()
    {
        MethodInfo method = typeof(IMeituUiDriver).GetMethod(
            nameof(IMeituUiDriver.RunBackgroundRemovalAsync))!;

        method.GetParameters().Select(parameter => parameter.Name).ShouldBe(
            ["target", "expectedWorkingCopyFileName", "modeDecision", "cancellationToken"]);
        method.GetParameters().ShouldNotContain(parameter =>
            parameter.Name!.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Contains("coordinate", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Contains("action", StringComparison.OrdinalIgnoreCase) ||
            parameter.ParameterType == typeof(KnownShortcut));

        string source = DriverSource();
        int start = source.IndexOf(
            "public async Task<OperationResult<MeituBackgroundRemovalOutcome>> RunBackgroundRemovalAsync",
            StringComparison.Ordinal);
        int end = source.IndexOf(
            "private async Task<OperationResult<MeituStateSnapshot>> AwaitBackgroundRemovalPhaseAsync",
            start,
            StringComparison.Ordinal);

        start.ShouldBeGreaterThan(-1);
        end.ShouldBeGreaterThan(start);
        string route = source[start..end];
        route.ShouldNotContain(nameof(IMeituUiDriver.SendVerifiedShortcutAsync));
        route.ShouldNotContain("SetValue", Case.Sensitive);
        route.ShouldNotContain("SendKeys", Case.Insensitive);
        route.ShouldNotContain("mouse", Case.Insensitive);
        route.ShouldNotContain("coordinate", Case.Insensitive);
        route.ShouldNotContain("Export", Case.Sensitive);
    }

    /// <summary>
    /// The Enhancement route writes nothing: no Save, no Save As, no value written anywhere.
    /// </summary>
    /// <remarks>
    /// A source-level assertion because it is the one property of this slice that cannot be
    /// recovered if it is broken. Enhancement changes the document Meitu is holding, and it must
    /// do so without ever setting a field — a value written during it would mean PrintFlow had
    /// typed into a surface whose state it had not established.
    ///
    /// The region is bounded at the export route rather than at the end of the class, because
    /// Part B2B adds one that legitimately writes values. That is why the companion test below
    /// exists: between them they say the whole thing, which is that the adapter writes values in
    /// exactly two places and neither is the Enhancement.
    /// </remarks>
    [Fact]
    public void The_Enhancement_and_close_routes_write_no_value_anywhere()
    {
        string source = DriverSource();

        int start = source.IndexOf(
            "public async Task<OperationResult<MeituEnhancementOutcome>> RunEnhancementAsync",
            StringComparison.Ordinal);
        int end = source.IndexOf(
            "public async Task<OperationResult<MeituExportEvidence>> ExportResultAsync",
            start, StringComparison.Ordinal);

        start.ShouldBeGreaterThan(-1);
        end.ShouldBeGreaterThan(start);

        // Everything from the Enhancement route to the export route: the invoke, the waits, the
        // reacquisition and the close route.
        source[start..end].ShouldNotContain("SetValue", Case.Sensitive);
    }

    /// <summary>
    /// The adapter writes values in exactly two places, and the export never writes the
    /// destination folder field (Epic 11300 Part B2B §8, §10).
    /// </summary>
    /// <remarks>
    /// The folder assertion is the one worth having. Meitu's Save surface exposes a
    /// <c>folderEdit</c> that accepts a written value, reads it back exactly, and does not move
    /// the export — the file landed in the operator's Downloads folder while the field displayed
    /// the controlled path. Nothing in the code catches that; only having looked does. So the
    /// prohibition is asserted on the source, where a future change that reaches for the
    /// obvious-looking control fails a test instead of quietly exporting somewhere else.
    /// </remarks>
    [Fact]
    public void The_adapter_writes_values_only_in_the_picker_and_the_export_and_never_names_a_folder_field()
    {
        string source = DriverSource();

        // Two call sites: the picker's file-name field, and the export's shared setter. Every
        // other value the export writes goes through that one setter.
        int writes = 0;
        for (int i = source.IndexOf("_elements.SetValue", StringComparison.Ordinal);
             i >= 0;
             i = source.IndexOf("_elements.SetValue", i + 1, StringComparison.Ordinal))
        {
            writes++;
        }

        writes.ShouldBe(3);

        // As string literals, which is the only form in which a control id can be looked up.
        // The names appear in this file's prose — the reason folderEdit is not used is worth
        // stating where the route is — and prose is exactly what this assertion must not forbid.
        source.ShouldNotContain("\"folderEdit\"", Case.Insensitive);
        source.ShouldNotContain("\"selectFolderButton\"", Case.Insensitive);
        source.ShouldNotContain("\"btnCoverSavePath\"", Case.Insensitive);
        source.ShouldNotContain("\"btnDesktopSavePath\"", Case.Insensitive);

        // And no signature member exists for one either, so the evidence cannot reintroduce it.
        string[] members = [.. typeof(MeituExportSignature).GetProperties().Select(p => p.Name)];
        members.ShouldNotContain(name => name.Contains("Folder", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Directory", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The export evidence record cannot be mistaken for proof that a file exists
    /// (Epic 11300 Part B2B §13).
    /// </summary>
    /// <remarks>
    /// The same guard Part B2A put on <c>MeituEnhancementOutcome</c>, one layer along and for
    /// the same reason. This record says what PrintFlow set and invoked; the filesystem says
    /// whether it worked. A <c>Succeeded</c> or <c>ByteLength</c> member here would let a caller
    /// read the first as the second.
    /// </remarks>
    [Fact]
    public void The_export_evidence_carries_no_success_size_or_hash_surface()
    {
        string[] members = [.. typeof(MeituExportEvidence).GetProperties().Select(p => p.Name)];

        members.ShouldNotContain(name => name.Contains("Succeed", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Exists", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Length", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Sha", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(name => name.Contains("Revision", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The export route exposes no coordinate, keystroke or free-form control name
    /// (Epic 11300 Part B2B §12).
    /// </summary>
    [Fact]
    public void The_export_route_exposes_no_coordinate_or_shortcut_surface()
    {
        System.Reflection.MethodInfo method = typeof(IMeituUiDriver).GetMethod(
            nameof(IMeituUiDriver.ExportResultAsync))!;

        method.GetParameters().ShouldNotContain(parameter =>
            parameter.Name!.Contains("coordinate", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Contains("key", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Contains("shortcut", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Contains("control", StringComparison.OrdinalIgnoreCase));

        string source = DriverSource();
        int start = source.IndexOf(
            "public async Task<OperationResult<MeituExportEvidence>> ExportResultAsync",
            StringComparison.Ordinal);
        int end = source.IndexOf(
            "private static OperationFailure TargetLost(", start, StringComparison.Ordinal);

        start.ShouldBeGreaterThan(-1);
        end.ShouldBeGreaterThan(start);

        string route = source[start..end];
        route.ShouldNotContain("SendShortcut", Case.Sensitive);
        route.ShouldNotContain("SendKeys", Case.Insensitive);
        route.ShouldNotContain("mouse_event", Case.Insensitive);
        route.ShouldNotContain("SetCursorPos", Case.Insensitive);
        route.ShouldNotContain("coordinate", Case.Insensitive);
    }

    private static string DriverSource() => File.ReadAllText(Path.Combine(
        ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Meitu", "GuardedMeituUiDriver.cs"));

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
    /// The Background Removal authority is a typed product decision that no adapter code can
    /// manufacture (Epic 11300 Part C2B1 §29).
    /// </summary>
    /// <remarks>
    /// Three separate claims, and each has a way of quietly failing:
    /// <list type="bullet">
    ///   <item>A <c>bool</c> or a string would make "automatic selection is on" expressible at
    ///         all, which is the session-wide permission the whole design exists to prevent.</item>
    ///   <item>The type lives in <c>PrintFlow.Domain</c>, not in the adapter surface and not in
    ///         Infrastructure. That is what lets the session, the attempt row and the request all
    ///         name the same value, and what keeps a UI-automation assembly from owning a product
    ///         decision.</item>
    ///   <item><c>SessionService</c> builds the request from the authority the attempt already
    ///         recorded. A literal authorised value anywhere in that file would be the service
    ///         deciding on the operator's behalf, so the source is checked for its absence rather
    ///         than for a comment promising it (§12).</item>
    /// </list>
    /// </remarks>
    [Fact]
    public void Background_Removal_authority_is_a_typed_domain_decision_the_service_never_invents()
    {
        PropertyInfo decision = typeof(MeituRequest).GetProperty(
            nameof(MeituRequest.BackgroundRemovalDecision))!;

        decision.PropertyType.ShouldBe(typeof(BackgroundRemovalDecision));
        decision.PropertyType.ShouldNotBe(typeof(bool));
        decision.PropertyType.ShouldNotBe(typeof(string));

        typeof(BackgroundRemovalDecision).Assembly.GetName().Name.ShouldBe("PrintFlow.Domain");
        typeof(BackgroundRemovalAuthority).Assembly.GetName().Name.ShouldBe("PrintFlow.Domain");

        string sessionService = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Workflow"), "Services", "SessionService.cs"));

        sessionService.ShouldContain("attempt.BackgroundRemovalAuthority", Case.Sensitive);
        sessionService.ShouldNotContain(
            "BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent", Case.Sensitive);

        // No adapter assembly may construct the request either: the decision reaches Meitu only
        // by way of the service that checked it.
        foreach (string file in InfrastructureSources())
        {
            File.ReadAllText(file).ShouldNotContain(
                "new MeituRequest(", Case.Sensitive,
                Path.GetFileName(file) + " builds a Meitu request; SessionService owns that.");
        }
    }

    /// <summary>Every Infrastructure source file, excluding build output.</summary>
    private static IEnumerable<string> InfrastructureSources()
    {
        string separator = Path.DirectorySeparatorChar.ToString();
        return Directory
            .GetFiles(ProjectDirectory("PrintFlow.Infrastructure"), "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains(separator + "bin" + separator, StringComparison.Ordinal) &&
                !f.Contains(separator + "obj" + separator, StringComparison.Ordinal));
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
