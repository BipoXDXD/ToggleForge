using System.Net;

namespace ToggleForge.Console.Api;

/// <summary>Problem Details (RFC 9457) como a API devolve, ou montado pelo console quando a resposta nem chega.</summary>
public sealed record Problem(string Type, string Title, int Status, string? Detail = null, IReadOnlyList<ProblemError>? Errors = null)
{
    public static Problem Unreachable { get; } = new(
        "/problems/unreachable", "Could not reach ToggleForge", 0, "Check your connection and try again.");

    public static Problem UnexpectedResponse(HttpStatusCode status) => new(
        "/problems/unexpected-response", "Unexpected response from ToggleForge", (int)status, "Try again in a moment.");

    public HttpStatusCode StatusCode => (HttpStatusCode)Status;

    /// <summary>O que mostrar ao membro: a API escreve <c>detail</c> e cada erro para quem usa o console.</summary>
    public IReadOnlyList<string> Messages =>
        Errors is { Count: > 0 } errors
            ? errors.Select(error => error.Detail).ToList()
            : [Detail ?? Title];
}

/// <param name="Source">Onde está o erro; o <c>pointer</c> leva a mensagem para o campo certo do formulário.</param>
public sealed record ProblemError(string Detail, ProblemSource? Source = null);

/// <param name="Pointer">JSON Pointer no corpo do pedido, como <c>/onValue</c>; nulo para erro fora do corpo.</param>
#pragma warning disable CA1720 // "Pointer" é o JSON Pointer (RFC 6901) do campo source.pointer do contrato, não um ponteiro de memória.
public sealed record ProblemSource(string? Pointer = null);
#pragma warning restore CA1720
