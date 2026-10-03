using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Dados do jogador informados pelo jogo no momento da avaliação. Não provam identidade: decisões
    /// sensíveis (compras, recompensas) continuam no backend do jogo.
    /// </summary>
    public sealed class EvaluationContext
    {
        private EvaluationContext(PlayerId? playerId) => PlayerId = playerId;

        public static EvaluationContext Anonymous { get; } = new(null);

        public PlayerId? PlayerId { get; }

        public static EvaluationContext ForPlayer(PlayerId playerId) =>
            new(playerId ?? throw new ArgumentNullException(nameof(playerId)));
    }
}
