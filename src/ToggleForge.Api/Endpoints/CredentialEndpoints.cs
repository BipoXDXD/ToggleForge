using Microsoft.AspNetCore.Http.HttpResults;
using ToggleForge.Api.Authentication;
using ToggleForge.Api.Contracts;
using ToggleForge.Api.Http;
using ToggleForge.Application.Delivery;
using ToggleForge.Domain;

namespace ToggleForge.Api.Endpoints;

internal static class CredentialEndpoints
{
    public static void MapCredentialEndpoints(this RouteGroupBuilder environment)
    {
        environment.MapGet("/credentials", ListAsync).WithName("listCredentials");
        environment.MapPost("/credentials", CreateAsync).WithName("createCredential").AddEndpointFilter<AntiforgeryFilter>();
        environment.MapPost("/credentials/{credentialId}/revocation", RevokeAsync)
            .WithName("revokeCredential")
            .AddEndpointFilter<AntiforgeryFilter>();
    }

    private static async Task<Results<Ok<CredentialListResponse>, JsonHttpResult<ProblemBody>>> ListAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        HttpContext context,
        CredentialService credentials,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        if (!RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator))
        {
            return errors.ToResult();
        }

        if (!(await credentials.ListAsync(context.User.MemberId(), locator, cancellationToken)).TryGetValue(out var listed, out var failure))
        {
            return Problems.From(failure);
        }

        CacheControl.Private(context.Response);
        return TypedResults.Ok(new CredentialListResponse(listed.Select(CredentialResponse.Of).ToList()));
    }

    private static async Task<Results<Ok<CredentialResponse>, JsonHttpResult<ProblemBody>>> RevokeAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        string credentialId,
        HttpContext context,
        CredentialService credentials,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator);
        var id = RouteInput.Credential(credentialId, errors);
        if (errors.Any || locator is null || id is null)
        {
            return errors.ToResult();
        }

        var revoked = await credentials.RevokeAsync(context.User.MemberId(), locator, id.Value, cancellationToken);
        return revoked.TryGetValue(out var credential, out var failure)
            ? TypedResults.Ok(CredentialResponse.Of(credential))
            : Problems.From(failure);
    }

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
