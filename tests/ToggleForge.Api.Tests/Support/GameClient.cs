using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ToggleForge.Api.Tests.Support;

/// <summary>O jogo baixando o snapshot, sem sessão do console; cada resposta é conferida contra o contrato.</summary>
public static class GameClient
{
    public static Task<ApiResponse> GetSnapshotAsync(WebApplicationFactory<Program> api, string secret, Action<HttpRequestMessage>? configure = null) =>
        SendAsync(api, request =>
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            configure?.Invoke(request);
        });

    /// <summary>Sem credencial pronta, para os casos de header ausente ou malformado.</summary>
    public static async Task<ApiResponse> SendAsync(WebApplicationFactory<Program> api, Action<HttpRequestMessage> configure)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(configure);

        using var game = api.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = ApiFactory.BaseAddress, AllowAutoRedirect = false, HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, Paths.Snapshot);
        configure(request);
        var response = await game.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        ApiContract.Verify("getSnapshot", response, body);
        return new ApiResponse(response.StatusCode, response, body);
    }
}
