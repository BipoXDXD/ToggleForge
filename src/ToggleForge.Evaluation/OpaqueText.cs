using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Regra comum de ID de jogador e seed, que entram no hash do rollout sem normalização: 1 a N code units
    /// UTF-16 bem formados, nenhum caractere de controle e pelo menos um caractere que não seja espaço.
    /// O oráculo em tools/rollout-vectors implementa a mesma regra.
    /// </summary>
    internal static class OpaqueText
    {
        internal static bool IsValid([NotNullWhen(true)] string? value, int maxLength) =>
            value is not null
            && value.Length > 0
            && value.Length <= maxLength
            && !value.All(char.IsWhiteSpace)
            && !value.Any(char.IsControl)
            && IsWellFormedUtf16(value);

        /// <summary>
        /// Surrogate solto não tem codificação UTF-8: viraria U+FFFD no hash e valores diferentes colidiriam.
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
