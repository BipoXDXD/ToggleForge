using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ToggleForge.Domain;
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Infrastructure.Persistence;

/// <summary>
/// O banco também é entrada não confiável (SA cap. 3): um valor gravado que não passa no parse é dado
/// corrompido e falha alto, em vez de virar um objeto inválido.
/// </summary>
internal static class ValueConverters
{
    public static ValueConverter<ProjectKey, string> ProjectKey { get; } =
        new(key => key.Value, value => Evaluation.ProjectKey.Parse(value));

    public static ValueConverter<EnvironmentKey, string> EnvironmentKey { get; } =
        new(key => key.Value, value => Evaluation.EnvironmentKey.Parse(value));

    public static ValueConverter<FlagKey, string> FlagKey { get; } =
        new(key => key.Value, value => Evaluation.FlagKey.Parse(value));

    public static ValueConverter<RolloutSeed, string> RolloutSeed { get; } =
        new(seed => seed.Value, value => Evaluation.RolloutSeed.Parse(value));

    public static ValueConverter<RolloutPercentage, int> RolloutPercentage { get; } =
        new(percentage => percentage.BasisPoints, value => Evaluation.RolloutPercentage.FromBasisPoints(value));

    public static ValueConverter<DisplayName, string> DisplayName { get; } =
        new(name => name.Value, value => ParseDisplayName(value));

    public static ValueConverter<ChangeSummary, string> ChangeSummary { get; } =
        new(summary => summary.Value, value => ParseChangeSummary(value));

    private static DisplayName ParseDisplayName(string value) =>
        Domain.DisplayName.TryParse(value, out var name) ? name : throw Corrupted(nameof(DisplayName), value);

    public static ChangeSummary ParseChangeSummary(string value) =>
        Domain.Authoring.ChangeSummary.TryParse(value, out var summary) ? summary : throw Corrupted(nameof(ChangeSummary), value);

    private static InvalidDataException Corrupted(string type, string value) =>
        new($"Stored {type} '{value}' is invalid.");
}
