using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// Builds the production Meitu foundation for the controlled workstation smoke
/// (Epic 11300 Part A §22).
/// </summary>
/// <remarks>
/// This exists because the alternative was worse. The registered environment gate refuses every
/// <c>Production</c> adapter on a workstation that has not verified — correctly, and since Epic
/// 11500 Part B on evidence rather than unconditionally. The tempting shortcut is to relax the gate so the
/// smoke can run; that would weaken the one control standing between a half-built adapter and a
/// live session, and §22 rules it out.
///
/// So the smoke gets its own composition instead. What that buys is precise: the object graph
/// assembled here produces an <see cref="IMeituAutomationFoundation"/> and nothing else. It has
/// no session, no repository, no workflow engine and no <c>IMeituProcessor</c> registration, so
/// nothing composed here can start a session, record an attempt or create a Revision — the
/// gate stays authoritative over everything it was authoritative over before, because none of
/// that is reachable from this graph at all.
/// </remarks>
public static class MeituAutomationComposition
{
    /// <summary>
    /// Composes the real Win32/UIA foundation against the signed preset.
    /// </summary>
    /// <param name="presetManifestAbsolutePath">The signed workstation preset manifest.</param>
    /// <param name="expectedPresetSha256">The digest configuration says the manifest must hash to.</param>
    /// <param name="workspace">The workspace that resolves working-copy references to paths.</param>
    /// <param name="evidenceDirectory">Where failure captures are written. Local, never committed.</param>
    /// <param name="clock">Time source for every bounded wait.</param>
    /// <param name="options">Timings and control names; defaults are used when omitted.</param>
    public static IMeituAutomationFoundation CreateFoundation(
        string presetManifestAbsolutePath,
        Sha256 expectedPresetSha256,
        IWorkspace workspace,
        string evidenceDirectory,
        TimeProvider clock,
        MeituAutomationOptions? options = null) =>
        Create(presetManifestAbsolutePath, expectedPresetSha256, workspace, evidenceDirectory, clock, options);

    /// <summary>
    /// The same object graph, reached through the workflow seam instead
    /// (Epic 11300 Part B2B §37).
    /// </summary>
    /// <remarks>
    /// Part B2B is the first slice in which <see cref="IMeituProcessor.ProcessAsync"/> can
    /// return a success, and §37 asks for that to be proved through the production-adapter seam
    /// without enabling Production composition broadly. This is how: the same adapter, reached
    /// through the same interface <c>SessionService</c> would use, composed with no session, no
    /// repository, no workflow engine and no registration.
    ///
    /// What that buys is exactly what it looks like. A caller here can run a real Enhancement and
    /// get a real <c>AdapterOutput</c> back; it cannot start a session, record an attempt or
    /// create a Revision, because none of that is reachable from this graph. <c>IEnvironmentGate</c>
    /// stays authoritative over everything it was authoritative over before, and Epic 11500's
    /// refusal of Production adapters in the application composition is untouched.
    /// </remarks>
    public static IMeituProcessor CreateProductionProcessor(
        string presetManifestAbsolutePath,
        Sha256 expectedPresetSha256,
        IWorkspace workspace,
        string evidenceDirectory,
        TimeProvider clock,
        MeituAutomationOptions? options = null) =>
        Create(presetManifestAbsolutePath, expectedPresetSha256, workspace, evidenceDirectory, clock, options);

    private static ProductionMeituProcessor Create(
        string presetManifestAbsolutePath,
        Sha256 expectedPresetSha256,
        IWorkspace workspace,
        string evidenceDirectory,
        TimeProvider clock,
        MeituAutomationOptions? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetManifestAbsolutePath);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        ArgumentNullException.ThrowIfNull(clock);

        MeituAutomationOptions resolved = options ?? new MeituAutomationOptions();

        PresetMeituBaselineProvider baselines = new(presetManifestAbsolutePath, expectedPresetSha256);
        Win32ExternalAppWindowLocator locator = new();
        UiaElementProvider elements = new();
        Win32ScopedInputSink input = new(locator);
        GdiWindowEvidenceSink evidence = new(evidenceDirectory, clock);

        GuardedMeituUiDriver driver = new(
            locator, elements, input, evidence, baselines, resolved, clock);

        // The same inspector the workflow uses for every other file. §16 rules out a second
        // image-inspection implementation inside the adapter, and composing the real one here is
        // what makes that structural rather than a note in a report.
        WicFileInspector inspector = new();

        return new ProductionMeituProcessor(
            baselines, locator, driver, workspace, inspector, new WicMeituTransparencyInspector(),
            new FileSystemMeituOutputProbe(),
            resolved, clock);
    }
}
