using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PrintFlow.Infrastructure.Verification;

/// <summary>
/// One PrintFlow assembly, identified by what it is rather than by where it is.
/// </summary>
/// <param name="Name">The file name, e.g. <c>PrintFlow.Infrastructure.dll</c>.</param>
/// <param name="Sha256">
/// The digest of the bytes. Null when the assembly could not be read at all, which never compares
/// equal to anything — an identity that could not be established is not a matching identity.
/// </param>
/// <param name="BuildIdentity">
/// <c>AssemblyInformationalVersion</c>, which on this repository is
/// <c>&lt;version&gt;+&lt;source revision&gt;</c>. Null when unreadable.
/// </param>
public sealed record ProductAssemblyIdentity(string Name, string? Sha256, string? BuildIdentity);

/// <summary>
/// The identity of the PrintFlow code that a regression run exercised, that an installation
/// carries, and that a running application loaded (PF-AUDIT-R1, audit finding F3).
/// </summary>
/// <remarks>
/// <b>Why a three-part version string is not enough.</b> The production revalidation record used to
/// say only "PrintFlow 0.1.0 was revalidated". Two different builds of PrintFlow share that string,
/// so replacing the installed payload with different bytes under the same version inherited the
/// previous payload's production approval — exactly the silent change of supported environment
/// SCRUM-11123 forbids.
/// <para>
/// <b>Why two identities and not one.</b> These are different facts and this repository produces
/// both. The regression run drives the Product from an ordinary Release build inside a test host;
/// an installation carries the RID-specific self-contained publish of the same commit. Measured on
/// this checkout, those two produce Product assemblies whose bytes differ while their
/// <c>AssemblyInformationalVersion</c> is identical. So:
/// </para>
/// <list type="bullet">
/// <item>
/// <see cref="ProductAssemblyIdentity.BuildIdentity"/> is a label that spans build modes; it does not
/// establish origin. A dirty build can share it. The controlled regression build pair pins both outputs.
/// </item>
/// <item>
/// <see cref="ProductAssemblyIdentity.Sha256"/> answers "are these the same bytes?" and is what
/// pins one candidate across time — recorded by the run, re-read by the revalidation writer before
/// it publishes, and compared by the running application against its own loaded assemblies.
/// </item>
/// </list>
/// <para>
/// <b>What is deliberately not identity.</b> Paths, timestamps, folder names and the repository's
/// HEAD. Two folders holding the same bytes are the same candidate; a documentation-only commit
/// changes HEAD and changes nothing that was tested, so it does not invalidate a record. What
/// invalidates one is a rebuild and reinstall, which really is a different candidate.
/// </para>
/// <para>
/// Not a signature and not a certificate. It answers "is this the thing that was tested?", not
/// "who produced this?" — and a shop with one workstation and an offline installer needs the first
/// question answered, not a PKI.
/// </para>
/// </remarks>
public static class ProductBuildIdentity
{
    /// <summary>
    /// The PrintFlow assemblies whose bytes decide production behaviour.
    /// </summary>
    /// <remarks>
    /// The same four the installer payload policy lists under <c>requiredFiles</c> as product code,
    /// and no others: the self-contained .NET and WPF assemblies beside them are the runtime, are
    /// enumerated by pattern rather than by name across servicing updates, and are not what a
    /// PrintFlow candidate is. Ordered so that two readings of the same folder produce the same
    /// list in the same order.
    /// </remarks>
    public static readonly ImmutableArray<string> ProductAssemblyFileNames =
    [
        "PrintFlow.App.dll",
        "PrintFlow.Domain.dll",
        "PrintFlow.Infrastructure.dll",
        "PrintFlow.Workflow.dll",
    ];

    /// <summary>Reads the identity of the Product assemblies in one folder.</summary>
    /// <remarks>
    /// Every named assembly appears in the result whether or not it was readable, because "the
    /// candidate is missing PrintFlow.Workflow.dll" has to reach a comparison as a mismatch rather
    /// than as a shorter list that happens to agree about the rest.
    /// </remarks>
    /// <param name="folder">The folder to read. An absent folder yields an all-unreadable identity.</param>
    public static ImmutableArray<ProductAssemblyIdentity> FromFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        return
        [
            .. ProductAssemblyFileNames.Select(name =>
            {
                string path = Path.Combine(folder, name);
                try
                {
                    if (!File.Exists(path))
                    {
                        return new ProductAssemblyIdentity(name, null, null);
                    }

                    string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                    string? build = FileVersionInfo.GetVersionInfo(path).ProductVersion;
                    return new ProductAssemblyIdentity(
                        name, digest, string.IsNullOrWhiteSpace(build) ? null : build.Trim());
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new ProductAssemblyIdentity(name, null, null);
                }
            }),
        ];
    }

    /// <summary>
    /// The identity of the Product assemblies this process actually loaded.
    /// </summary>
    /// <remarks>
    /// Resolved as "the folder this assembly was loaded from", not by reflecting over each
    /// project's types. Infrastructure cannot reference <c>PrintFlow.App</c> — App references it —
    /// and the four Product assemblies always sit in one folder, whether that is an installation,
    /// a Release build or a test host's output. An empty
    /// <see cref="System.Reflection.Assembly.Location"/> (a single-file publish, which this
    /// product does not use) yields an all-unreadable identity, which fails closed.
    /// </remarks>
    public static ImmutableArray<ProductAssemblyIdentity> Running()
    {
        string location = typeof(ProductBuildIdentity).Assembly.Location;
        return string.IsNullOrWhiteSpace(location)
            ? [.. ProductAssemblyFileNames.Select(name => new ProductAssemblyIdentity(name, null, null))]
            : FromFolder(Path.GetDirectoryName(location)!);
    }

    /// <summary>
    /// A short digest of a whole candidate, for a diagnostic line and an audit trail.
    /// </summary>
    /// <remarks>
    /// Derived, never stored as the authority: it is re-computable from the identity list it
    /// summarises, so it cannot disagree with the data underneath it, and no comparison in this
    /// codebase is decided by it. It exists so a human can say "candidate 4C7A…" without reading
    /// four digests. This is the one composite in the contract, and it lives here alone — the
    /// PowerShell tooling compares per-assembly digests and never re-implements it.
    /// </remarks>
    public static string Fingerprint(ImmutableArray<ProductAssemblyIdentity> assemblies)
    {
        // A default array rather than an empty one is what an absent JSON property deserialises to,
        // and this is called from diagnostic message construction. It answers rather than throws:
        // a fingerprint is never a gate, and a failing check must not be replaced by an exception
        // raised while explaining itself.
        StringBuilder canonical = new();
        foreach (ProductAssemblyIdentity assembly in (assemblies.IsDefault ? [] : assemblies)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            canonical
                .Append(assembly.Name.ToLowerInvariant()).Append(':')
                .Append(assembly.Sha256?.ToUpperInvariant() ?? "(unreadable)").Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    /// <summary>
    /// Whether two candidate identities are the same bytes, and what differs when they are not.
    /// </summary>
    /// <remarks>
    /// Pure, and the only place the answer is decided. An unreadable digest on either side is a
    /// mismatch: the question is "are these the same bytes", and "we could not tell" is not yes.
    /// </remarks>
    /// <param name="recorded">The identity some record or run result claims.</param>
    /// <param name="observed">The identity read from the thing in front of us.</param>
    /// <returns>An empty array when they match; otherwise one sentence per difference.</returns>
    public static ImmutableArray<string> CompareBytes(
        ImmutableArray<ProductAssemblyIdentity> recorded,
        ImmutableArray<ProductAssemblyIdentity> observed) =>
        Compare(recorded, observed, a => a.Sha256, "bytes", Short);

    /// <summary>
    /// Whether two candidate identities carry the same informational label, not proof of source origin.
    /// </summary>
    /// <remarks>
    /// A diagnostic comparison across build modes. The build-pair contract separately pins each
    /// output set; neither label equality nor cross-profile byte equality establishes origin.
    /// </remarks>
    public static ImmutableArray<string> CompareBuildIdentity(
        ImmutableArray<ProductAssemblyIdentity> recorded,
        ImmutableArray<ProductAssemblyIdentity> observed) =>
        Compare(recorded, observed, a => a.BuildIdentity, "build identity", value => value);

    private static ImmutableArray<string> Compare(
        ImmutableArray<ProductAssemblyIdentity> recorded,
        ImmutableArray<ProductAssemblyIdentity> observed,
        Func<ProductAssemblyIdentity, string?> field,
        string what,
        Func<string, string> render)
    {
        List<string> differences = [];

        foreach (string name in ProductAssemblyFileNames)
        {
            ProductAssemblyIdentity? left = Find(recorded, name);
            ProductAssemblyIdentity? right = Find(observed, name);

            string? claimed = left is null ? null : field(left);
            string? actual = right is null ? null : field(right);

            if (string.IsNullOrWhiteSpace(claimed))
            {
                differences.Add($"{name}: no {what} recorded.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(actual))
            {
                differences.Add($"{name}: {what} could not be read here.");
                continue;
            }

            if (!string.Equals(claimed.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                differences.Add(
                    $"{name}: recorded {what} {render(claimed.Trim())}, found {render(actual.Trim())}.");
            }
        }

        return [.. differences];
    }

    private static ProductAssemblyIdentity? Find(
        ImmutableArray<ProductAssemblyIdentity> assemblies, string name) =>
        assemblies.IsDefaultOrEmpty
            ? null
            : assemblies.FirstOrDefault(a =>
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string Short(string digest) =>
        digest.Length < 12 ? digest : digest[..12] + "…";
}
