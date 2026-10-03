using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

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
            playerId = IsValid(value) ? new PlayerId(value) : null;
            return playerId is not null;
        }

        public static PlayerId Parse(string value) =>
            TryParse(value, out var playerId)
                ? playerId
                : throw new ArgumentException(InvalidValueMessage, nameof(value));

        public override string ToString() => Value;

        private static bool IsValid([NotNullWhen(true)] string? value) =>
            value is not null
            && value.Length is > 0 and <= MaxLength
            && !value.All(char.IsWhiteSpace)
            && !value.Any(char.IsControl)
            && IsWellFormedUtf16(value);

        /// <summary>
        /// Surrogate solto não tem codificação UTF-8: viraria U+FFFD no hash e IDs diferentes colidiriam.
        /// </summary>
        private static bool IsWellFormedUtf16(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                if (char.IsLowSurrogate(value[index]))
                {
                    return false;
                }

                if (char.IsHighSurrogate(value[index]))
                {
                    var hasLowSurrogate = index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]);
                    if (!hasLowSurrogate)
                    {
                        return false;
                    }

                    index++;
                }
            }

            return true;
        }
    }
}
