namespace PrintFlow.Domain.Ids;

/// <summary>
/// Produces new entity identifiers.
/// </summary>
/// <remarks>
/// Kept behind an interface for one reason only: deterministic tests. Production uses
/// <see cref="SystemIdGenerator"/>, which delegates to the in-box UUIDv7 API on .NET 10
/// (Epic 11100 plan §6.6). The pure workflow engine never calls this — identifiers arrive
/// through <c>CommandContext</c> so the reducer stays free of hidden non-determinism.
/// </remarks>
public interface IIdGenerator
{
    Guid NewId();
}

/// <summary>Production identifier source: time-ordered UUIDv7.</summary>
public sealed class SystemIdGenerator : IIdGenerator
{
    public static readonly SystemIdGenerator Instance = new();

    public Guid NewId() => Guid.CreateVersion7();
}
