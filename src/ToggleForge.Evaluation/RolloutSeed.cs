using System;
using System.Diagnostics.CodeAnalysis;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Semente fixa de uma flag no hash do rollout. Trocar a seed redistribui todos os jogadores da flag.
    /// </summary>
    public sealed record RolloutSeed
    {
        public const int MaxLength = 64;

        private const string InvalidValueMessage =
            "A rollout seed must have 1 to 64 characters, not be blank "
            + "and contain no control characters or unpaired surrogates.";

        private RolloutSeed(string value) => Value = value;

        public string Value { get; }

        public static bool TryParse(string? value, [NotNullWhen(true)] out RolloutSeed? seed)
        {
            seed = OpaqueText.IsValid(value, MaxLength) ? new RolloutSeed(value) : null;
            return seed is not null;
        }

        public static RolloutSeed Parse(string value) =>
            TryParse(value, out var seed)
                ? seed
                : throw new ArgumentException(InvalidValueMessage, nameof(value));

        public override string ToString() => Value;
    }
}
