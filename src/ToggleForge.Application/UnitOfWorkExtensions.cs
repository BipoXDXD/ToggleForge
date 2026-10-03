namespace ToggleForge.Application;

public static class UnitOfWorkExtensions
{
    /// <summary>Para gravações sem disputa esperada: um conflito aqui é defeito, não regra de negócio.</summary>
    public static async Task SaveOrFailAsync(this IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        var outcome = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (outcome != SaveOutcome.Saved)
        {
            throw new InvalidOperationException($"Unexpected save outcome {outcome}.");
        }
    }
}
