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
        organization.MapGet("/projects", ListAsync).WithName("listProjects");
        organization.MapPost("/projects", CreateAsync).WithName("createProject").AddEndpointFilter<AntiforgeryFilter>();
        organization.MapGet("/projects/{projectKey}", GetAsync).WithName("getProject");
        organization.MapPost("/projects/{projectKey}/environments", CreateEnvironmentAsync)
            .WithName("createEnvironment")
            .AddEndpointFilter<AntiforgeryFilter>();
        organization.MapGet("/projects/{projectKey}/environments/{environmentKey}", GetEnvironmentAsync).WithName("getEnvironment");
    }

    private static async Task<Results<Ok<ProjectListResponse>, JsonHttpResult<ProblemBody>>> ListAsync(
        string organizationId,
        HttpContext context,
        ProjectService projects,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        if (!RouteInput.TryOrganization(organizationId, errors, out var organization))
        {
            return errors.ToResult();
        }

        if (!(await projects.ListAsync(context.User.MemberId(), organization, cancellationToken)).TryGetValue(out var listed, out var failure))
        {
            return Problems.From(failure);
        }

        CacheControl.Private(context.Response);
        return TypedResults.Ok(new ProjectListResponse(listed.Select(ProjectResponse.Of).ToList()));
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

    private static async Task<Results<Created<EnvironmentResponse>, JsonHttpResult<ProblemBody>>> CreateEnvironmentAsync(
        string organizationId,
        string projectKey,
        EnvironmentCreationRequest request,
        HttpContext context,
        ProjectService projects,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        RouteInput.TryOrganization(organizationId, errors, out var organization);
        var project = RouteInput.Project(projectKey, errors);
        var key = errors.Parse<EnvironmentKey>(request.Key, "/key", EnvironmentKey.TryParse, InputDetails.Key);
        var name = errors.Parse<DisplayName>(request.Name, "/name", DisplayName.TryParse, InputDetails.DisplayName);
        if (errors.Any || project is null || key is null || name is null)
        {
            return errors.ToResult();
        }

        var created = await projects.CreateEnvironmentAsync(context.User.MemberId(), organization, project, key, name, cancellationToken);
        return created.TryGetValue(out var environment, out var failure)
            ? TypedResults.Created(
                ResourcePaths.Environment(new EnvironmentLocator(organization, project, key)), EnvironmentResponse.Of(environment))
            : Problems.From(failure);
    }

    private static async Task<Results<Ok<EnvironmentResponse>, JsonHttpResult<ProblemBody>>> GetEnvironmentAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        HttpContext context,
        ProjectService projects,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        if (!RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator))
        {
            return errors.ToResult();
        }

        if (!(await projects.GetEnvironmentAsync(context.User.MemberId(), locator, cancellationToken)).TryGetValue(out var environment, out var failure))
        {
            return Problems.From(failure);
        }

        CacheControl.Private(context.Response);
        return TypedResults.Ok(EnvironmentResponse.Of(environment));
    }
}
