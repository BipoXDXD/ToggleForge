using ToggleForge.Domain;

namespace ToggleForge.Application.Access;

/// <summary>E-mail e senha já passaram pelos limites do contrato; as regras finais são do Identity.</summary>
public sealed record AccountRegistration(string Email, string Password, DisplayName OrganizationName);
