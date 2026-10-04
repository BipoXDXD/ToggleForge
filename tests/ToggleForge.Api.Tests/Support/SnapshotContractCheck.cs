namespace ToggleForge.Api.Tests.Support;

/// <summary>
/// Confere cada resposta que o SDK recebe contra <c>getSnapshot</c> no contrato, como o <see cref="GameClient"/> faz,
/// e devolve o corpo intacto ao SDK.
/// </summary>
public sealed class SnapshotContractCheck : DelegatingHandler
{
    /// <summary>O <c>If-None-Match</c> de cada pedido e o status que voltou, na ordem.</summary>
    public List<(string? IfNoneMatch, System.Net.HttpStatusCode Status)> Exchanges { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        Exchanges.Add((request.Headers.IfNoneMatch.FirstOrDefault()?.ToString(), response.StatusCode));
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        ApiContract.Verify("getSnapshot", response, body);
        var checkedContent = new StringContent(body);
        foreach (var header in response.Content.Headers)
        {
            checkedContent.Headers.Remove(header.Key);
            checkedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = checkedContent;
        return response;
    }
}
