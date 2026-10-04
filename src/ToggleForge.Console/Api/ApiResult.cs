using System.Diagnostics.CodeAnalysis;

namespace ToggleForge.Console.Api;

/// <summary>A resposta de uma operação: o recurso, ou o problema que a API (ou a rede) devolveu.</summary>
public sealed class ApiResult<T>
    where T : class
{
    internal ApiResult(T? value, Problem? problem)
    {
        Value = value;
        Problem = problem;
    }

    public T? Value { get; }

    public Problem? Problem { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Problem))]
    public bool Succeeded => Problem is null;
}

public static class ApiResult
{
    public static ApiResult<T> Success<T>(T value)
        where T : class => new(value, problem: null);

    public static ApiResult<T> Failure<T>(Problem problem)
        where T : class => new(value: null, problem);
}
