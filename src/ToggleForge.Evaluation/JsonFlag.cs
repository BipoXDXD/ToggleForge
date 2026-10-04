using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Flag de configuração JSON publicada (ADR 0011). Os valores são texto JSON compacto que o núcleo não lê:
    /// quem criou a flag já o validou, e o parser do jogo o converte para a classe dele.
    /// </summary>
    public sealed class JsonFlag : Flag
    {
        public JsonFlag(FlagKey key, bool isEnabled, string onValue, string offValue, ITargetingRule targeting)
            : base(key, isEnabled, targeting)
        {
            OnValue = onValue ?? throw new ArgumentNullException(nameof(onValue));
            OffValue = offValue ?? throw new ArgumentNullException(nameof(offValue));
        }

        public string OnValue { get; }

        public string OffValue { get; }

        /// <summary>
        /// Avalia a flag fora de um snapshot, como a simulação do rascunho; o resultado sai sem versão.
        /// </summary>
        /// <param name="parse">
        /// Converte o texto JSON escolhido. Não é chamado quando falta o ID do jogador num rollout parcial; se lançar
        /// exceção, volta <paramref name="defaultValue"/> com <see cref="EvaluationReason.InvalidValue"/>. Roda a cada
        /// avaliação: guarde o resultado em vez de avaliar a cada frame.
        /// </param>
        public EvaluationResult<T> Evaluate<T>(
            ProjectKey projectKey, EnvironmentKey environmentKey, EvaluationContext context, T defaultValue, Func<string, T> parse)
        {
            var identity = IdentityIn(projectKey, environmentKey);
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (parse is null)
            {
                throw new ArgumentNullException(nameof(parse));
            }

            return Evaluate(identity, context, defaultValue, parse, snapshotVersion: null);
        }

        internal EvaluationResult<T> Evaluate<T>(
            FlagIdentity identity, EvaluationContext context, T defaultValue, Func<string, T> parse, long? snapshotVersion)
        {
            var choice = Choose(identity, context);
            if (choice.UsesDefault)
            {
                return new EvaluationResult<T>(defaultValue, choice.Reason, snapshotVersion, choice.Bucket);
            }

            var json = choice.Pick(OnValue, OffValue);
            T value;
            try
            {
                value = parse(json);
            }
            catch (Exception)
            {
                // O valor vem de fora do build e o parser é do jogo: uma recusa dele vira fallback, porque a
                // avaliação nunca lança por dado ruim.
                return new EvaluationResult<T>(defaultValue, EvaluationReason.InvalidValue, snapshotVersion, bucket: null);
            }

            return new EvaluationResult<T>(value, choice.Reason, snapshotVersion, choice.Bucket);
        }
    }
}
