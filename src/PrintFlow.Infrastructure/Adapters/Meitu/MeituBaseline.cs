using System.Collections.Immutable;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// The accepted identity and recognition signals for Meitu on this workstation, as read from
/// the signed Epic 11000 preset chain (Epic 11300 Part A §7).
/// </summary>
/// <param name="ExecutablePath">The one executable PrintFlow may launch or match against.</param>
/// <param name="ExecutableSha256">The accepted binary digest for that executable.</param>
/// <param name="AcceptedVersion">The accepted Meitu runtime version, recorded as evidence.</param>
/// <param name="UiLanguage">The Meitu UI language the recognition signals were captured in.</param>
/// <param name="AcceptedWindowTitles">
/// Titles observed on the accepted runtime. Corroborating evidence only — process ownership,
/// not a title, is what identifies a window (§9).
/// </param>
/// <param name="WelcomeWindowTitle">
/// The exact window title Epic 11000 observed on the clean start page.
/// </param>
/// <param name="WelcomeMarkers">
/// Stable structural labels of the clean start page, extracted from the signed clean-start
/// evidence file. Deliberately excludes theme colour and the rotating advertisement, which
/// Epic 11000 recorded as unsafe recognition inputs.
/// </param>
/// <remarks>
/// This record is the adapter's <i>only</i> source of Meitu facts. Nothing in the adapter
/// hard-codes a path, a version or a label, so a Meitu upgrade is a preset revalidation rather
/// than a code change (§7).
/// </remarks>
public sealed record MeituBaseline(
    string ExecutablePath,
    Sha256 ExecutableSha256,
    string AcceptedVersion,
    string UiLanguage,
    ImmutableArray<string> AcceptedWindowTitles,
    string WelcomeWindowTitle,
    ImmutableArray<string> WelcomeMarkers);

/// <summary>
/// Supplies the verified <see cref="MeituBaseline"/>, or refuses.
/// </summary>
/// <remarks>
/// Infrastructure-only on purpose: an executable path is an environment fact, and letting it
/// surface in <c>PrintFlow.Workflow</c> would give the workflow layer a way to name a program
/// to run. Workflow sees <c>IMeituProcessor</c> and nothing else (§26).
/// </remarks>
public interface IMeituBaselineProvider
{
    /// <summary>
    /// Returns the accepted Meitu identity, or a failure when the signed chain cannot be
    /// verified.
    /// </summary>
    /// <remarks>
    /// Fails closed in every ambiguous case — missing manifest, hash mismatch, absent
    /// <c>meituContract</c>. There is no "assume the default install location" branch, because
    /// searching the machine for a plausible <c>XiuXiu.exe</c> and picking one is precisely
    /// what §7 forbids.
    /// </remarks>
    OperationResult<MeituBaseline> GetVerifiedBaseline();
}
