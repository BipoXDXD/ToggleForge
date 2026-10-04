using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using ToggleForge.Api.Endpoints;
using ToggleForge.Api.Http;

namespace ToggleForge.Api;

internal static class ApiPipeline
{
    public static WebApplication UseToggleForgeApi(this WebApplication app)
    {
        // Antes de tudo: o rate limit, os cookies e os logs precisam do IP e do esquema do cliente, não do proxy.
        app.UseForwardedHeaders();
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = UnexpectedErrors.HandleAsync,
            // UnexpectedErrors registra o 500 com a referência devolvida ao cliente; o log padrão duplicaria.
            SuppressDiagnosticsCallback = _ => true,
        });
        app.UseStatusCodePages(context => EmptyErrorAsProblem(context.HttpContext));
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        var api = app.MapGroup("/api/v1");
        api.MapSessionEndpoints();

        var organization = api.MapGroup("/organizations/{organizationId}");
        organization.MapProjectEndpoints();

        var environment = organization.MapGroup("/projects/{projectKey}/environments/{environmentKey}");
        environment.MapDraftEndpoints();
        environment.MapFlagEndpoints();
        environment.MapPublicationEndpoints();
        environment.MapSimulationEndpoints();
        environment.MapCredentialEndpoints();

        var sdk = app.MapGroup("/sdk/v1").RequireAuthorization(ApiServices.SdkPolicy);
        sdk.MapSnapshotEndpoints();
        return app;
    }

    /// <summary>Rota inexistente ou método errado também respondem em Problem Details.</summary>
    private static Task EmptyErrorAsProblem(HttpContext context)
    {
        var status = context.Response.StatusCode;
        if (status == StatusCodes.Status415UnsupportedMediaType)
        {
            return UnsupportedContentType().ExecuteAsync(context);
        }

        return Problems.Json(new ProblemBody("about:blank", ReasonPhrases.GetReasonPhrase(status), status)).ExecuteAsync(context);
    }

    /// <summary>O roteamento recusa o Content-Type antes do endpoint; no contrato, toda entrada inválida é 400 (ADR 0004).</summary>
    private static JsonHttpResult<ProblemBody> UnsupportedContentType()
    {
        var errors = new InputErrors();
        errors.InHeader(HeaderNames.ContentType, "Send the body as application/json.");
        return errors.ToResult();
    }
}
