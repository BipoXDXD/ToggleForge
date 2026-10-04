using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ToggleForge.Domain;
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Infrastructure.Persistence;

/// <summary>
/// O banco também é entrada não confiável (SA cap. 3): um valor gravado que não passa no parse é dado
/// corrompido e falha alto, em vez de virar um objeto inválido. A exceção vai inteira para o log de erros
/// inesperados, então a mensagem nunca traz o valor gravado, que pode ser a configuração de um estúdio.
/// </summary>
internal static class ValueConverters
{
    public static ValueConverter<ProjectKey, string> ProjectKey { get; } =
        new(key => key.Value, value => ParseProjectKey(value));

    public static ValueConverter<EnvironmentKey, string> EnvironmentKey { get; } =
        new(key => key.Value, value => ParseEnvironmentKey(value));

    public static ValueConverter<FlagKey, string> FlagKey { get; } =
        new(key => key.Value, value => ParseFlagKey(value));

    public static ValueConverter<RolloutSeed, string> RolloutSeed { get; } =
        new(seed => seed.Value, value => ParseRolloutSeed(value));

    public static ValueConverter<RolloutPercentage, int> RolloutPercentage { get; } =
        new(percentage => percentage.BasisPoints, value => ParseRolloutPercentage(value));

    public static ValueConverter<DisplayName, string> DisplayName { get; } =
        new(name => name.Value, value => ParseDisplayName(value));

    public static ValueConverter<ChangeSummary, string> ChangeSummary { get; } =
        new(summary => summary.Value, value => ParseChangeSummary(value));

    public static ValueConverter<FlagType, string> FlagType { get; } =
        new(type => FlagTypeText(type), text => ParseFlagType(text));

    public static ValueConverter<JsonFlagValue, string> JsonFlagValue { get; } =
        new(value => value.Text, text => ParseJsonFlagValue(text));

    public static ChangeSummary ParseChangeSummary(string value) =>
        Domain.Authoring.ChangeSummary.TryParse(value, out var summary) ? summary : throw Corrupted(nameof(ChangeSummary), value);

    public static PublicationOrigin ParsePublicationOrigin(string value) =>
        Enum.TryParse<PublicationOrigin>(value, ignoreCase: true, out var origin) ? origin : throw Corrupted(nameof(PublicationOrigin), value);

    public static FlagKey ParseFlagKey(string value) =>
        Evaluation.FlagKey.TryParse(value, out var key) ? key : throw Corrupted(nameof(FlagKey), value);

    public static RolloutSeed ParseRolloutSeed(string value) =>
        Evaluation.RolloutSeed.TryParse(value, out var seed) ? seed : throw Corrupted(nameof(RolloutSeed), value);

    public static RolloutPercentage ParseRolloutPercentage(int value) =>
        Evaluation.RolloutPercentage.TryFromBasisPoints(value, out var percentage)
            ? percentage
            : throw new InvalidDataException($"Stored {nameof(RolloutPercentage)} is out of range.");

    private static ProjectKey ParseProjectKey(string value) =>
        Evaluation.ProjectKey.TryParse(value, out var key) ? key : throw Corrupted(nameof(ProjectKey), value);

    private static EnvironmentKey ParseEnvironmentKey(string value) =>
        Evaluation.EnvironmentKey.TryParse(value, out var key) ? key : throw Corrupted(nameof(EnvironmentKey), value);

    private static DisplayName ParseDisplayName(string value) =>
        Domain.DisplayName.TryParse(value, out var name) ? name : throw Corrupted(nameof(DisplayName), value);

    private static string FlagTypeText(FlagType type) => type switch
    {
        Domain.Authoring.FlagType.Boolean => "boolean",
        Domain.Authoring.FlagType.Json => "json",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown flag type."),
    };

    private static FlagType ParseFlagType(string text) => text switch
    {
        "boolean" => Domain.Authoring.FlagType.Boolean,
        "json" => Domain.Authoring.FlagType.Json,
        _ => throw Corrupted(nameof(FlagType), text),
    };

    private static JsonFlagValue ParseJsonFlagValue(string text) =>
        Domain.Authoring.JsonFlagValue.TryParse(text, out var value, out _) ? value : throw Corrupted(nameof(JsonFlagValue), text);

    /// <summary>Só o tipo e o tamanho: o valor gravado fica fora da mensagem (ADR 0011, I8).</summary>
    private static InvalidDataException Corrupted(string type, string value) =>
        new($"Stored {type} is invalid ({Encoding.UTF8.GetByteCount(value)} bytes in UTF-8).");
}
