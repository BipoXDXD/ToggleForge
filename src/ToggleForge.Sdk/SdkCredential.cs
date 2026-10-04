using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ToggleForge.Sdk
{
    /// <summary>
    /// A credencial do ambiente embutida no build: <c>tfsdk_</c> seguido de 43 caracteres base64url. Ela é
    /// extraível do jogo, mas ainda assim nunca vai para log nem para mensagem de erro (ADR 0010):
    /// <see cref="ToString"/> a esconde.
    /// </summary>
    public sealed class SdkCredential
    {
        private const string Prefix = "tfsdk_";
        private const int SecretLength = 43;

        private SdkCredential(string value) => Value = value;

        internal string Value { get; }

        public static bool TryParse(string? value, [NotNullWhen(true)] out SdkCredential? credential)
        {
            credential = IsWellFormed(value) ? new SdkCredential(value) : null;
            return credential is not null;
        }

        /// <exception cref="ArgumentException">A mensagem descreve o formato, sem repetir o valor recebido.</exception>
        public static SdkCredential Parse(string value) =>
            TryParse(value, out var credential)
                ? credential
                : throw new ArgumentException(
                    $"An environment credential is '{Prefix}' followed by {SecretLength} base64url characters.", nameof(value));

        public override string ToString() => Prefix + "***";

        private static bool IsWellFormed([NotNullWhen(true)] string? value) =>
            value is not null
            && value.Length == Prefix.Length + SecretLength
            && value.StartsWith(Prefix, StringComparison.Ordinal)
            && value.Skip(Prefix.Length).All(IsBase64Url);

        private static bool IsBase64Url(char character) =>
            character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
    }
}
