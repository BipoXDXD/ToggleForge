namespace ToggleForge.Api.Http;

internal static class CacheControl
{
    /// <summary>Segredo ou dado pessoal: nada guarda a resposta.</summary>
    public static void NoStore(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers.CacheControl = "no-store";
    }

    /// <summary>Só o próprio cliente guarda, revalidando com a ETag.</summary>
    public static void Private(HttpResponse response, string? etag = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers.CacheControl = "private, no-cache";
        if (etag is not null)
        {
            response.Headers.ETag = etag;
        }
    }

    public static string ETagOf(long revision) => $"\"{revision}\"";
}
