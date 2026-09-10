using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrintFlow.Tests.Regression;

/// <summary>
/// Raised when a run identity cannot be claimed, and never caught into a softer outcome.
/// </summary>
/// <remarks>
/// A distinct type rather than a bare <see cref="InvalidOperationException"/> so a test can assert
/// that a run refused *for this reason* — and so nothing in the run path can mistake a refused
/// claim for an ordinary failure of a case.
/// </remarks>
public sealed class RegressionExecutionClaimRefusedException(string message) : Exception(message);

/// <summary>
/// One invocation's exclusive right to write into a run folder (PF-AUDIT-R1, audit finding F4).
/// </summary>
/// <remarks>
/// <b>The defect this closes.</b> The runner used to call <c>Directory.CreateDirectory(runFolder)</c>
/// and start working. That succeeds on a folder which already holds a completed run, so a second
/// execution silently inherited the first one's destination: if the second one then failed before
/// writing its own <c>result.json</c>, the first one's <c>Passed</c> was still sitting there for the
/// wrapper to read and report as this invocation's success.
/// <para>
/// <b>Why a file and not a check.</b> "Does the folder already exist?" followed by "then create it"
/// is two operations, and two invocations can both pass the first. Creating the claim with
/// <see cref="FileMode.CreateNew"/> is one operation that the file system decides: exactly one
/// concurrent claimant gets the file and every other one gets an exception. That is the whole
/// concurrency argument, and it is why this is not a lock object, a mutex or a database row.
/// </para>
/// <para>
/// <b>Why it never deletes.</b> A refused claim leaves the existing folder exactly as it was.
/// Clearing a previous run to make room would destroy the evidence that the run identity was
/// already used, which is the one fact the refusal exists to report. The operator picks a new
/// <c>-RunId</c>; nothing here tidies up on their behalf.
/// </para>
/// </remarks>
/// <param name="RunId">The run identity being claimed.</param>
/// <param name="InvocationId">The single invocation that holds the claim.</param>
/// <param name="ClaimedBy">The Windows account that staked it.</param>
/// <param name="ClaimedAtLocal">When it was staked.</param>
/// <param name="ClaimedByProcess">The process that staked it, for an auditor reading a stale claim.</param>
public sealed record RegressionExecutionClaim(
    string RunId,
    string InvocationId,
    string ClaimedBy,
    string ClaimedAtLocal,
    string ClaimedByProcess)
{
    /// <summary>The claim file's name inside the run folder.</summary>
    public const string FileName = "execution-claim.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Claims <paramref name="runId"/> for this invocation, or refuses.
    /// </summary>
    /// <remarks>
    /// Called before the run reads configuration, composes the graph or looks at an external
    /// application, so a refused claim costs nothing and touches nothing.
    /// </remarks>
    /// <param name="runFolder">Where this run's evidence will be written.</param>
    /// <param name="runId">The run identity.</param>
    /// <param name="invocationId">This invocation's identity, minted by whoever started it.</param>
    /// <exception cref="RegressionExecutionClaimRefusedException">
    /// The destination already holds a completed run, or the identity is already claimed.
    /// </exception>
    public static RegressionExecutionClaim Stake(string runFolder, string runId, string invocationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationId);

        // Whether this call is what brought the folder into existence. A refused claim must leave the
        // file system as it found it: creating an empty folder and then refusing would burn the run
        // identity for the wrapper, which reads an existing destination as "already used". Removing a
        // folder this call created, and only while it is empty, destroys no evidence.
        bool created = !Directory.Exists(runFolder);
        Directory.CreateDirectory(runFolder);

        string resultPath = Path.Combine(runFolder, "result.json");
        if (File.Exists(resultPath))
        {
            throw new RegressionExecutionClaimRefusedException(
                $"Run '{runId}' already holds a completed run's result at '{resultPath}'. A new " +
                "execution needs a run identity of its own; this one is not reused and the " +
                "existing evidence is not removed. Start again with a different -RunId.");
        }

        RegressionExecutionClaim claim = new(
            runId,
            invocationId,
            $"{Environment.UserDomainName}\\{Environment.UserName}",
            DateTimeOffset.Now.ToString("o"),
            $"{Environment.ProcessPath ?? "(unknown)"} pid {Environment.ProcessId}");

        string claimPath = Path.Combine(runFolder, FileName);
        try
        {
            // CreateNew, and the write happens inside the handle that won the race. Two
            // invocations reaching this line together cannot both leave with a claim.
            using FileStream stream = new(claimPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using StreamWriter writer = new(stream);
            writer.Write(JsonSerializer.Serialize(claim, Options));
        }
        catch (IOException)
        {
            RegressionExecutionClaim? existing = Read(runFolder);
            RemoveIfThisCallCreatedIt(runFolder, created);
            throw new RegressionExecutionClaimRefusedException(
                $"Run '{runId}' is already claimed by invocation " +
                $"'{existing?.InvocationId ?? "(unreadable)"}', staked by " +
                $"{existing?.ClaimedBy ?? "(unknown)"} at {existing?.ClaimedAtLocal ?? "(unknown)"}. " +
                "Two executions cannot share one run identity. Start again with a different -RunId.");
        }

        return claim;
    }

    /// <summary>
    /// Undoes only this call's own directory creation, and only while the directory is empty.
    /// </summary>
    /// <remarks>
    /// The one deletion in this type, and it is deliberately narrow: a folder that already existed is
    /// never touched, and a folder holding anything at all is never removed. Without it a refused
    /// claim would leave an empty destination behind, and the wrapper — which correctly reads an
    /// existing destination as an identity already used — would refuse that run id for good.
    /// </remarks>
    private static void RemoveIfThisCallCreatedIt(string runFolder, bool created)
    {
        if (!created)
        {
            return;
        }

        try
        {
            if (Directory.Exists(runFolder) &&
                Directory.EnumerateFileSystemEntries(runFolder).Any())
            {
                return;
            }

            Directory.Delete(runFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A directory that will not delete changes nothing about the refusal.
        }
    }

    /// <summary>The claim in a run folder, or null when there is not one to read.</summary>
    public static RegressionExecutionClaim? Read(string runFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runFolder);

        string path = Path.Combine(runFolder, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RegressionExecutionClaim>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
