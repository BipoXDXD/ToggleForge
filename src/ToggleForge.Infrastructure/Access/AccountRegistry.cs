using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ToggleForge.Application.Access;
using ToggleForge.Domain;
using ToggleForge.Domain.Access;

namespace ToggleForge.Infrastructure.Access;

internal sealed class AccountRegistry(AccessDbContext context, UserManager<IdentityUser<Guid>> users) : IAccountRegistry
{
    private static readonly string[] _duplicateCodes = ["DuplicateEmail", "DuplicateUserName"];

    public async Task<Result<Guid>> RegisterAsync(AccountRegistration registration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var member = new IdentityUser<Guid> { Id = Guid.NewGuid(), UserName = registration.Email, Email = registration.Email };

        IdentityResult created;
        try
        {
            created = await users.CreateAsync(member, registration.Password);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        })
        {
            return AccessFailures.EmailTaken;
        }

        if (!created.Succeeded)
        {
            return created.Errors.Any(error => _duplicateCodes.Contains(error.Code))
                ? AccessFailures.EmailTaken
                : throw new InvalidOperationException(
                    "The API accepted an account Identity rejects: " + string.Join(", ", created.Errors.Select(e => e.Code)));
        }

        context.Organizations.Add(Organization.Found(registration.OrganizationName, member.Id));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return member.Id;
    }
}
