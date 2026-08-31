using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// Builds the production Photoshop foundation for the controlled workstation smoke
/// (Epic 11400 Part A §19, §20).
/// </summary>
/// <remarks>
/// This exists because the alternative was worse. Epic 11500 has not implemented real
/// workstation verification, so <c>FoundationEnvironmentGate</c> still refuses every
/// <c>Production</c> adapter — correctly. The tempting shortcut is to relax the gate so the
/// smoke can run; that would weaken the one control standing between a half-built adapter and a
/// live session, and §19 rules it out.
///
/// So the smoke gets its own composition instead, and what that buys is precise: the object
/// graph assembled here produces an <see cref="IPhotoshopAutomationFoundation"/> and nothing
/// else. It has no session, no repository, no workflow engine and no
/// <c>IPhotoshopOutputProcessor</c> registration, so nothing composed here can start a session,
/// record an attempt or create a Revision — the gate stays authoritative over everything it was
/// authoritative over before, because none of that is reachable from this graph at all.
///
/// There is deliberately no <c>CreateProductionProcessor</c> counterpart to the Meitu
/// composition's. That method exists over there because the Meitu adapter can genuinely
/// complete a workflow operation; the Photoshop one cannot yet, and offering a way to reach its
/// workflow seam would only offer a way to reach a refusal (§19).
/// </remarks>
public static class PhotoshopAutomationComposition
{
    /// <summary>Composes the real Win32 foundation against the signed preset.</summary>
    /// <param name="presetManifestAbsolutePath">The signed workstation preset manifest.</param>
    /// <param name="expectedPresetSha256">The digest configuration says the manifest must hash to.</param>
    /// <param name="workspace">The workspace that resolves managed references to paths.</param>
    /// <param name="evidenceDirectory">Where failure captures are written. Local, never committed.</param>
    /// <param name="clock">Time source for every bounded wait.</param>
    /// <param name="options">Timings; defaults are used when omitted.</param>
    public static IPhotoshopAutomationFoundation CreateFoundation(
        string presetManifestAbsolutePath,
        Sha256 expectedPresetSha256,
        IWorkspace workspace,
        string evidenceDirectory,
        TimeProvider clock,
        PhotoshopAutomationOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetManifestAbsolutePath);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        ArgumentNullException.ThrowIfNull(clock);

        return Create(
            presetManifestAbsolutePath,
            expectedPresetSha256,
            workspace,
            evidenceDirectory,
            clock,
            options);
    }

    /// <summary>
    /// Composes the Part A foundation plus the closed B1A.3 in-memory preparation seam for the
    /// controlled Photoshop smoke. It still exposes no workflow output operation.
    /// </summary>
    public static IPhotoshopPreparationAutomation CreatePreparationAutomation(
        string presetManifestAbsolutePath,
        Sha256 expectedPresetSha256,
        IWorkspace workspace,
        string evidenceDirectory,
        TimeProvider clock,
        PhotoshopAutomationOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetManifestAbsolutePath);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        ArgumentNullException.ThrowIfNull(clock);

        return Create(
            presetManifestAbsolutePath,
            expectedPresetSha256,
            workspace,
            evidenceDirectory,
            clock,
            options);
    }

    /// <summary>
    /// Composes the closed B1B CMYK + W1 operation for controlled workstation proof. It exposes
    /// no workflow output, TIFF, save, arbitrary Action or generic scripting surface.
    /// </summary>
    public static IPhotoshopW1Automation CreateW1Automation(
        string presetManifestAbsolutePath,
        Sha256 expectedPresetSha256,
        IWorkspace workspace,
        string evidenceDirectory,
        TimeProvider clock,
        PhotoshopAutomationOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetManifestAbsolutePath);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        ArgumentNullException.ThrowIfNull(clock);

        return Create(
            presetManifestAbsolutePath,
            expectedPresetSha256,
            workspace,
            evidenceDirectory,
            clock,
            options);
    }

    private static ProductionPhotoshopOutputProcessor Create(
        string presetManifestAbsolutePath,
        Sha256 expectedPresetSha256,
        IWorkspace workspace,
        string evidenceDirectory,
        TimeProvider clock,
        PhotoshopAutomationOptions? options)
    {
        PhotoshopAutomationOptions resolved = options ?? new PhotoshopAutomationOptions();

        PresetPhotoshopBaselineProvider baselines = new(presetManifestAbsolutePath, expectedPresetSha256);
        Win32ExternalAppWindowLocator locator = new();
        Win32VerifiedControlSink controls = new();
        Win32ScopedInputSink input = new(locator);
        GdiWindowEvidenceSink evidence = new(evidenceDirectory, clock);

        GuardedPhotoshopUiDriver driver = new(
            locator, controls, input, evidence, baselines, resolved, clock);

        return new ProductionPhotoshopOutputProcessor(
            baselines, locator, driver, workspace, resolved, clock);
    }
}
