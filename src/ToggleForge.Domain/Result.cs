using System.Diagnostics.CodeAnalysis;

namespace ToggleForge.Domain;

public sealed class Result<T>
{
    private readonly T? _value;
    private readonly Failure? _failure;

    private Result(T value) => _value = value;

    private Result(Failure failure) => _failure = failure;

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Failure failure) => new(failure);

    public Result<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return TryGetValue(out var value, out var failure) ? map(value) : failure;
    }

    public bool TryGetValue([MaybeNullWhen(false)] out T value, [NotNullWhen(false)] out Failure? failure)
    {
        value = _value;
        failure = _failure;
        return failure is null;
    }
}
