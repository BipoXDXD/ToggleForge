using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Versão publicada e imutável da configuração de um ambiente: é o que os jogos baixam e avaliam.
    /// </summary>
    public sealed class ConfigurationSnapshot
    {
        private readonly Dictionary<FlagKey, BooleanFlag> _flags;

        public ConfigurationSnapshot(
            ProjectKey projectKey, EnvironmentKey environmentKey, long version, IEnumerable<BooleanFlag> flags)
        {
            if (version < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(version), version, "Published versions start at 1.");
            }

            ProjectKey = projectKey ?? throw new ArgumentNullException(nameof(projectKey));
            EnvironmentKey = environmentKey ?? throw new ArgumentNullException(nameof(environmentKey));
            Version = version;
            _flags = IndexByKey(flags ?? throw new ArgumentNullException(nameof(flags)));
        }

        public ProjectKey ProjectKey { get; }

        public EnvironmentKey EnvironmentKey { get; }

        public long Version { get; }

        public bool TryGetFlag(FlagKey key, [NotNullWhen(true)] out BooleanFlag? flag) =>
            _flags.TryGetValue(key, out flag);

        private static Dictionary<FlagKey, BooleanFlag> IndexByKey(IEnumerable<BooleanFlag> flags)
        {
            var index = new Dictionary<FlagKey, BooleanFlag>();
            foreach (var flag in flags)
            {
                if (flag is null)
                {
                    throw new ArgumentException("The flag list must not contain null.", nameof(flags));
                }

                if (index.ContainsKey(flag.Key))
                {
                    throw new ArgumentException($"Flag key '{flag.Key}' appears more than once.", nameof(flags));
                }

                index.Add(flag.Key, flag);
            }

            return index;
        }
    }
}
