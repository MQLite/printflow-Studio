namespace PrintFlow.Domain.Ids;

/// <summary>
/// Marker for a strongly typed, UUIDv7-backed identifier.
/// </summary>
/// <remarks>
/// Wrapper structs exist so that a <see cref="RevisionId"/> can never be passed where a
/// <see cref="SessionId"/> is expected. Epic 11100 plan §6.6 — primitive obsession for
/// identifiers is exactly the class of defect that is expensive to find later.
/// </remarks>
public interface ITypedId
{
    Guid Value { get; }
}

/// <summary>Identifies one <c>ProcessingSession</c>.</summary>
public readonly record struct SessionId(Guid Value) : ITypedId
{
    public static SessionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}

/// <summary>Identifies one <c>Revision</c>: a validated, hashed file in the derivation tree.</summary>
public readonly record struct RevisionId(Guid Value) : ITypedId
{
    public static RevisionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}

/// <summary>Identifies one <c>ProcessingAttempt</c>.</summary>
public readonly record struct AttemptId(Guid Value) : ITypedId
{
    public static AttemptId From(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}

/// <summary>Identifies one <c>ReviewDecision</c>.</summary>
public readonly record struct ReviewId(Guid Value) : ITypedId
{
    public static ReviewId From(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}

/// <summary>Identifies one <c>PrintOutput</c>.</summary>
public readonly record struct PrintOutputId(Guid Value) : ITypedId
{
    public static PrintOutputId From(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}

/// <summary>Identifies one <c>InputSnapshot</c>.</summary>
public readonly record struct SnapshotId(Guid Value) : ITypedId
{
    public static SnapshotId From(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}
