using System.Security.Cryptography;
using ToggleForge.Evaluation;

namespace ToggleForge.Application.Authoring;

internal static class RolloutSeeds
{
    private const int RandomBytes = 16;

    /// <summary>A seed nunca vem do cliente: trocar a seed redistribui todos os jogadores da flag.</summary>
    public static RolloutSeed Generate() => RolloutSeed.Parse(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(RandomBytes)));
}
