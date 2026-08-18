namespace PrintFlow.Domain.Results;

/// <summary>
/// The single value of a type carrying no information; lets an operation that returns
/// nothing still flow through <see cref="OperationResult{T}"/>.
/// </summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;

    public override string ToString() => "()";
}
