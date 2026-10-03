using System;
using System.Diagnostics.CodeAnalysis;

namespace ToggleForge.Evaluation
{
    /// <summary>Chave de um ambiente (por exemplo, prod ou staging), única por projeto.</summary>
    public sealed record EnvironmentKey
    {
        private EnvironmentKey(string value) => Value = value;

        public string Value { get; }

        public static bool TryParse(string? value, [NotNullWhen(true)] out EnvironmentKey? key)
        {
            key = KeyFormat.IsValid(value) ? new EnvironmentKey(value) : null;
            return key is not null;
        }

        public static EnvironmentKey Parse(string value) =>
            TryParse(value, out var key) ? key : throw new ArgumentException(KeyFormat.Description, nameof(value));

        public override string ToString() => Value;
    }
}
