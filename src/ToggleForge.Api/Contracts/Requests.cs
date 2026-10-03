namespace ToggleForge.Api.Contracts;

// Campos anuláveis: "ausente" vira erro MISSING_VALUE em vez de um default silencioso.

internal sealed record AccountCreationRequest(string? Email, string? Password, string? OrganizationName);

internal sealed record CredentialsRequest(string? Email, string? Password);

internal sealed record ProjectCreationRequest(string? Key, string? Name);

internal sealed record FlagCreationRequest(string? Key, bool? Enabled, bool? OnValue, bool? OffValue, int? RolloutBasisPoints);

internal sealed record PublicationRequest(long? DraftRevision, string? Summary);

internal sealed record CredentialCreationRequest(string? Name);
