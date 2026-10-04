using ToggleForge.Evaluation;
using ToggleForge.Sdk;

namespace ToggleForge.SdkDemo;

/// <summary>Como o modo ao vivo lê a flag a cada volta: uma linha por avaliação, que só aparece quando muda.</summary>
internal static class LiveReadings
{
    public static string Boolean(ToggleForgeClient game, string flagKey, EvaluationContext player) =>
        FlagResultText.Describe(flagKey, game.EvaluateBoolean(flagKey, player, defaultValue: false));

    /// <remarks>Mostra o texto JSON como chega ao parser do jogo; <c>null</c> é o valor padrão da demonstração.</remarks>
    public static string Json(ToggleForgeClient game, string flagKey, EvaluationContext player)
    {
        var result = game.EvaluateJson(flagKey, player, defaultValue: "null", json => json);
        return FlagResultText.Describe(flagKey, result, result.Value);
    }
}
