using ToggleForge.Domain;
using ToggleForge.Domain.Access;

namespace ToggleForge.Application.Access;

public sealed record MembershipView(Guid OrganizationId, DisplayName OrganizationName, Role Role);
