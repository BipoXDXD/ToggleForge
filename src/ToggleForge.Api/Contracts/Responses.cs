using System.Text.Json.Serialization;
using ToggleForge.Application.Access;
using ToggleForge.Application.Authoring;
using ToggleForge.Domain.Authoring;
using ToggleForge.Domain.Delivery;
using ToggleForge.Evaluation;

namespace ToggleForge.Api.Contracts;

internal sealed record AntiforgeryTokenResponse(string Token);

internal sealed record MemberResponse(Guid Id, string Email);

internal sealed record MembershipResponse(Guid OrganizationId, string Name, string Role)
{
    public static MembershipResponse Of(MembershipView view) =>
        new(view.OrganizationId, view.OrganizationName.Value, view.Role.ToString().ToLowerInvariant());
}

internal sealed record SessionResponse(MemberResponse Member, IReadOnlyList<MembershipResponse> Organizations);

internal sealed record EnvironmentResponse(
    string Key, string Name, long? CurrentVersion, long DraftRevision, bool HasUnpublishedChanges)
{
    public static EnvironmentResponse Of(EnvironmentView view) =>
        new(view.Key.Value, view.Name.Value, view.CurrentVersion, view.DraftRevision, view.HasUnpublishedChanges);
}

internal sealed record ProjectResponse(string Key, string Name, IReadOnlyList<EnvironmentResponse> Environments)
{
    public static ProjectResponse Of(ProjectView view) =>
        new(view.Key.Value, view.Name.Value, view.Environments.Select(EnvironmentResponse.Of).ToList());
}

internal sealed record ProjectListResponse(IReadOnlyList<ProjectResponse> Data);

internal sealed record FlagResponse(string Key, bool Enabled, bool OnValue, bool OffValue, int RolloutBasisPoints)
{
    public static FlagResponse Of(FlagKey key, FlagSettings settings) =>
        new(key.Value, settings.IsEnabled, settings.OnValue, settings.OffValue, settings.Rollout.BasisPoints);
}

internal sealed record DraftChangeResponse(string FlagKey, string Change);

internal sealed record DraftResponse(
    long Revision, long? BasedOnVersion, IReadOnlyList<FlagResponse> Flags, IReadOnlyList<DraftChangeResponse> Changes)
{
    public static DraftResponse Of(DraftView view) =>
        new(
            view.Revision,
            view.BasedOnVersion,
            view.Flags.Select(flag => FlagResponse.Of(flag.Key, flag.Settings)).ToList(),
            view.Changes.Select(change => new DraftChangeResponse(change.FlagKey.Value, change.Kind.ToString().ToLowerInvariant())).ToList());
}

internal sealed record PublicationResponse(
    long Version,
    DateTimeOffset PublishedAt,
    MemberResponse PublishedBy,
    string Origin,
    long? RestoredVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Summary,
    IReadOnlyList<FlagResponse> Flags)
{
    public static PublicationResponse Of(PublicationView view) =>
        new(
            view.Version,
            view.PublishedAt,
            new MemberResponse(view.PublishedBy.Id, view.PublishedBy.Email),
            view.Origin.ToString().ToLowerInvariant(),
            view.RestoredVersion,
            view.Summary?.Value,
            view.Flags.Select(flag => FlagResponse.Of(flag.Key, flag.Settings)).ToList());
}

internal sealed record PublicationSummaryResponse(
    long Version,
    DateTimeOffset PublishedAt,
    MemberResponse PublishedBy,
    string Origin,
    long? RestoredVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Summary)
{
    public static PublicationSummaryResponse Of(PublicationSummaryView view) =>
        new(
            view.Version,
            view.PublishedAt,
            new MemberResponse(view.PublishedBy.Id, view.PublishedBy.Email),
            view.Origin.ToString().ToLowerInvariant(),
            view.RestoredVersion,
            view.Summary?.Value);
}

internal sealed record PublicationListResponse(IReadOnlyList<PublicationSummaryResponse> Data, PageMetadata Metadata);

internal sealed record PageMetadata(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextPageToken);

internal sealed record CredentialResponse(
    Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt)
{
    /// <summary><c>lastUsedAt</c> fica nulo até o uso pelo SDK ser registrado.</summary>
    public static CredentialResponse Of(Credential credential) =>
        new(credential.Id, credential.Name.Value, credential.Prefix, credential.CreatedAt, LastUsedAt: null, credential.RevokedAt);
}

internal sealed record CredentialListResponse(IReadOnlyList<CredentialResponse> Data);

internal sealed record CreatedCredentialResponse(
    Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt, string Secret)
{
    public static CreatedCredentialResponse Of(IssuedCredential issued) =>
        new(
            issued.Credential.Id,
            issued.Credential.Name.Value,
            issued.Credential.Prefix,
            issued.Credential.CreatedAt,
            LastUsedAt: null,
            issued.Credential.RevokedAt,
            issued.Secret.Value);
}

internal sealed record SnapshotFlagResponse(
    string Key, bool Enabled, bool OnValue, bool OffValue, int RolloutBasisPoints, string Seed);

internal sealed record SnapshotResponse(
    string Algorithm, string ProjectKey, string EnvironmentKey, long Version, DateTimeOffset PublishedAt, IReadOnlyList<SnapshotFlagResponse> Flags)
{
    public static SnapshotResponse Of(SnapshotView view) =>
        new(
            RolloutBucket.AlgorithmTag,
            view.ProjectKey.Value,
            view.EnvironmentKey.Value,
            view.Version,
            view.PublishedAt,
            view.Flags.Select(flag => new SnapshotFlagResponse(
                flag.Key.Value,
                flag.Settings.IsEnabled,
                flag.Settings.OnValue,
                flag.Settings.OffValue,
                flag.Settings.Rollout.BasisPoints,
                flag.Seed.Value)).ToList());
}
