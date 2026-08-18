namespace PrintFlow.Domain.Results;

/// <summary>
/// Success carrying a value, or a structured <see cref="OperationFailure"/>.
/// </summary>
/// <remarks>
/// Deliberately small and hand-written (Epic 11100 plan §9.3). Exceptions remain reserved
/// for programmer error; every module seam converts infrastructure faults into a failure
/// result at its own boundary so callers cannot forget to handle them.
/// </remarks>
public readonly struct OperationResult<T>
{
    private readonly T? _value;
    private readonly OperationFailure? _failure;

    private OperationResult(T value)
    {
        _value = value;
        _failure = null;
    }

    private OperationResult(OperationFailure failure)
    {
        _value = default;
        _failure = failure;
    }

    public bool IsSuccess => _failure is null;

    public bool IsFailure => _failure is not null;

    /// <summary>The success value. Throws when the result is a failure — check first.</summary>
    public T Value => _failure is null
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot read the value of a failed result ({_failure.Code}: {_failure.TechnicalDetail}).");

    /// <summary>The failure. Throws when the result is a success — check first.</summary>
    public OperationFailure Failure => _failure
        ?? throw new InvalidOperationException("Cannot read the failure of a successful result.");

    public static OperationResult<T> Success(T value) => new(value);

    public static OperationResult<T> Failed(OperationFailure failure) => new(failure);

    public static OperationResult<T> Failed(
        FailureCode code,
        string technicalDetail,
        bool isRetryable = false) =>
        new(OperationFailure.Create(code, technicalDetail, isRetryable));

    public static implicit operator OperationResult<T>(T value) => Success(value);

    public static implicit operator OperationResult<T>(OperationFailure failure) => Failed(failure);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<OperationFailure, TOut> onFailure) =>
        _failure is null ? onSuccess(_value!) : onFailure(_failure);

    public OperationResult<TOut> Map<TOut>(Func<T, TOut> selector) =>
        _failure is null
            ? OperationResult<TOut>.Success(selector(_value!))
            : OperationResult<TOut>.Failed(_failure);

    public bool TryGetValue(out T value)
    {
        value = _value!;
        return _failure is null;
    }

    public override string ToString() =>
        _failure is null ? $"Success({_value})" : $"Failure({_failure})";
}

/// <summary>Factory helpers so call sites can write <c>OperationResult.Ok()</c>.</summary>
public static class OperationResult
{
    public static OperationResult<Unit> Ok() => OperationResult<Unit>.Success(Unit.Value);

    public static OperationResult<T> Ok<T>(T value) => OperationResult<T>.Success(value);

    public static OperationResult<T> Fail<T>(OperationFailure failure) =>
        OperationResult<T>.Failed(failure);

    public static OperationResult<T> Fail<T>(
        FailureCode code,
        string technicalDetail,
        bool isRetryable = false) =>
        OperationResult<T>.Failed(OperationFailure.Create(code, technicalDetail, isRetryable));
}
