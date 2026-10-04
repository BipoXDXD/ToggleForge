using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    // A API recusa null num merge patch (nenhum campo da flag pode ser apagado) e resumo vazio na publicação:
    // o que é nulo fica de fora do JSON.
    private static readonly JsonSerializerOptions _omitNulls = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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

    public Task<ApiResult<Versioned<Flag>>> GetFlagAsync(FlagAddress flag, CancellationToken cancellationToken) =>
        ExchangeVersionedAsync<Flag>(new Outgoing(HttpMethod.Get, FlagPath(flag)), cancellationToken);

    /// <param name="eTag">A ETag da última leitura; se a flag mudou desde então, a API responde 412 e nada muda.</param>
    public Task<ApiResult<Versioned<Flag>>> UpdateFlagAsync(
        FlagAddress flag, string eTag, FlagModification changes, CancellationToken cancellationToken) =>
        ExchangeVersionedAsync<Flag>(new Outgoing(HttpMethod.Patch, FlagPath(flag), MergePatch(changes), eTag), cancellationToken);

    /// <param name="eTag">A ETag da última leitura; se a flag mudou desde então, a API responde 412 e nada muda.</param>
    /// <returns>O problema, ou <see langword="null"/> quando a flag saiu do rascunho.</returns>
    public async Task<Problem?> DeleteFlagAsync(FlagAddress flag, string eTag, CancellationToken cancellationToken)
    {
        var result = await SendAsync<NoContent>(new Outgoing(HttpMethod.Delete, FlagPath(flag), IfMatch: eTag), cancellationToken);
        return result.Problem;
    }

    /// <param name="idempotencyKey">
    /// A mesma chave em todo reenvio do mesmo pedido: a API devolve a resposta original em vez de publicar de novo.
    /// </param>
    public Task<ApiResult<Publication>> PublishAsync(
        EnvironmentAddress environment, PublicationRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
        SendAsync<Publication>(
            new Outgoing(
                HttpMethod.Post,
                $"{EnvironmentPath(environment)}/publications",
                JsonContent.Create(request, options: _omitNulls),
                IdempotencyKey: idempotencyKey),
            cancellationToken);

    private static JsonContent Json(object body) => JsonContent.Create(body);

    private static JsonContent MergePatch(FlagModification changes) =>
        JsonContent.Create(changes, new MediaTypeHeaderValue("application/merge-patch+json"), _omitNulls);

    private static string ProjectsPath(Guid organizationId) => $"api/v1/organizations/{organizationId}/projects";

    // As chaves chegam da URL do console, que o membro pode digitar: escapadas, nunca coladas cruas no path.
    private static string ProjectPath(Guid organizationId, string projectKey) =>
        $"{ProjectsPath(organizationId)}/{Uri.EscapeDataString(projectKey)}";

    private static string DraftPath(Guid organizationId, string projectKey, string environmentKey) =>
        $"{ProjectPath(organizationId, projectKey)}/environments/{Uri.EscapeDataString(environmentKey)}/draft";

    private static string EnvironmentPath(EnvironmentAddress environment) =>
        $"{ProjectPath(environment.OrganizationId, environment.ProjectKey)}/environments/{Uri.EscapeDataString(environment.EnvironmentKey)}";

    private static string FlagPath(FlagAddress flag) =>
        $"{DraftPath(flag.OrganizationId, flag.ProjectKey, flag.EnvironmentKey)}/flags/{Uri.EscapeDataString(flag.FlagKey)}";

    private async Task<ApiResult<T>> SendAsync<T>(Outgoing outgoing, CancellationToken cancellationToken)
        where T : class
    {
        var result = await ExchangeAsync<T>(outgoing, cancellationToken);
        return result.Succeeded ? ApiResult.Success(result.Value.Value) : ApiResult.Failure<T>(result.Problem);
    }

    /// <summary>Para recursos que se alteram com If-Match: sem ETag na resposta, não há como alterá-los depois.</summary>
    private async Task<ApiResult<Versioned<T>>> ExchangeVersionedAsync<T>(Outgoing outgoing, CancellationToken cancellationToken)
        where T : class
    {
        var result = await ExchangeAsync<T>(outgoing, cancellationToken);
        if (!result.Succeeded)
        {
            return ApiResult.Failure<Versioned<T>>(result.Problem);
        }

        return result.Value.ETag is { } eTag
            ? ApiResult.Success(new Versioned<T>(result.Value.Value, eTag))
            : ApiResult.Failure<Versioned<T>>(Problem.UnexpectedResponse(HttpStatusCode.OK));
    }

    private async Task<ApiResult<Received<T>>> ExchangeAsync<T>(Outgoing outgoing, CancellationToken cancellationToken)
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
                    return ApiResult.Failure<Received<T>>(token.Problem);
                }

                request.Headers.Add(AntiforgeryHeader, token.Value.Token);
            }

            if (outgoing.IfMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", outgoing.IfMatch);
            }

            if (outgoing.IdempotencyKey is not null)
            {
                request.Headers.Add("Idempotency-Key", outgoing.IdempotencyKey);
            }

            using var response = await http.SendAsync(request, cancellationToken);
            var result = await ReadAsync<T>(response, cancellationToken);
            return result.Succeeded
                ? ApiResult.Success(new Received<T>(result.Value, response.Headers.ETag?.ToString()))
                : ApiResult.Failure<Received<T>>(result.Problem);
        }
        catch (HttpRequestException)
        {
            return ApiResult.Failure<Received<T>>(Problem.Unreachable);
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

    /// <summary>
    /// Um pedido à API. <paramref name="IfMatch"/> é a ETag lida antes de alterar o recurso;
    /// <paramref name="IdempotencyKey"/>, a chave das operações que a API deduplica.
    /// </summary>
    private sealed record Outgoing(
        HttpMethod Method, string Path, HttpContent? Content = null, string? IfMatch = null, string? IdempotencyKey = null);

    /// <summary>A resposta como chegou; a ETag só existe nos recursos que se alteram com If-Match.</summary>
    private sealed record Received<T>(T Value, string? ETag);

    /// <summary>O "corpo" de uma resposta 204; outra operação que receber 204 cai em resposta inesperada.</summary>
    private sealed class NoContent
    {
        public static NoContent Instance { get; } = new();
    }
}
