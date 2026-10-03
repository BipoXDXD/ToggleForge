using System;
using System.Diagnostics.CodeAnalysis;

namespace ToggleForge.Evaluation
{
    /// <summary>Chave de um projeto, única por organização.</summary>
    public sealed record ProjectKey
    {
        private ProjectKey(string value) => Value = value;

        public string Value { get; }

        public static bool TryParse(string? value, [NotNullWhen(true)] out ProjectKey? key)
        {
            key = KeyFormat.IsValid(value) ? new ProjectKey(value) : null;
            return key is not null;
        }

        public static ProjectKey Parse(string value) =>
            TryParse(value, out var key) ? key : throw new ArgumentException(KeyFormat.Description, nameof(value));

        public override string ToString() => Value;
    }
}
