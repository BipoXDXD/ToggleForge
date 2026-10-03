using System;

namespace ToggleForge.Evaluation
{
    public sealed class FlagIdentity
    {
        public FlagIdentity(ProjectKey projectKey, EnvironmentKey environmentKey, FlagKey flagKey)
        {
            ProjectKey = projectKey ?? throw new ArgumentNullException(nameof(projectKey));
            EnvironmentKey = environmentKey ?? throw new ArgumentNullException(nameof(environmentKey));
            FlagKey = flagKey ?? throw new ArgumentNullException(nameof(flagKey));
        }

        public ProjectKey ProjectKey { get; }

        public EnvironmentKey EnvironmentKey { get; }

        public FlagKey FlagKey { get; }
    }
}
