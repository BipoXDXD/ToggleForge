using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Fração dos jogadores incluídos num rollout, em basis points (0 a 10 000; 1 bp = 0,01%).
    /// Inteiros evitam arredondamento na comparação com o bucket do hash.
    /// </summary>
    public sealed record RolloutPercentage
    {
        public const int MaxBasisPoints = 10_000;

        private const int BasisPointsPerPercent = 100;

        private const decimal MaxPercent = 100m;

        private RolloutPercentage(int basisPoints) => BasisPoints = basisPoints;

        public static RolloutPercentage None { get; } = new(0);

        public static RolloutPercentage All { get; } = new(MaxBasisPoints);

        public int BasisPoints { get; }

        public bool IncludesNobody => BasisPoints == 0;

        public bool IncludesEveryone => BasisPoints == MaxBasisPoints;

        public static bool TryFromBasisPoints(int basisPoints, [NotNullWhen(true)] out RolloutPercentage? percentage)
        {
            percentage = basisPoints is >= 0 and <= MaxBasisPoints ? new RolloutPercentage(basisPoints) : null;
            return percentage is not null;
        }

        public static RolloutPercentage FromBasisPoints(int basisPoints) =>
            TryFromBasisPoints(basisPoints, out var percentage)
                ? percentage
                : throw new ArgumentOutOfRangeException(
                    nameof(basisPoints), basisPoints, $"Expected 0 to {MaxBasisPoints} basis points.");

        /// <summary>Converte um percentual com até duas casas decimais (por exemplo, 12,5).</summary>
        public static RolloutPercentage FromPercent(decimal percent)
        {
            // A faixa é checada antes de multiplicar: percentuais enormes estourariam o decimal.
            if (percent < 0 || percent > MaxPercent)
            {
                throw InvalidPercent(percent);
            }

            var basisPoints = percent * BasisPointsPerPercent;
            if (basisPoints != decimal.Truncate(basisPoints))
            {
                throw InvalidPercent(percent);
            }

            return new RolloutPercentage((int)basisPoints);
        }

        /// <summary>Um bucket de 0 a 9999 está incluído quando fica abaixo do percentual.</summary>
        public bool Includes(int bucket) => bucket < BasisPoints;

        public override string ToString() =>
            ((decimal)BasisPoints / BasisPointsPerPercent).ToString("0.##", CultureInfo.InvariantCulture) + "%";

        private static ArgumentOutOfRangeException InvalidPercent(decimal percent) =>
            new(nameof(percent), percent, $"Expected 0 to {MaxPercent} with at most two decimal places.");
    }
}
