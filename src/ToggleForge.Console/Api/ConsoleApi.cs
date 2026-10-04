using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ToggleForge.Console.Session;

namespace ToggleForge.Console.Api;

/// <summary>
/// As operações de <c>/api/v1</c> que o console usa. O navegador manda o cookie de sessão sozinho, porque a
/// API está na mesma origem; aqui só entra o token antiforgery das mutações (ADR 0004). Um 401 em qualquer
/// chamada encerra a sessão local, e o layout leva o membro ao login.
/// </summary>
public sealed class ConsoleApi(HttpClient http, SessionState session)
{
    private const string AntiforgeryHeader = "X-XSRF-TOKEN";

    public Task<ApiResult<Api.Session>> GetSessionAsync(CancellationToken cancellationToken) =>
        SendAsync<Api.Session>(new Outgoing(HttpMethod.Get, "api/v1/session"), cancellationToken);

    public Task<ApiResult<Api.Session>> SignInAsync(Credentials credentials, CancellationToken cancellationToken) =>
        SendAsync<Api.Session>(new Outgoing(HttpMethod.Post, "api/v1/session", Json(credentials)), cancellationToken);

    /// <returns>O problema, ou <see langword="null"/> quando a sessão foi encerrada no servidor.</returns>
    public async Task<Problem?> SignOutAsync(CancellationToken cancellationToken)
    {
        var result = await SendAsync<NoContent>(new Outgoing(HttpMethod.Delete, "api/v1/session"), cancellationToken);
        return result.Problem;
    }

    public async Task<ApiResult<IReadOnlyList<Project>>> ListProjectsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var result = await SendAsync<ProjectList>(new Outgoing(HttpMethod.Get, ProjectsPath(organizationId)), cancellationToken);
        return result.Succeeded
            ? ApiResult.Success<IReadOnlyList<Project>>(result.Value.Data)
            : ApiResult.Failure<IReadOnlyList<Project>>(result.Problem);
    }

    public Task<ApiResult<Project>> CreateProjectAsync(Guid organizationId, ProjectCreation project, CancellationToken cancellationToken) =>
        SendAsync<Project>(new Outgoing(HttpMethod.Post, ProjectsPath(organizationId), Json(project)), cancellationToken);

    public Task<ApiResult<Project>> GetProjectAsync(Guid organizationId, string projectKey, CancellationToken cancellationToken) =>
        SendAsync<Project>(new Outgoing(HttpMethod.Get, ProjectPath(organizationId, projectKey)), cancellationToken);

    public Task<ApiResult<Draft>> GetDraftAsync(
        Guid organizationId, string projectKey, string environmentKey, CancellationToken cancellationToken) =>
        SendAsync<Draft>(new Outgoing(HttpMethod.Get, DraftPath(organizationId, projectKey, environmentKey)), cancellationToken);

    public Task<ApiResult<Flag>> CreateFlagAsync(
        Guid organizationId, string projectKey, string environmentKey, FlagCreation flag, CancellationToken cancellationToken) =>
        SendAsync<Flag>(
            new Outgoing(HttpMethod.Post, $"{DraftPath(organizationId, projectKey, environmentKey)}/flags", Json(flag)), cancellationToken);

    private static JsonContent Json(object body) => JsonContent.Create(body);

    private static string ProjectsPath(Guid organizationId) => $"api/v1/organizations/{organizationId}/projects";

    // As chaves chegam da URL do console, que o membro pode digitar: escapadas, nunca coladas cruas no path.
    private static string ProjectPath(Guid organizationId, string projectKey) =>
        $"{ProjectsPath(organizationId)}/{Uri.EscapeDataString(projectKey)}";

    private static string DraftPath(Guid organizationId, string projectKey, string environmentKey) =>
        $"{ProjectPath(organizationId, projectKey)}/environments/{Uri.EscapeDataString(environmentKey)}/draft";

    private async Task<ApiResult<T>> SendAsync<T>(Outgoing outgoing, CancellationToken cancellationToken)
        where T : class
    {
        var result = await ExchangeAsync<T>(outgoing, cancellationToken);
        return result.Succeeded ? ApiResult.Success(result.Value.Value) : ApiResult.Failure<T>(result.Problem);
    }

    private async Task<ApiResult<Versioned<T>>> ExchangeAsync<T>(Outgoing outgoing, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var request = new HttpRequestMessage(outgoing.Method, outgoing.Path) { Content = outgoing.Content };
            if (outgoing.Method != HttpMethod.Get)
            {
                var token = await AntiforgeryTokenAsync(cancellationToken);
                if (!token.Succeeded)
                {
                    return ApiResult.Failure<Versioned<T>>(token.Problem);
                }

                request.Headers.Add(AntiforgeryHeader, token.Value.Token);
            }

            if (outgoing.IfMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", outgoing.IfMatch);
            }

            using var response = await http.SendAsync(request, cancellationToken);
            var result = await ReadAsync<T>(response, cancellationToken);
            return result.Succeeded
                ? ApiResult.Success(new Versioned<T>(result.Value, response.Headers.ETag?.ToString()))
                : ApiResult.Failure<Versioned<T>>(result.Problem);
        }
        catch (HttpRequestException)
        {
            return ApiResult.Failure<Versioned<T>>(Problem.Unreachable);
        }
    }

    /// <summary>O token é preso à sessão: <see cref="SessionState"/> o esquece ao entrar e ao sair.</summary>
    private async Task<ApiResult<AntiforgeryToken>> AntiforgeryTokenAsync(CancellationToken cancellationToken)
    {
        if (session.AntiforgeryToken is { } cached)
        {
            return ApiResult.Success<AntiforgeryToken>(cached);
        }

        using var response = await http.GetAsync("api/v1/antiforgery-token", cancellationToken);
        var result = await ReadAsync<AntiforgeryToken>(response, cancellationToken);
        if (result.Succeeded)
        {
            session.AntiforgeryToken = result.Value;
        }

        return result;
    }

    private async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            session.End();
        }

        if (response.StatusCode == HttpStatusCode.NoContent && NoContent.Instance is T nothing)
        {
            return ApiResult.Success(nothing);
        }

        try
        {
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
                return value is null
                    ? ApiResult.Failure<T>(Problem.UnexpectedResponse(response.StatusCode))
                    : ApiResult.Success<T>(value);
            }

            var problem = await response.Content.ReadFromJsonAsync<Problem>(cancellationToken);
            return ApiResult.Failure<T>(problem ?? Problem.UnexpectedResponse(response.StatusCode));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // Corpo que não é o JSON do contrato, como a página de erro de um proxy no caminho.
            return ApiResult.Failure<T>(Problem.UnexpectedResponse(response.StatusCode));
        }
    }

    /// <summary>Um pedido à API; <paramref name="IfMatch"/> é a ETag lida antes de alterar o recurso.</summary>
    private sealed record Outgoing(HttpMethod Method, string Path, HttpContent? Content = null, string? IfMatch = null);

    /// <summary>O "corpo" de uma resposta 204; outra operação que receber 204 cai em resposta inesperada.</summary>
    private sealed class NoContent
    {
        public static NoContent Instance { get; } = new();
    }
}
