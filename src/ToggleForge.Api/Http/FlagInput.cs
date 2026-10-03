using ToggleForge.Evaluation;

namespace ToggleForge.Api.Http;

internal static class FlagInput
{
    public const string RolloutPointer = "/rolloutBasisPoints";

    public static RolloutPercentage? Rollout(int basisPoints, InputErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (RolloutPercentage.TryFromBasisPoints(basisPoints, out var rollout))
        {
            return rollout;
        }

        errors.InBody(RolloutPointer, "Use 0 to 10000 basis points (100 = 1%).");
        return null;
    }
}
