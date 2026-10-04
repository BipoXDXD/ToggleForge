namespace ToggleForge.Evaluation.Tests;

public sealed class EvaluationReasonsTests
{
    public static TheoryData<EvaluationReason, bool> FallbackByReason => new()
    {
        { EvaluationReason.SnapshotMissing, true },
        { EvaluationReason.InvalidFlagKey, true },
        { EvaluationReason.FlagNotFound, true },
        { EvaluationReason.MissingPlayerId, true },
        { EvaluationReason.Disabled, false },
        { EvaluationReason.Included, false },
        { EvaluationReason.Excluded, false },
        { EvaluationReason.SnapshotExpired, true },
        { EvaluationReason.TypeMismatch, true },
        { EvaluationReason.InvalidValue, true },
    };

    [Theory]
    [MemberData(nameof(FallbackByReason))]
    public void IsFallback_is_true_only_for_the_reasons_that_return_the_default(EvaluationReason reason, bool expected)
    {
        Assert.Equal(expected, reason.IsFallback());
    }

    // Sem esta guarda, um motivo novo no enum passaria sem ninguém decidir se ele é fallback.
    [Fact]
    public void The_fallback_table_covers_every_reason()
    {
        var covered = FallbackByReason.Select(row => row.Data.Item1).OrderBy(reason => reason);

        Assert.Equal(Enum.GetValues<EvaluationReason>().OrderBy(reason => reason), covered);
    }
}
