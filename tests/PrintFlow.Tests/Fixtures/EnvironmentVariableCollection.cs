using System.Collections;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// The collection every test that sets a process environment variable belongs to, so that no two
/// of them run at once.
/// </summary>
/// <remarks>
/// Environment variables are process-global, and the regression runner reads its whole invocation
/// from them. Two tests setting <c>PRINTFLOW_REGRESSION_RUN_ID</c> concurrently would each see the
/// other's value — a flake that looks like a run-identity defect and is not one. Everything else in
/// the suite keeps running in parallel, because nothing else touches them.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class EnvironmentVariableCollection
{
    public const string Name = "EnvironmentVariables";
}

/// <summary>
/// Environment variables set for the length of a block and restored afterwards, whatever happens.
/// </summary>
/// <remarks>
/// Restored rather than cleared: a variable that was already set belongs to whoever set it, and a
/// test that blanked it would change the behaviour of everything after it in the same process.
/// </remarks>
internal sealed class EnvironmentVariables : IEnumerable<KeyValuePair<string, string?>>, IDisposable
{
    private readonly Dictionary<string, string?> _previous = [];

    /// <summary>Sets one variable, remembering what it was.</summary>
    public string? this[string name]
    {
        set
        {
            _previous.TryAdd(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <summary>Present only so the collection initialiser syntax is available.</summary>
    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator() => _previous.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        foreach ((string name, string? value) in _previous)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
