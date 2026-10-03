using Microsoft.AspNetCore.Http.HttpResults;
using ToggleForge.Api.Authentication;
using ToggleForge.Api.Contracts;
using ToggleForge.Api.Http;
using ToggleForge.Application.Delivery;
using ToggleForge.Domain;

namespace ToggleForge.Api.Endpoints;

internal static class CredentialEndpoints
{
    public static void MapCredentialEndpoints(this RouteGroupBuilder environment) =>
        environment.MapPost("/credentials", CreateAsync).WithName("createCredential").AddEndpointFilter<AntiforgeryFilter>();

    /// <summary>Sem Location: o contrato não tem leitura de uma credencial, e o segredo só aparece nesta resposta.</summary>
    private static async Task<Results<Created<CreatedCredentialResponse>, JsonHttpResult<ProblemBody>>> CreateAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        CredentialCreationRequest request,
        HttpContext context,
        CredentialService credentials,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator);
        var name = errors.Parse<DisplayName>(request.Name, "/name", DisplayName.TryParse, InputDetails.DisplayName);
        if (errors.Any || locator is null || name is null)
        {
            return errors.ToResult();
        }

        var created = await credentials.CreateAsync(context.User.MemberId(), locator, name, cancellationToken);
        if (!created.TryGetValue(out var issued, out var failure))
        {
            return Problems.From(failure);
        }

        CacheControl.NoStore(context.Response);
        return TypedResults.Created((string?)null, CreatedCredentialResponse.Of(issued));
    }
}
