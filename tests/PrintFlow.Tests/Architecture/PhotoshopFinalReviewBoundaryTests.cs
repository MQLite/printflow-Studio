using System.IO;
using System.Reflection;
using PrintFlow.Domain.Outputs;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// Where C2B's new capability stops (Epic 11400 Part C2B §20, §21, §42).
/// </summary>
/// <remarks>
/// C2B is the slice in which PrintFlow first moves a deliverable and first disposes of one. Both
/// are one step from a shell that deletes a file because a button was pressed, and one step from
/// a workspace-wide cleanup that arrived as a side effect of one artefact's lifecycle. Each rule
/// here draws that line where the slice's own reasoning put it.
/// </remarks>
public sealed class PhotoshopFinalReviewBoundaryTests
{
    // -----------------------------------------------------------------------------------
    // §42 — no shell does file work
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The shell moves nothing, recycles nothing and hashes nothing: it issues Approve and Reject
    /// and reads back what the workflow layer decided (§42).
    /// </summary>
    /// <remarks>
    /// The review panel is the natural place for someone to "just move the file here" — the
    /// operator is standing right there and the path is on screen. That the shell cannot even name
    /// the Recycle Bin or a workspace area's folder is what keeps promotion a workflow decision
    /// rather than a screen's.
    /// </remarks>
    [Fact]
    public void The_shell_performs_no_file_movement_hashing_or_recycling()
    {
        foreach (string file in SourceFiles("PrintFlow.App"))
        {
            string name = Path.GetFileName(file);

            // The composition root is exempt, and only it. Naming a type in order to register it
            // is the opposite of using it: ServiceRegistration hands IRecycleBin to the workflow
            // layer precisely so no screen ever holds one.
            if (name == "ServiceRegistration.cs")
            {
                continue;
            }

            string source = File.ReadAllText(file);

            foreach (string banned in new[]
            {
                "IRecycleBin",
                "SendToRecycleBin",
                "File.Move(",
                "File.Copy(",
                "File.Delete(",
                "ReserveOutput(",
                "WriteReservedAsync(",
                "MoveToRejectedAsync(",
                "CleanupWorking(",
                "SHA256",
                "ComputeHash(",
            })
            {
                source.ShouldNotContain(
                    banned, Case.Sensitive, $"{name} performs its own file or hash work: '{banned}'.");
            }
        }
    }

    /// <summary>
    /// No view model touches <c>System.IO</c> at all (§42).
    /// </summary>
    [Fact]
    public void No_view_model_references_System_IO()
    {
        string viewModels = Path.Combine(ProjectDirectory("PrintFlow.App"), "ViewModels");

        foreach (string file in Directory.EnumerateFiles(viewModels, "*.cs", SearchOption.AllDirectories))
        {
            File.ReadAllText(file).ShouldNotContain(
                "System.IO", Case.Sensitive, $"{Path.GetFileName(file)} reaches into the file system.");
        }
    }

    // -----------------------------------------------------------------------------------
    // §42 — the disposal route
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// There is exactly one implementation of the disposal port, it lives in Infrastructure, and
    /// nothing in the solution hard-deletes a file (§14, §42).
    /// </summary>
    /// <remarks>
    /// "No hard-delete fallback" is only a real guarantee if there is no second route to deletion
    /// beside the one that refuses to fall back. <c>Directory.Delete</c> in
    /// <c>FileWorkspace.CleanupWorking</c> is the single exception, and it is exempted by name
    /// rather than by pattern so that adding a second one is a visible edit to this test.
    /// </remarks>
    [Fact]
    public void The_only_deletion_route_is_the_Recycle_Bin_abstraction_in_Infrastructure()
    {
        List<string> implementations = [];
        foreach (Assembly assembly in new[]
        {
            typeof(FoundationEnvironmentGate).Assembly,
            typeof(IRecycleBin).Assembly,
            typeof(PrintOutput).Assembly,
        })
        {
            implementations.AddRange(assembly.GetTypes()
                .Where(type => type is { IsAbstract: false, IsInterface: false } &&
                               typeof(IRecycleBin).IsAssignableFrom(type))
                .Select(type => type.FullName!));
        }

        implementations.ShouldBe(["PrintFlow.Infrastructure.Workspace.RecycleBin"]);

        foreach (string project in new[] { "PrintFlow.Domain", "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App" })
        {
            foreach (string file in SourceFiles(project))
            {
                string name = Path.GetFileName(file);
                string source = File.ReadAllText(file);

                // The deterministic fake adapter is exempt: its ProduceMissingFile scenario exists
                // to make an output vanish so the workflow's own failure handling can be tested,
                // and it never runs against a Revision, an approved file or a rejected one. It is
                // exempted by name so that a second exemption is a visible edit here.
                if (name != "FakeAdapterExecution.cs")
                {
                    source.ShouldNotContain("File.Delete(", Case.Sensitive, $"{name} deletes a file outright.");
                }

                if (name != "FileWorkspace.cs")
                {
                    source.ShouldNotContain(
                        "Directory.Delete(", Case.Sensitive, $"{name} deletes a directory outright.");
                }
            }
        }
    }

    // -----------------------------------------------------------------------------------
    // §42 — promotion and disposal are workflow decisions, not adapter ones
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The final review's file lifecycle lives in <c>SessionService</c> and nowhere else, and it
    /// names no adapter (§36, §42).
    /// </summary>
    /// <remarks>
    /// Both halves matter. If Infrastructure could reach the Recycle Bin or a promotion, an
    /// adapter could decide the fate of its own output; if the promotion branched on which adapter
    /// produced the TIFF, the Fake path and the Photoshop path would be two review contracts
    /// wearing one name, and only one of them would be tested.
    /// </remarks>
    [Fact]
    public void Promotion_and_disposal_live_in_SessionService_and_branch_on_no_adapter()
    {
        List<string> callers = [];
        foreach (string project in new[] { "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App" })
        {
            foreach (string file in SourceFiles(project))
            {
                string name = Path.GetFileName(file);

                // The port that declares it and the one type that implements it are not callers.
                if (name is "IWorkspace.cs" or "RecycleBin.cs")
                {
                    continue;
                }

                if (File.ReadAllText(file).Contains("SendToRecycleBin(", StringComparison.Ordinal))
                {
                    callers.Add(name);
                }
            }
        }

        callers.Order().ShouldBe(["SessionService.cs"]);

        string lifecycle = MethodSource("PerformFinalReviewFileWorkAsync") +
                           MethodSource("PromoteApprovedOutputAsync") +
                           MethodSource("ReserveApprovedDestinationAsync") +
                           MethodSource("RecycleRejectedOutput");

        foreach (string banned in new[] { "AdapterExecutionMode", "AdapterKind", "_photoshop", "_meitu" })
        {
            lifecycle.ShouldNotContain(
                banned, Case.Sensitive, $"The final-review file lifecycle branches on '{banned}'.");
        }
    }

    /// <summary>
    /// Promotion creates no Revision: the moved deliverable is recorded on the
    /// <see cref="PrintOutput"/>, and the producing Revision is immutable (§4, §28, §42).
    /// </summary>
    /// <remarks>
    /// Stated against the database as well as the source, because this is the one place the two
    /// could drift apart in a way nothing else would notice: a Revision row whose path could be
    /// updated would make "the producing record never moves" a convention rather than a fact.
    /// </remarks>
    [Fact]
    public void Promotion_records_the_moved_file_on_the_PrintOutput_and_never_on_the_Revision()
    {
        string lifecycle = MethodSource("PromoteApprovedOutputAsync") +
                           MethodSource("ReserveApprovedDestinationAsync");

        foreach (string banned in new[] { "Revision.Create(", "new Revision(", "revision with {" })
        {
            lifecycle.ShouldNotContain(banned, Case.Sensitive, $"Promotion creates or rewrites a Revision: '{banned}'.");
        }

        string schema = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Sqlite", "Migrations", "0001_initial_schema.sql"));
        schema.ShouldContain("CREATE TRIGGER Revision_Immutable_Update");
        schema.ShouldContain("OR OLD.RelativePath     <> NEW.RelativePath");

        string promotion = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Sqlite", "Migrations",
            "0007_print_output_promotion.sql"));
        promotion.ShouldContain("CREATE TRIGGER PrintOutput_Identity_Immutable");
        promotion.ShouldContain("OLD.Sha256           <> NEW.Sha256");
        promotion.ShouldContain("OLD.ByteLength       <> NEW.ByteLength");
    }

    // -----------------------------------------------------------------------------------
    // §20, §21 — CleanupWorking was not globalised
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// <c>WorkflowEffect.CleanupWorking</c> still has no production interpreter, and
    /// <c>IWorkspace.CleanupWorking</c> still has no production caller (§20, §21).
    /// </summary>
    /// <remarks>
    /// C2B needed file lifecycle handling, which is exactly the moment someone wires up the
    /// dormant cleanup effect because it is there and it sounds related. It is not safe to wire as
    /// it stands: it deletes the whole <c>Working\</c> tree, and since C2A the production TIFF's
    /// Revision — like every Meitu-derived Revision before it — names a file inside that tree, so
    /// executing it would destroy the artefacts immutable records point at. The gap is reported,
    /// not closed by a slice that owns one artefact's lifecycle.
    /// <para>
    /// This test is what makes "deliberately left alone" checkable. It fails the moment the effect
    /// gains an interpreter, which is the moment the audit §21 asks for has to happen.
    /// </para>
    /// </remarks>
    [Fact]
    public void CleanupWorking_still_has_no_production_interpreter_or_caller()
    {
        List<string> callers = [];
        foreach (string project in new[] { "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App" })
        {
            foreach (string file in SourceFiles(project))
            {
                string name = Path.GetFileName(file);

                // The port declares it, the workspace implements it, and the engine emits it as
                // data. None of those three carries it out.
                if (name is "IWorkspace.cs" or "FileWorkspace.cs" or "WorkflowEffect.cs" or "WorkflowEngine.cs")
                {
                    continue;
                }

                if (File.ReadAllText(file).Contains("CleanupWorking", StringComparison.Ordinal))
                {
                    callers.Add(name);
                }
            }
        }

        callers.ShouldBeEmpty(
            "CleanupWorking gained an interpreter. Epic 11400 Part C2B §21 requires the safety audit " +
            "and the full test matrix before that is allowed.");
    }

    // -----------------------------------------------------------------------------------
    // §39 — the gate is untouched
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// C2B changed nothing about the environment gate, and added no way to open Production (§39).
    /// </summary>
    [Fact]
    public void The_environment_gate_still_refuses_every_production_adapter()
    {
        IEnvironmentGate gate = new FoundationEnvironmentGate();

        gate.Verify(AdapterExecutionMode.Fake).IsSuccess.ShouldBeTrue();
        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
        gate.Verify(AdapterExecutionMode.Production).Failure.Code
            .ShouldBe(Domain.Results.FailureCode.EnvironmentNotVerified);

        // And no screen can flip it. The composition root registers the one real gate — which is
        // how the application gets one at all — but nothing an operator can reach names it, so
        // there is no control anywhere that opens Production.
        foreach (string file in Directory.EnumerateFiles(
            ProjectDirectory("PrintFlow.App"), "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "ServiceRegistration.cs")
            {
                continue;
            }

            File.ReadAllText(file).ShouldNotContain(
                "IEnvironmentGate", Case.Sensitive,
                $"{Path.GetFileName(file)} can reach the environment gate.");
        }
    }

    // -----------------------------------------------------------------------------------
    // Source helpers
    // -----------------------------------------------------------------------------------

    /// <summary>The body of one <c>SessionService</c> method, from its signature to the next one.</summary>
    private static string MethodSource(string methodName)
    {
        string source = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Workflow"), "Services", "SessionService.cs"));

        int start = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, $"SessionService has no method named {methodName}.");

        int end = source.IndexOf("\n    private ", start, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    private static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(ProjectDirectory(project), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string ProjectDirectory(string project)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        current.ShouldNotBeNull();
        return Path.Combine(current.FullName, "src", project);
    }
}
