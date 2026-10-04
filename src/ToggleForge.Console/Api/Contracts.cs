namespace ToggleForge.Console.Api;

// Só os campos que o console usa de cada schema de docs/api/openapi.yaml (ADR 0009). Campo novo na API é
// ignorado, e o contrato, não um assembly compartilhado, é o que liga os dois lados.

public sealed record Session(Member Member, IReadOnlyList<Membership> Organizations);

public sealed record Member(Guid Id, string Email);

/// <param name="Role">A lista de papéis pode crescer; papel desconhecido não ganha permissão nenhuma.</param>
public sealed record Membership(Guid OrganizationId, string Name, string Role)
{
    public bool CanCreateProjects => Role == "owner";

    public bool CanEditDrafts => Role is "owner" or "editor";
}

public sealed record ProjectList(IReadOnlyList<Project> Data);

public sealed record Project(string Key, string Name, IReadOnlyList<ProjectEnvironment> Environments);

public sealed record ProjectEnvironment(string Key, string Name, long? CurrentVersion, bool HasUnpublishedChanges);

public sealed record Draft(long Revision, IReadOnlyList<Flag> Flags);

public sealed record Flag(string Key, bool Enabled, int RolloutBasisPoints);

public sealed record Credentials(string Email, string Password);

public sealed record ProjectCreation(string Key, string Name);

public sealed record FlagCreation(string Key, bool Enabled, bool OnValue, bool OffValue, int RolloutBasisPoints);

/// <summary>Um recurso com a ETag que a API devolveu junto; é o valor do If-Match ao alterá-lo.</summary>
public sealed record Versioned<T>(T Value, string? ETag);

internal sealed record AntiforgeryToken(string Token);
