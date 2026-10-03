using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using ToggleForge.Api.Contracts;
using ToggleForge.Api.Http;
using ToggleForge.Application.Access;
using ToggleForge.Domain;

namespace ToggleForge.Api.Endpoints;

internal static class SessionEndpoints
{
    private const string SessionPath = "/api/v1/session";

    public static void MapSessionEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/antiforgery-token", GetAntiforgeryToken).WithName("getAntiforgeryToken").AllowAnonymous();
        api.MapPost("/accounts", CreateAccountAsync).WithName("createAccount").AllowAnonymous().AddEndpointFilter<AntiforgeryFilter>();
        api.MapGet("/session", GetSessionAsync).WithName("getSession");
        api.MapPost("/session", SignInAsync).WithName("signIn").AllowAnonymous().AddEndpointFilter<AntiforgeryFilter>();
        api.MapDelete("/session", SignOutAsync).WithName("signOut").AddEndpointFilter<AntiforgeryFilter>();
    }

    private static Ok<AntiforgeryTokenResponse> GetAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        // Por cima do "no-cache, no-store" que o antiforgery acabou de escrever: o contrato pede só no-store.
        CacheControl.NoStore(context.Response);
        return TypedResults.Ok(new AntiforgeryTokenResponse(
            tokens.RequestToken ?? throw new InvalidOperationException("Antiforgery issued no request token.")));
    }

    private static async Task<Results<Created<SessionResponse>, JsonHttpResult<ProblemBody>>> CreateAccountAsync(
        AccountCreationRequest request,
        HttpContext context,
        IAccountRegistry accounts,
        UserManager<IdentityUser<Guid>> users,
        SignInManager<IdentityUser<Guid>> signIn,
        IMembershipDirectory memberships,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        var email = AccountInput.Email(request.Email, errors);
        var password = AccountInput.Password(request.Password, AccountInput.MinNewPasswordLength, errors);
        var organizationName = errors.Parse<DisplayName>(
            request.OrganizationName, "/organizationName", DisplayName.TryParse, InputDetails.DisplayName);
        if (email is null || password is null || organizationName is null)
        {
            return errors.ToResult();
        }

        var registered = await accounts.RegisterAsync(new AccountRegistration(email, password, organizationName), cancellationToken);
        if (!registered.TryGetValue(out var memberId, out var failure))
        {
            if (failure == AccessFailures.PasswordBreached)
            {
                errors.InBody("/password", failure.Detail);
                return errors.ToResult();
            }

            return Problems.From(failure);
        }

        var member = await users.FindByIdAsync(memberId.ToString())
            ?? throw new InvalidOperationException($"Member {memberId} vanished right after registration.");
        await signIn.SignInAsync(member, isPersistent: false);
        CacheControl.NoStore(context.Response);
        return TypedResults.Created(SessionPath, await SessionOfAsync(member, memberships, cancellationToken));
    }

    private static async Task<Ok<SessionResponse>> GetSessionAsync(
        HttpContext context,
        UserManager<IdentityUser<Guid>> users,
        IMembershipDirectory memberships,
        CancellationToken cancellationToken)
    {
        var member = await SignedInMemberAsync(context, users);
        CacheControl.NoStore(context.Response);
        return TypedResults.Ok(await SessionOfAsync(member, memberships, cancellationToken));
    }

    /// <summary>E-mail desconhecido e senha errada respondem igual e no mesmo tempo (contrato: signIn).</summary>
    private static async Task<Results<Ok<SessionResponse>, JsonHttpResult<ProblemBody>>> SignInAsync(
        CredentialsRequest request,
        HttpContext context,
        UserManager<IdentityUser<Guid>> users,
        SignInManager<IdentityUser<Guid>> signIn,
        SignInTiming timing,
        IMembershipDirectory memberships,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        var email = AccountInput.Email(request.Email, errors);
        var password = AccountInput.Password(request.Password, minLength: 1, errors);
        if (email is null || password is null)
        {
            return errors.ToResult();
        }

        if (await users.FindByEmailAsync(email) is not { } member)
        {
            timing.SpendPasswordCheck(password);
            return WrongCredentials();
        }

        var result = await signIn.PasswordSignInAsync(member, password, isPersistent: false, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            context.Response.Headers.RetryAfter = RetryAfterSeconds(await users.GetLockoutEndDateAsync(member), clock);
            return Problems.TooManyRequests("Too many failed attempts. Wait and try again.");
        }

        if (!result.Succeeded)
        {
            return WrongCredentials();
        }

        CacheControl.NoStore(context.Response);
        return TypedResults.Ok(await SessionOfAsync(member, memberships, cancellationToken));
    }

    /// <summary>Trocar o security stamp invalida o cookie antigo mesmo que alguém o tenha copiado (STRIDE S2).</summary>
    private static async Task<NoContent> SignOutAsync(
        HttpContext context, UserManager<IdentityUser<Guid>> users, SignInManager<IdentityUser<Guid>> signIn)
    {
        var member = await SignedInMemberAsync(context, users);
        await users.UpdateSecurityStampAsync(member);
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    /// <summary>Só em endpoints autenticados: o validador do security stamp já recusou cookie de conta inexistente.</summary>
    private static async Task<IdentityUser<Guid>> SignedInMemberAsync(HttpContext context, UserManager<IdentityUser<Guid>> users) =>
        await users.GetUserAsync(context.User)
        ?? throw new InvalidOperationException("Authenticated session without an account.");

    private static async Task<SessionResponse> SessionOfAsync(
        IdentityUser<Guid> member, IMembershipDirectory memberships, CancellationToken cancellationToken)
    {
        var organizations = await memberships.ListAsync(member.Id, cancellationToken);
        return new SessionResponse(
            new MemberResponse(member.Id, member.Email ?? throw new InvalidOperationException($"Member {member.Id} has no email.")),
            organizations.Select(MembershipResponse.Of).ToList());
    }

    private static JsonHttpResult<ProblemBody> WrongCredentials() => Problems.Unauthorized("Wrong email or password.");

    private static string RetryAfterSeconds(DateTimeOffset? lockoutEnd, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return RetryAfter.Seconds((lockoutEnd ?? now) - now);
    }
}
