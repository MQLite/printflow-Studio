using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// Builds the production Meitu foundation for the controlled workstation smoke
/// (Epic 11300 Part A §22).
/// </summary>
/// <remarks>
/// This exists because the alternative was worse. Epic 11500 has not implemented real
/// workstation verification, so <c>FoundationEnvironmentGate</c> still refuses every
/// <c>Production</c> adapter — correctly. The tempting shortcut is to relax the gate so the
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
        MeituAutomationOptions? options = null)
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

        return new ProductionMeituProcessor(baselines, locator, driver, workspace, resolved, clock);
    }
}
