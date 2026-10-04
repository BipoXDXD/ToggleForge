using ToggleForge.Evaluation;
using ToggleForge.Sdk;

namespace ToggleForge.SdkDemo;

/// <summary>Como o modo ao vivo lê a flag a cada volta: uma linha por avaliação, que só aparece quando muda.</summary>
internal static class LiveReadings
{
    public static string Boolean(ToggleForgeClient game, string flagKey, EvaluationContext player) =>
        FlagResultText.Describe(flagKey, game.EvaluateBoolean(flagKey, player, defaultValue: false));
}
