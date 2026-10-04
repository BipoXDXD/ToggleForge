using System.Text.Json.Serialization;

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

/// <param name="BasedOnVersion">A publicação atual, com que as mudanças são comparadas; nula antes da primeira.</param>
public sealed record Draft(long Revision, long? BasedOnVersion, IReadOnlyList<Flag> Flags, IReadOnlyList<DraftChange> Changes);

/// <param name="Change"><c>added</c>, <c>modified</c> ou <c>removed</c>.</param>
public sealed record DraftChange(string FlagKey, string Change);

/// <summary>Os tipos de flag do contrato (<c>FlagType</c>). O tipo vem na criação e nunca muda (ADR 0011).</summary>
/// <remarks>
/// O console é servido pela API (ADR 0009) e está sempre na versão dela, então conhece todo tipo do contrato:
/// o que não é JSON é booleano.
/// </remarks>
public static class FlagTypes
{
    public const string Boolean = "boolean";

    public const string Json = "json";
}

/// <param name="Type"><see cref="FlagTypes.Boolean"/> ou <see cref="FlagTypes.Json"/>.</param>
/// <param name="OnValue">O valor de quem cai no rollout.</param>
/// <param name="OffValue">O valor de quem fica fora e de todos quando a flag está desligada.</param>
public sealed record Flag(string Key, string Type, bool Enabled, FlagValue OnValue, FlagValue OffValue, int RolloutBasisPoints)
{
    public bool IsJson => Type == FlagTypes.Json;
}

/// <summary>Um ambiente de um projeto da organização.</summary>
public sealed record EnvironmentAddress(Guid OrganizationId, string ProjectKey, string EnvironmentKey);

/// <summary>Onde uma flag mora: organização, projeto, ambiente e a chave dela no rascunho.</summary>
public sealed record FlagAddress(Guid OrganizationId, string ProjectKey, string EnvironmentKey, string FlagKey);

public sealed record Credentials(string Email, string Password);

public sealed record ProjectCreation(string Key, string Name);

/// <param name="Type">Sempre enviado, embora opcional no contrato: o console não depende do padrão da API.</param>
public sealed record FlagCreation(string Key, string Type, bool Enabled, FlagValue OnValue, FlagValue OffValue, int RolloutBasisPoints);

/// <param name="DraftRevision">A revisão que o membro revisou; se o rascunho mudou depois, a API recusa.</param>
/// <param name="Summary">Opcional; a API recusa texto vazio, então ausente vai como nulo e fica fora do JSON.</param>
public sealed record PublicationRequest(long DraftRevision, string? Summary);

/// <param name="Origin"><c>publication</c> ou <c>rollback</c>; a lista pode crescer.</param>
/// <param name="RestoredVersion">A versão cujo conteúdo um rollback restaurou; nula numa publicação comum.</param>
public sealed record PublicationSummary(
    long Version, DateTimeOffset PublishedAt, Member PublishedBy, string Origin, long? RestoredVersion, string? Summary = null);

/// <param name="Flags">Todas as flags da versão, como os jogos as recebem.</param>
public sealed record Publication(
    long Version,
    DateTimeOffset PublishedAt,
    Member PublishedBy,
    string Origin,
    long? RestoredVersion,
    IReadOnlyList<Flag> Flags,
    string? Summary = null);

/// <param name="Data">Mais nova primeiro.</param>
public sealed record PublicationPage(IReadOnlyList<PublicationSummary> Data, PageMetadata Metadata);

/// <param name="NextPageToken">Opaco; ausente na última página.</param>
public sealed record PageMetadata(string? NextPageToken = null);

/// <param name="ExpectedCurrentVersion">A versão no ar que o membro viu; se outra publicação veio depois, a API recusa.</param>
/// <param name="DiscardDraftChanges">Sem isso, a API recusa reverter por cima de mudanças não publicadas.</param>
public sealed record RollbackRequest(long TargetVersion, long ExpectedCurrentVersion, bool DiscardDraftChanges);

/// <param name="Version">Só ao simular uma publicação: o rascunho não tem versão, e a API recusa o campo.</param>
public sealed record SimulationRequest(string PlayerId, string Source, long? Version)
{
    public static SimulationRequest ForDraft(string playerId) => new(playerId, "draft", Version: null);

    public static SimulationRequest ForPublication(string playerId, long version) => new(playerId, "publication", version);
}

/// <param name="Version">A versão simulada; nula quando foi o rascunho.</param>
/// <param name="Results">Um por flag, em ordem de chave.</param>
public sealed record Simulation(long? Version, IReadOnlyList<SimulationResult> Results);

/// <param name="Reason"><c>disabled</c>, <c>included</c> ou <c>excluded</c>; a lista pode crescer.</param>
/// <param name="Bucket">O bucket do jogador (0 a 9999); nulo quando o resultado não dependeu dele.</param>
public sealed record SimulationResult(string FlagKey, string Type, FlagValue Value, string Reason, int? Bucket)
{
    public bool IsJson => Type == FlagTypes.Json;
}

/// <summary>Merge patch: só os campos que mudaram vão para a API, e campo nulo nem é enviado.</summary>
/// <remarks>O JSON null de um valor é <see cref="FlagValue.Null"/>, não a referência nula; a API o recusa aqui.</remarks>
public sealed record FlagModification(
    bool? Enabled = null, FlagValue? OnValue = null, FlagValue? OffValue = null, int? RolloutBasisPoints = null)
{
    // Não vai no corpo: a API recusa campo que não conhece (proteção contra mass assignment).
    [JsonIgnore]
    public bool IsEmpty => this == new FlagModification();
}

/// <summary>Um recurso com a ETag que a API devolveu junto; é o valor do If-Match ao alterá-lo.</summary>
public sealed record Versioned<T>(T Value, string ETag);

internal sealed record AntiforgeryToken(string Token);
