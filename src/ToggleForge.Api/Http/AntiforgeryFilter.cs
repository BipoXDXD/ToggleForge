using Microsoft.AspNetCore.Antiforgery;

namespace ToggleForge.Api.Http;

/// <summary>
/// O middleware de antiforgery só valida endpoints de formulário; as mutações JSON do console validam
/// aqui, antes do handler (STRIDE S2).
/// </summary>
internal sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public const string HeaderName = "X-XSRF-TOKEN";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (await antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            return await next(context);
        }

        var errors = new InputErrors();
        errors.InHeader(HeaderName, "Missing or invalid antiforgery token. Fetch a new one and retry.", ErrorTypes.InvalidToken);
        return errors.ToResult();
    }
}
