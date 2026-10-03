using System;
using System.Diagnostics.CodeAnalysis;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Identificador estável do jogador, informado pelo jogo. É opaco: não é normalizado nem aparado,
    /// porque qualquer transformação mudaria o bucket do rollout. Não prova identidade.
    /// </summary>
    public sealed record PlayerId
    {
        public const int MaxLength = 256;

        private const string InvalidValueMessage =
            "A player id must have 1 to 256 characters, not be blank "
            + "and contain no control characters or unpaired surrogates.";

        private PlayerId(string value) => Value = value;

        public string Value { get; }

        public static bool TryParse(string? value, [NotNullWhen(true)] out PlayerId? playerId)
        {
            playerId = OpaqueText.IsValid(value, MaxLength) ? new PlayerId(value) : null;
            return playerId is not null;
        }

        public static PlayerId Parse(string value) =>
            TryParse(value, out var playerId)
                ? playerId
                : throw new ArgumentException(InvalidValueMessage, nameof(value));

        public override string ToString() => Value;
    }
}
