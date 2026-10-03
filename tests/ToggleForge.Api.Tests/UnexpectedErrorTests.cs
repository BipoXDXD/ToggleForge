using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ToggleForge.Api.Tests.Support;
using ToggleForge.Application.Access;

namespace ToggleForge.Api.Tests;

/// <summary>STRIDE I3: a falha inesperada chega ao cliente só como uma referência opaca; o detalhe fica no log.</summary>
public sealed class UnexpectedErrorTests(ApiFactory api)
{
    private static readonly string _canary = $"CANARY-{Guid.NewGuid()}";

    [Fact]
    public async Task An_unexpected_error_hides_its_details_and_logs_them_under_the_reference()
    {
        await using var broken = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddScoped<IMembershipDirectory, BrokenMembershipDirectory>()));
        using var client = ConsoleClient.Anonymous(broken);

        var response = await client.SendAsync("createAccount", HttpMethod.Post, Paths.Accounts, new
        {
            email = $"member-{Guid.NewGuid():N}@toggleforge.test",
            password = ConsoleClient.Password,
            organizationName = "Studio",
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        foreach (var leak in new[] { _canary, "Exception", " at ", "Npgsql", "ToggleForge", "SELECT" })
        {
            Assert.DoesNotContain(leak, response.Body, StringComparison.Ordinal);
        }

        var reference = response.Json["instance"]!.GetValue<string>()["/unexpected-errors/".Length..];
        Assert.Contains(api.Logs.Entries, entry => entry.Contains(reference, StringComparison.Ordinal) && entry.Contains(_canary, StringComparison.Ordinal));
    }

    private sealed class BrokenMembershipDirectory : IMembershipDirectory
    {
        public Task<Domain.Access.Role?> FindRoleAsync(Guid memberId, Guid organizationId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(_canary + " Npgsql: SELECT secret FROM somewhere");

        public Task<IReadOnlyList<MembershipView>> ListAsync(Guid memberId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(_canary + " Npgsql: SELECT secret FROM somewhere");

        public Task<IReadOnlyDictionary<Guid, string>> FindEmailsAsync(IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(_canary + " Npgsql: SELECT secret FROM somewhere");
    }
}
