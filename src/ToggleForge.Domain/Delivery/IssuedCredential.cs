namespace ToggleForge.Domain.Delivery;

public sealed record IssuedCredential(Credential Credential, CredentialSecret Secret);
