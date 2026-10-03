using System;
using System.Diagnostics.CodeAnalysis;

namespace ToggleForge.Evaluation
{
    /// <summary>Chave de uma flag, única por ambiente.</summary>
    public sealed record FlagKey
    {
        private FlagKey(string value) => Value = value;

        public string Value { get; }

        public static bool TryParse(string? value, [NotNullWhen(true)] out FlagKey? key)
        {
            key = KeyFormat.IsValid(value) ? new FlagKey(value) : null;
            return key is not null;
        }

        public static FlagKey Parse(string value) =>
            TryParse(value, out var key) ? key : throw new ArgumentException(KeyFormat.Description, nameof(value));

        public override string ToString() => Value;
    }
}
