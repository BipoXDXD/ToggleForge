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

/// <remarks>O console só mostra o texto; <c>source</c> fica de fora até a interface marcar o campo com erro.</remarks>
public sealed record ProblemError(string Detail);
