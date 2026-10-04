using System.Text.Json;
using System.Text.Json.Serialization;
using ToggleForge.Api.Http;
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

/// <summary>Um valor de flag como JSON na resposta, nunca como string: o texto compacto, com a ordem das chaves.</summary>
internal static class FlagValueJson
{
    public static JsonElement Of(JsonFlagValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Of(value.Text);
    }

    /// <summary><paramref name="text"/> é o texto JSON de um valor de flag que o domínio já validou.</summary>
    public static JsonElement Of(string text) => JsonElement.Parse(text);
}

internal sealed record FlagResponse(string Key, string Type, bool Enabled, JsonElement OnValue, JsonElement OffValue, int RolloutBasisPoints)
{
    public static FlagResponse Of(FlagKey key, FlagSettings settings) =>
        new(
            key.Value,
            FlagTypes.Name(settings.Type),
            settings.IsEnabled,
            FlagValueJson.Of(settings.Values.OnValue),
            FlagValueJson.Of(settings.Values.OffValue),
            settings.Rollout.BasisPoints);
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
        new(credential.Id, credential.Name.Value, credential.Prefix, credential.CreatedAt, credential.LastUsedAt, credential.RevokedAt);
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
    string Key, string Type, bool Enabled, JsonElement OnValue, JsonElement OffValue, int RolloutBasisPoints, string Seed);

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
                FlagTypes.Name(flag.Settings.Type),
                flag.Settings.IsEnabled,
                FlagValueJson.Of(flag.Settings.Values.OnValue),
                FlagValueJson.Of(flag.Settings.Values.OffValue),
                flag.Settings.Rollout.BasisPoints,
                flag.Seed.Value)).ToList());
}

internal static class SimulationSources
{
    public const string Draft = "draft";
    public const string Publication = "publication";
}

internal sealed record SimulationResultResponse(string FlagKey, string Type, JsonElement Value, string Reason, int? Bucket)
{
    public static SimulationResultResponse Of(SimulationResultView view) =>
        new(view.FlagKey.Value, FlagTypes.Name(view.Type), FlagValueJson.Of(view.Value), ReasonOf(view.Reason), view.Bucket);

    /// <summary>A simulação sempre tem jogador e só avalia flags que existem: os outros motivos não ocorrem.</summary>
    private static string ReasonOf(EvaluationReason reason) =>
        reason switch
        {
            EvaluationReason.Disabled => "disabled",
            EvaluationReason.Included => "included",
            EvaluationReason.Excluded => "excluded",
            _ => throw new InvalidOperationException($"A simulation cannot end with reason '{reason}'."),
        };
}

internal sealed record SimulationResponse(string Source, long? Version, IReadOnlyList<SimulationResultResponse> Results)
{
    public static SimulationResponse Of(SimulationView view) =>
        new(
            view.Version is null ? SimulationSources.Draft : SimulationSources.Publication,
            view.Version,
            view.Results.Select(SimulationResultResponse.Of).ToList());
}
