using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Formato comum das chaves de projeto, ambiente e flag: 1 a 64 caracteres em [a-z0-9._-],
    /// começando por letra ou dígito. Chaves entram em URLs e no hash do rollout, por isso o
    /// alfabeto é restrito e sem ambiguidade de caixa.
    /// </summary>
    internal static class KeyFormat
    {
        internal const int MaxLength = 64;

        internal static readonly string InvalidKeyMessage =
            $"A key must have 1 to {MaxLength} characters from [a-z0-9._-] and start with a letter or digit.";

        internal static bool IsValid([NotNullWhen(true)] string? value) =>
            value is not null
            && value.Length > 0
            && value.Length <= MaxLength
            && IsLowercaseLetterOrDigit(value[0])
            && value.All(IsKeyCharacter);

        private static bool IsKeyCharacter(char character) =>
            IsLowercaseLetterOrDigit(character) || IsSeparator(character);

        private static bool IsLowercaseLetterOrDigit(char character) =>
            character is (>= 'a' and <= 'z') or (>= '0' and <= '9');

        private static bool IsSeparator(char character) => character is '-' or '.' or '_';
    }
}
