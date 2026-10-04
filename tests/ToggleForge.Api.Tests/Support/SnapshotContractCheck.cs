namespace ToggleForge.Api.Tests.Support;

/// <summary>
/// Confere cada resposta que o SDK recebe contra <c>getSnapshot</c> no contrato, como o <see cref="GameClient"/> faz,
/// e devolve o corpo intacto ao SDK.
/// </summary>
public sealed class SnapshotContractCheck : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
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
