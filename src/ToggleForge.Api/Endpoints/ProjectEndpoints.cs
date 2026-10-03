using Microsoft.AspNetCore.Http.HttpResults;
using ToggleForge.Api.Authentication;
using ToggleForge.Api.Contracts;
using ToggleForge.Api.Http;
using ToggleForge.Application.Authoring;
using ToggleForge.Domain;
using ToggleForge.Evaluation;

namespace ToggleForge.Api.Endpoints;

internal static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this RouteGroupBuilder organization)
    {
        organization.MapPost("/projects", CreateAsync).WithName("createProject").AddEndpointFilter<AntiforgeryFilter>();
        organization.MapGet("/projects/{projectKey}", GetAsync).WithName("getProject");
    }

    private static async Task<Results<Created<ProjectResponse>, JsonHttpResult<ProblemBody>>> CreateAsync(
        string organizationId,
        ProjectCreationRequest request,
        HttpContext context,
        ProjectService projects,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        RouteInput.TryOrganization(organizationId, errors, out var organization);
        var key = errors.Parse<ProjectKey>(request.Key, "/key", ProjectKey.TryParse, InputDetails.Key);
        var name = errors.Parse<DisplayName>(request.Name, "/name", DisplayName.TryParse, InputDetails.DisplayName);
        if (errors.Any || key is null || name is null)
        {
            return errors.ToResult();
        }

        var created = await projects.CreateAsync(context.User.MemberId(), organization, key, name, cancellationToken);
        return created.TryGetValue(out var project, out var failure)
            ? TypedResults.Created(ResourcePaths.Project(organization, key), ProjectResponse.Of(project))
            : Problems.From(failure);
    }

    private static async Task<Results<Ok<ProjectResponse>, JsonHttpResult<ProblemBody>>> GetAsync(
        string organizationId,
        string projectKey,
        HttpContext context,
        ProjectService projects,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        RouteInput.TryOrganization(organizationId, errors, out var organization);
        var key = RouteInput.Project(projectKey, errors);
        if (errors.Any || key is null)
        {
            return errors.ToResult();
        }

        var found = await projects.GetAsync(context.User.MemberId(), organization, key, cancellationToken);
        if (!found.TryGetValue(out var project, out var failure))
        {
            return Problems.From(failure);
        }

        CacheControl.Private(context.Response);
        return TypedResults.Ok(ProjectResponse.Of(project));
    }
}
