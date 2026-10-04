using ToggleForge.Evaluation;
using ToggleForge.Sdk;

namespace ToggleForge.SdkDemo;

/// <summary>As leituras do modo ao vivo: cada leitura avalia a flag para o jogador e a descreve numa linha.</summary>
internal static class LiveReadings
{
    public static string Boolean(ToggleForgeClient game, string flagKey, EvaluationContext player) =>
        FlagResultText.Describe(flagKey, game.EvaluateBoolean(flagKey, player, defaultValue: false));

    /// <remarks>
    /// Mostra o texto JSON como chega ao parser do jogo; <c>null</c> é o valor padrão da demonstração.
    /// </remarks>
    public static string Json(ToggleForgeClient game, string flagKey, EvaluationContext player)
    {
        var result = game.EvaluateJson(flagKey, player, defaultValue: "null", json => json);
        return FlagResultText.Describe(flagKey, result, result.Value);
    }
}
