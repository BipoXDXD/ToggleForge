using System.Text.Json;
using ToggleForge.Api.Http;

namespace ToggleForge.Api.Contracts;

// Campos anuláveis: "ausente" vira erro MISSING_VALUE em vez de um default silencioso.

internal sealed record AccountCreationRequest(string? Email, string? Password, string? OrganizationName);

internal sealed record CredentialsRequest(string? Email, string? Password);

internal sealed record ProjectCreationRequest(string? Key, string? Name);

internal sealed record EnvironmentCreationRequest(string? Key, string? Name);

/// <summary>
/// <see cref="JsonElement"/> não anulável distingue o campo ausente (<see cref="JsonValueKind.Undefined"/>) do
/// <c>null</c>, que é um valor válido numa flag JSON e um tipo inválido.
/// </summary>
internal sealed record FlagCreationRequest(
    string? Key, JsonElement Type, bool? Enabled, JsonElement OnValue, JsonElement OffValue, int? RolloutBasisPoints);

internal sealed record FlagModificationRequest(
    PatchField<bool> Enabled, PatchField<JsonElement> OnValue, PatchField<JsonElement> OffValue, PatchField<int> RolloutBasisPoints)
{
    public bool IsEmpty => !(Enabled.IsPresent || OnValue.IsPresent || OffValue.IsPresent || RolloutBasisPoints.IsPresent);
}

internal sealed record PublicationRequest(long? DraftRevision, string? Summary);

internal sealed record SimulationRequest(string? PlayerId, string? Source, long? Version);

internal sealed record RollbackRequest(long? TargetVersion, long? ExpectedCurrentVersion, bool? DiscardDraftChanges);

internal sealed record CredentialCreationRequest(string? Name);
