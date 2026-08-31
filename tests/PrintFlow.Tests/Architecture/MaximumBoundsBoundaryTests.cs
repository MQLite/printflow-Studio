using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// The limiting edge is decided in exactly one place, and the production Photoshop path cannot
/// reach the legacy independent-pixel pair (Epic 11400 Part B1A.2A §6, §17, §23).
/// </summary>
/// <remarks>
/// The specific way this slice could go wrong is not a wrong number; it is a <b>second</b>
/// answer. <c>FitWithinBounds</c> chooses which single edge Photoshop may be given, and any other
/// code that worked that out for itself — a screen previewing a size, an adapter deriving a
/// target, a service tidying a plan — would be a rule free to drift from the one the engine
/// enforces. The one that decides what Photoshop is actually told would be the wrong one to be
/// wrong.
/// <para>
/// The second failure mode is subtler and is what §17 exists for.
/// <c>PrintDimensions.PixelWidth</c> and <c>PixelHeight</c> are two independent millimetre
/// conversions that still sit on the request for display and naming. Sending both to Photoshop
/// would be the non-proportional stretch the accepted contract prohibits, and it would look
/// entirely reasonable at the call site.
/// </para>
/// <para>
/// Source text as well as reflection, because both failures are lines someone adds while wiring
/// something up rather than types that reach a signature.
/// </para>
/// </remarks>
public sealed class MaximumBoundsBoundaryTests
{
    // -------------------------------------------------------------------------------------
    // §6, §23: one limiting-edge authority, and it is pure Domain code
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The calculator and the plan live in the Domain and nowhere else (§23).
    /// </summary>
    [Fact]
    public void The_fit_calculation_and_the_plan_are_declared_only_in_the_domain()
    {
        typeof(FitWithinBounds).Assembly.GetName().Name.ShouldBe("PrintFlow.Domain");
        typeof(PrintPreparationPlan).Assembly.GetName().Name.ShouldBe("PrintFlow.Domain");
        typeof(PrintDimensionSemantics).Assembly.GetName().Name.ShouldBe("PrintFlow.Domain");
        typeof(LimitingEdge).Assembly.GetName().Name.ShouldBe("PrintFlow.Domain");
        typeof(PhotoshopResizeMode).Assembly.GetName().Name.ShouldBe("PrintFlow.Domain");
    }

    /// <summary>
    /// Nothing outside the Domain calls the fit calculation (§6, §23).
    /// </summary>
    /// <remarks>
    /// Workflow reaches it through <c>PrintPreparationPlan.For</c>, which is the only creator of a
    /// plan; Infrastructure and the shell reach it not at all. A screen or an adapter calling the
    /// calculator directly would not be wrong arithmetic — it would be a second plan, produced
    /// outside the audited path and bound to nothing.
    /// <para>
    /// The banned text is the <b>invocation</b> — the static class followed by a member access —
    /// rather than the bare name. <c>FitWithinBoundsPreparation</c> is the union case naming which
    /// contract a run was made under, and Infrastructure necessarily matches on it to persist and
    /// to describe a run; that is reading a decision, not making one. Banning the name outright
    /// would forbid the very type the union exists to be switched over
    /// (Epic 11400 Part B1A.2D §12).
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("PrintFlow.Workflow")]
    [InlineData("PrintFlow.Infrastructure")]
    [InlineData("PrintFlow.App")]
    public void No_project_outside_the_domain_calls_the_fit_calculation(string project)
    {
        Offenders(project, subdirectory: null, @"FitWithinBounds\.").ShouldBeEmpty(
            "a plan is created only through PrintPreparationPlan.For, which is the calculator's " +
            "single caller.");
    }

    /// <summary>
    /// Nothing outside the Domain calls the target-edge calculation
    /// (Epic 11400 Part B1A.2D §17, §35).
    /// </summary>
    /// <remarks>
    /// The flexible-size half of the same rule, and it matters more, not less: a target edge can
    /// enlarge, so a screen or an adapter that computed one would be producing a projection nobody
    /// authorised. Workflow reaches it through <c>TargetEdgePrintPreparationPlan.For</c> and
    /// nothing else does.
    /// </remarks>
    [Theory]
    [InlineData("PrintFlow.Workflow")]
    [InlineData("PrintFlow.Infrastructure")]
    [InlineData("PrintFlow.App")]
    public void No_project_outside_the_domain_calls_the_target_edge_calculation(string project)
    {
        Offenders(project, subdirectory: null, @"ScaleToTargetEdge\.").ShouldBeEmpty(
            "a target-edge plan is created only through TargetEdgePrintPreparationPlan.For, which " +
            "is the calculator's single caller.");
    }

    /// <summary>
    /// Infrastructure cannot mint an enlargement authority (Epic 11400 Part B1A.2D §35).
    /// </summary>
    /// <remarks>
    /// Granting permission to enlarge is an operator decision the engine records against the exact
    /// plan on offer. An adapter or a mapper that could call <c>EnlargementAuthority.For</c> could
    /// manufacture that permission — from a database row, or from nothing — and the whole point of
    /// §9 is that the permission names one exact thing a human agreed to.
    /// <para>
    /// Rehydration is deliberately not this method: reading a stored authority back is
    /// <c>EnlargementAuthority.Rehydrate</c>, which refuses an incomplete record and cannot invent
    /// one, and the mapper is expected to call it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("PrintFlow.Infrastructure")]
    [InlineData("PrintFlow.App")]
    public void No_project_outside_the_workflow_grants_enlargement_authority(string project)
    {
        Offenders(project, subdirectory: null, @"EnlargementAuthority\.For\(").ShouldBeEmpty(
            "an enlargement is authorised by the engine, against the plan actually on offer.");
    }

    [Fact]
    public void The_shell_cannot_construct_the_enlargement_command_or_receive_its_binding_facts()
    {
        Offenders("PrintFlow.App", subdirectory: null, @"WorkflowCommand\.AuthoriseEnlargement")
            .ShouldBeEmpty("the shell confirms the service's current offer, not a Revision/hash payload.");

        IEnumerable<string> sizingProperties = typeof(FlexibleSizeView)
            .GetProperties()
            .Select(property => property.Name);
        sizingProperties.ShouldAllBe(name =>
            !name.Contains("Sha", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Revision", StringComparison.OrdinalIgnoreCase));

        MethodInfo confirmation = typeof(ISessionService)
            .GetMethod(nameof(ISessionService.AuthoriseCurrentEnlargementAsync))
            .ShouldNotBeNull();
        confirmation.GetParameters().Select(parameter => parameter.ParameterType).ShouldBe(
            [typeof(PrintFlow.Domain.Ids.SessionId), typeof(Guid), typeof(string), typeof(CancellationToken)]);
    }

    [Fact]
    public void The_operator_screen_exposes_no_resampling_selector()
    {
        string xaml = ShellSource("Views", "SessionScreenView.xaml");
        xaml.ShouldNotContain("PhotoshopResizeMode", Case.Sensitive);
        xaml.ShouldNotContain("BicubicSharper", Case.Sensitive);
        xaml.ShouldNotContain("PreserveDetails", Case.Sensitive);
    }

    /// <summary>
    /// Neither the shell nor Infrastructure constructs a plan (§23).
    /// </summary>
    /// <remarks>
    /// Plan construction is <c>SessionService</c>'s, because only it holds the upstream Revision
    /// whose bytes were just re-verified and whose pixels the fit is calculated from. A plan built
    /// anywhere else would be bound to whatever that code happened to have to hand.
    /// </remarks>
    [Theory]
    [InlineData("PrintFlow.Infrastructure")]
    [InlineData("PrintFlow.App")]
    public void No_project_outside_the_workflow_service_constructs_a_plan(string project)
    {
        Offenders(project, subdirectory: null, @"PrintPreparationPlan\.For").ShouldBeEmpty(
            "SessionService owns plan construction; everything else reads the plan it produced.");
    }

    /// <summary>The service that owns plan construction really does construct it (§23).</summary>
    /// <remarks>
    /// The positive half of the rule above. Without it the two bans would still pass in a codebase
    /// where nothing produced a plan at all.
    /// </remarks>
    [Fact]
    public void The_session_service_constructs_the_plan()
    {
        WorkflowSource("Services", "SessionService.cs")
            .ShouldContain("PrintPreparationPlan.For(", Case.Sensitive);
    }

    /// <summary>
    /// No view model re-derives the limiting edge or whether a plan still applies (§23).
    /// </summary>
    /// <remarks>
    /// The read model already reports only the <i>usable</i> plan, so the shell has nothing left
    /// to decide. The members named here are the raw materials of a second staleness rule or a
    /// second edge selection; the display fields the UI slice will bind to —
    /// <c>LimitingEdge</c>, <c>MaxWidthMm</c>, <c>NeedsDimensionReview</c> — are deliberately not
    /// banned, because naming a value is display and comparing them is a decision.
    /// </remarks>
    [Theory]
    [InlineData("Covers")]
    [InlineData("UsablePrintPreparationPlan")]
    [InlineData("SourceRevisionId")]
    [InlineData("SourceSha256")]
    [InlineData("Rehydrate")]
    public void No_view_model_restates_the_stale_plan_rule(string banned)
    {
        Offenders("PrintFlow.App", "ViewModels", banned).ShouldBeEmpty(
            "staleness is decided once, in the workflow layer; the shell reads the answer.");
    }

    /// <summary>
    /// No resampling vocabulary reaches the Workflow layer or the shell (§23).
    /// </summary>
    /// <remarks>
    /// The policy is a closed Domain enum with two members, and it is fixed by the accepted
    /// contract rather than chosen. A resampling name appearing as text in a command, a view model
    /// or a screen would be an operator-selectable resample arriving by the back door — the one
    /// thing B1A.1 settled by refusing.
    /// </remarks>
    [Theory]
    [InlineData("PrintFlow.Workflow", "BICUBIC")]
    [InlineData("PrintFlow.Workflow", "ResampleMethod")]
    [InlineData("PrintFlow.Workflow", "BicubicSharper")]
    [InlineData("PrintFlow.App", "BICUBIC")]
    [InlineData("PrintFlow.App", "ResampleMethod")]
    [InlineData("PrintFlow.App", "BicubicSharper")]
    public void No_resampling_vocabulary_reaches_the_workflow_or_the_shell(string project, string banned)
    {
        Offenders(project, subdirectory: null, banned).ShouldBeEmpty(
            "the resampling policy is fixed by contract and travels as a neutral Domain enum.");
    }

    /// <summary>
    /// The Domain names no Photoshop COM or DOM type (§5, §23).
    /// </summary>
    /// <remarks>
    /// The neutral <c>PhotoshopResizeMode</c> is PrintFlow's vocabulary; mapping it to
    /// <c>ResampleMethod.NONE</c> or <c>ResampleMethod.BICUBICSHARPER</c> belongs beside the
    /// Photoshop driver, when a production resize exists. Its enum member is named
    /// <c>BicubicSharper</c> because that is what the operation is, not because a COM constant
    /// leaked upwards — so the automation vocabulary is what is banned here.
    /// </remarks>
    [Theory]
    [InlineData("ResampleMethod")]
    [InlineData("Interop")]
    [InlineData("ComImport")]
    [InlineData("Marshal")]
    [InlineData("System.Runtime.InteropServices")]
    public void The_domain_names_no_photoshop_automation_type(string banned)
    {
        Offenders("PrintFlow.Domain", subdirectory: null, banned).ShouldBeEmpty(
            "the Domain states a neutral resize policy; COM belongs to one adapter.");
    }

    // -------------------------------------------------------------------------------------
    // §17: the production path cannot use the legacy independent-pixel pair
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The request carries the typed preparation union, not a loose pair of numbers and not a
    /// mode string (§17; Epic 11400 Part B1A.2D §12, §25).
    /// </summary>
    /// <remarks>
    /// Structural, and non-nullable on purpose: a request that could be built without a
    /// preparation is a request some future call site would build without one.
    /// <para>
    /// The union is closed by a <c>private protected</c> constructor, so the two accepted contracts
    /// are the only ones that can ever reach an adapter. That is what lets Infrastructure switch
    /// over them exhaustively instead of testing a discriminator string and defaulting.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_photoshop_request_carries_a_typed_preparation_union()
    {
        PropertyInfo preparation = typeof(PhotoshopRequest)
            .GetProperty(nameof(PhotoshopRequest.Preparation))
            .ShouldNotBeNull();

        preparation.PropertyType.ShouldBe(typeof(PhotoshopPreparation));

        // A positional parameter of the primary constructor, so it cannot be omitted.
        typeof(PhotoshopRequest)
            .GetConstructors()
            .ShouldContain(c => c.GetParameters()
                .Any(p => p.ParameterType == typeof(PhotoshopPreparation)));

        // Exactly two cases, both in the Domain, and no third can be added from outside it.
        Type[] cases =
        [
            .. typeof(PhotoshopPreparation).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(PhotoshopPreparation))),
        ];
        cases.ShouldBe(
            [typeof(FitWithinBoundsPreparation), typeof(TargetEdgePreparation)],
            ignoreOrder: true);
        typeof(PhotoshopPreparation)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .ShouldBeEmpty(
                "a public constructor would let any assembly add a preparation the workflow has " +
                "never validated.");
    }

    /// <summary>
    /// No Photoshop adapter derives a target pair from the legacy independent-pixel fields (§17).
    /// </summary>
    /// <remarks>
    /// The regression §17 asks for, and the reason it is worth having: the fields are still on the
    /// request, still populated, and reading them would compile, run, and produce two numbers that
    /// look exactly like a size. Sending both to Photoshop is the non-proportional stretch the
    /// contract prohibits, and it would only be visible in the output.
    /// <para>
    /// The scan covers both adapters and the service that builds the request, because "the
    /// production Photoshop path" is all three.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Dimensions.PixelWidth")]
    [InlineData("Dimensions.PixelHeight")]
    [InlineData("Preparation.SourcePixelWidth")]
    [InlineData("Preparation.SourcePixelHeight")]
    public void The_photoshop_path_derives_no_target_pair_from_independent_pixels(string banned)
    {
        List<string> offenders =
        [
            .. Offenders("PrintFlow.Infrastructure", Path.Combine("Adapters", "Photoshop"), banned),
            .. Offenders("PrintFlow.Infrastructure", Path.Combine("Adapters", "Fake"), banned),
            .. Offenders("PrintFlow.Workflow", "Services", banned),
        ];

        offenders.ShouldBeEmpty(
            "the executable geometry is the plan's single limiting edge; two independently " +
            "converted numbers are not a Photoshop target.");
    }

    /// <summary>
    /// The plan states a projection and never an actual result (§20).
    /// </summary>
    /// <remarks>
    /// Nothing in this slice has read anything back from Photoshop, so a member called
    /// <c>ActualWidth</c> would be a claim no code could honestly make. B1A.3 adds the real
    /// read-back; until then the vocabulary itself keeps the two apart.
    /// </remarks>
    [Fact]
    public void Neither_the_plan_nor_the_attempt_claims_an_actual_photoshop_result()
    {
        foreach (Type type in new[]
                 {
                     typeof(PrintPreparationPlan), typeof(ProcessingAttempt),
                     typeof(ProcessingSession), typeof(SessionView),
                 })
        {
            foreach (PropertyInfo property in type.GetProperties())
            {
                property.Name.ShouldNotStartWith("Actual");
                property.Name.ShouldNotContain("PhotoshopResult");
            }
        }
    }

    /// <summary>
    /// No production Photoshop mutation, W1 action, TIFF save or Revision path was enabled (§23).
    /// </summary>
    /// <remarks>
    /// The scope statement re-checked from this slice's angle. The B1 boundary tests already prove
    /// the adapter names no action and constructs no <c>AdapterOutput</c>; what is new here is that
    /// a typed geometry plan now reaches the request, which is exactly the change that would make
    /// "just resize it while we are here" tempting.
    /// <para>
    /// The patterns name <b>calls</b> rather than words. Part A's Save As <i>probe</i> — a
    /// keystroke used to detect a blocking dialog and covered by its own reviewed-shortcut test —
    /// is not a save, and the adapter's refusal message legitimately says out loud which
    /// operations are not implemented. Banning the vocabulary rather than the invocation would
    /// have failed on the sentence explaining the boundary.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(@"\bresizeImage\s*\(")]
    [InlineData(@"\bResizeImage\s*\(")]
    [InlineData(@"\bchangeMode\s*\(")]
    [InlineData(@"\bConvertProfile\s*\(")]
    [InlineData(@"\.SaveAs\s*\(")]
    [InlineData(@"\bDoAction\s*\(")]
    public void The_production_photoshop_adapter_gained_no_mutation_capability(string banned)
    {
        Offenders("PrintFlow.Infrastructure", Path.Combine("Adapters", "Photoshop"), banned)
            .ShouldBeEmpty("production resizing, colour conversion and the TIFF save are B1A.3.");
    }

    // -------------------------------------------------------------------------------------
    // Part B1A.2B §29: the operator UI reads the plan and decides nothing about it
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// No view model touches the file system (Part B1A.2B §29).
    /// </summary>
    /// <remarks>
    /// The maximum-bound plan is calculated from the source's own pixels, which is exactly the
    /// value a screen would be tempted to fetch for itself — by opening the image, by measuring a
    /// preview, or by reading a file's dimensions "just to show the operator". Every one of those
    /// would be a second reading of the artefact, taken outside the integrity check the command
    /// path performs.
    /// </remarks>
    [Theory]
    [InlineData(@"System\.IO")]
    [InlineData(@"\bFile\.")]
    [InlineData(@"\bDirectory\.")]
    [InlineData(@"\bPath\.")]
    [InlineData(@"\bFileStream\b")]
    public void No_view_model_reaches_the_file_system(string banned)
    {
        Offenders("PrintFlow.App", "ViewModels", banned).ShouldBeEmpty(
            "the shell shows what the workflow layer reported; it never opens an image.");
    }

    /// <summary>
    /// The shell never constructs the millimetre-to-pixel conversion for a plan (§29).
    /// </summary>
    /// <remarks>
    /// <c>PixelsFromMillimetres</c> is the Domain's one conversion and is legitimate where a
    /// millimetre really is being converted — but under the maximum-bound contract the operator's
    /// two millimetre values are <b>limits</b>, and converting them would produce a pixel pair
    /// that looks exactly like an output size and is not one. What the image becomes is
    /// <c>FitWithinBounds</c>' answer, and it reaches the screen through the plan (§7).
    /// <para>
    /// The already-produced outputs list is deliberately not in scope. Those rows describe a TIFF
    /// that exists, recorded before this contract, and Part B1A.2B leaves them exactly as they
    /// are; what is banned here is deriving pixels from the millimetres an operator is
    /// <i>currently</i> entering as limits.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("PixelsFromMillimetres")]
    [InlineData("MillimetresPerInch")]
    [InlineData("25.4")]
    public void No_view_model_derives_pixels_from_the_operators_millimetres(string banned)
    {
        Offenders("PrintFlow.App", "ViewModels", banned).ShouldBeEmpty(
            "two independently converted millimetre values are not a projected output size.");
    }

    /// <summary>
    /// The shell offers exactly one route to the size decision, and it is the command (§29).
    /// </summary>
    /// <remarks>
    /// The "review the maximum bounds" action added by this slice is a <c>ReturnToStep</c> aimed
    /// at a target the workflow layer offered — deliberately not a second
    /// <c>SetPrintDimensions</c> call site reachable from the Photoshop step, which would let a
    /// screen rewrite the size without rewinding what was derived from the old one (§12).
    /// </remarks>
    [Fact]
    public void The_shell_has_exactly_one_set_print_dimensions_call_site()
    {
        Offenders("PrintFlow.App", subdirectory: null, @"WorkflowCommand\.SetPrintDimensions")
            .Count.ShouldBe(1, "the size is recorded from the size panel and from nowhere else.");
    }

    /// <summary>
    /// No screen or adapter turns a preset name into millimetres
    /// (Epic 11400 Part B1A.2D §3, §4, §35).
    /// </summary>
    /// <remarks>
    /// The defect this catches is the one the whole slice turns on, and it is invisible on screen:
    /// <c>PrintDimensions.NominalMillimetres</c> answers "how big is a sheet of A4" — 210 × 297 mm
    /// — while the configured preset answers "how big does this shop print A4" — a 280 mm maximum
    /// long edge. A view model that reached for the first would show, record and print a limit
    /// nobody configured, 17 mm out on every A4 job, and every test asserting "the preset's
    /// millimetres" would still pass.
    /// <para>
    /// So the executable answer comes from
    /// <c>IWorkstationPresetProvider.GetPrintSizeRecommendations</c>, resolved in the workflow
    /// layer and reported to the shell. The nominal sizes remain in the Domain as descriptive
    /// metadata, which is why the ban is on the projects rather than on the method (§4).
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("PrintFlow.App")]
    [InlineData("PrintFlow.Infrastructure")]
    public void No_project_outside_the_domain_reads_the_nominal_paper_sizes(string project)
    {
        Offenders(project, subdirectory: null, @"NominalMillimetres\(").ShouldBeEmpty(
            "a named preset's executable limit comes from the verified preset, never from the " +
            "paper standard it is named after.");
    }

    /// <summary>
    /// The preset recommendation is resolved in the workflow layer and reported to the shell
    /// (§3, §28, §35).
    /// </summary>
    /// <remarks>
    /// The positive half of the rule above. Without it, the ban would still pass in a shell that
    /// offered no named sizes at all — and the point is not that the shell states no size, it is
    /// that the size it states came from the verified preset by way of the read model.
    /// </remarks>
    [Fact]
    public void The_session_service_resolves_the_configured_recommendation()
    {
        WorkflowSource("Services", "SessionService.cs")
            .ShouldContain("GetPrintSizeRecommendations()", Case.Sensitive);

        ShellSource("ViewModels", "SessionViewModel.cs")
            .ShouldContain("Sizing.PresetRecommendations", Case.Sensitive);
    }

    /// <summary>
    /// The review action goes through <c>ReturnToStep</c>, and takes its destination from the
    /// workflow layer (§29).
    /// </summary>
    /// <remarks>
    /// The positive half of the rule above: without it, the ban would still pass in a shell where
    /// the review action did nothing at all. The <c>ReturnTargets</c> read is what makes the
    /// destination the engine's answer rather than an assumption that the size step is always
    /// behind the Photoshop step.
    /// </remarks>
    [Fact]
    public void The_bounds_review_action_returns_to_a_target_the_workflow_layer_offered()
    {
        string screen = ShellSource("ViewModels", "SessionViewModel.cs");

        screen.ShouldContain("ReviewBoundsTarget", Case.Sensitive);
        screen.ShouldContain("ReturnTargets.FirstOrDefault", Case.Sensitive);
        screen.ShouldContain("WorkflowCommand.ReturnToStep", Case.Sensitive);
    }

    /// <summary>
    /// The read model exposes the producing attempt's plan as a flattened view, not as a
    /// database entity or a rebindable record (§20).
    /// </summary>
    /// <remarks>
    /// Structural, because the temptation is to hand the shell the <c>PrintPreparationPlan</c>
    /// itself: it already has every field an audit line needs. It also carries
    /// <c>SourceRevisionId</c>, <c>SourceSha256</c> and <c>Covers</c> — the raw materials of a
    /// second staleness rule in a layer that must not hold one.
    /// </remarks>
    [Fact]
    public void The_attempt_preparation_view_carries_no_binding_and_no_actual_result()
    {
        PropertyInfo attempt = typeof(SessionView)
            .GetProperty(nameof(SessionView.AttemptPreparation))
            .ShouldNotBeNull();

        attempt.PropertyType.ShouldBe(typeof(PrintPreparationAttemptView));

        IEnumerable<string> names = typeof(PrintPreparationAttemptView)
            .GetProperties()
            .Select(property => property.Name);

        foreach (string name in names)
        {
            name.ShouldNotStartWith("Actual");
            name.ShouldNotContain("Sha");
            name.ShouldNotContain("RevisionId");
        }
    }

    /// <summary>
    /// The schema is forward-only and moves one script at a time
    /// (Epic 11400 Part B1A.2A §29; Part B1A.2D §20).
    /// </summary>
    /// <remarks>
    /// The B1A.2B UI slice added nothing here, because a screen has nothing to migrate. B1A.2D
    /// added exactly one script, 0006, for the flexible-size selection, the TargetEdgeV1 plan and
    /// the enlargement authority — and this assertion is what keeps "exactly one" honest: a
    /// second script appearing in the same slice would mean a migration was added without the
    /// discussion of what it destroys.
    /// <para>
    /// It also fixes the numbering. A script that skipped or reused a version would apply out of
    /// order against a database in the field, so the newest name is checked rather than merely
    /// counted.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_migration_set_ends_at_the_flexible_size_migration()
    {
        string directory = Path.Combine(FindProjectDirectory("PrintFlow.Infrastructure"), "Sqlite");

        IEnumerable<string> scripts = Directory
            .EnumerateFiles(directory, "*.sql", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal);

        scripts.Last().ShouldStartWith("0006", Case.Sensitive);
    }

    // -------------------------------------------------------------------------------------
    // Source helpers
    // -------------------------------------------------------------------------------------

    private static string ShellSource(params string[] relativePath) =>
        File.ReadAllText(Path.Combine([FindProjectDirectory("PrintFlow.App"), .. relativePath]));


    private static string WorkflowSource(params string[] relativePath) =>
        File.ReadAllText(Path.Combine(
            [FindProjectDirectory("PrintFlow.Workflow"), .. relativePath]));

    private static IReadOnlyList<string> Offenders(string project, string? subdirectory, string banned)
    {
        List<string> offenders = [];

        foreach ((string file, string[] lines) in SourceOf(project, subdirectory))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (Contains(lines[i], banned))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        return offenders;
    }

    /// <summary>
    /// Matches in executable text only, never inside a comment.
    /// </summary>
    /// <remarks>
    /// The comments in this slice discuss these very names — explaining why the production path
    /// must not read <c>Dimensions.PixelWidth</c> requires writing it down — and a scan that failed
    /// on the explanation would push the reasoning out of the code, which is the opposite of what
    /// these tests are for.
    /// </remarks>
    private static bool Contains(string line, string pattern)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith("///", StringComparison.Ordinal) ||
            trimmed.StartsWith("*", StringComparison.Ordinal) ||
            trimmed.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        return Regex.IsMatch(line, pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
    }

    private static IEnumerable<(string File, string[] Lines)> SourceOf(string project, string? subdirectory)
    {
        string directory = FindProjectDirectory(project);
        if (subdirectory is not null)
        {
            directory = Path.Combine(directory, subdirectory);
        }

        if (!Directory.Exists(directory))
        {
            yield break;
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

    private static string FindProjectDirectory(string projectName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException(
                "Could not locate the repository root (PrintFlowStudio.sln) above " + AppContext.BaseDirectory)
            : Path.Combine(current.FullName, "src", projectName);
    }
}
