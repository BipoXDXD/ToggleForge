using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Qual valor da flag vale para um jogador, e por quê. Cada tipo de flag aplica a escolha aos próprios
    /// valores, então a regra (inativa → desligado; senão o targeting decide) existe num lugar só.
    /// </summary>
    internal readonly struct FlagChoice
    {
        private readonly Variant _variant;

        private FlagChoice(Variant variant, EvaluationReason reason, int? bucket)
        {
            _variant = variant;
            Reason = reason;
            Bucket = bucket;
        }

        public EvaluationReason Reason { get; }

        public int? Bucket { get; }

        /// <summary>Nenhum valor da flag vale: o jogo recebe o próprio valor padrão.</summary>
        public bool UsesDefault => _variant == Variant.Default;

        public static FlagChoice On(int? bucket) => new FlagChoice(Variant.On, EvaluationReason.Included, bucket);

        public static FlagChoice Off(EvaluationReason reason, int? bucket) => new FlagChoice(Variant.Off, reason, bucket);

        public static FlagChoice Default(EvaluationReason reason) => new FlagChoice(Variant.Default, reason, bucket: null);

        /// <exception cref="InvalidOperationException">A escolha é o valor padrão; confira <see cref="UsesDefault"/> antes.</exception>
        public T Pick<T>(T onValue, T offValue) => _variant switch
        {
            Variant.On => onValue,
            Variant.Off => offValue,
            _ => throw new InvalidOperationException("A default choice has no flag value to pick."),
        };

        private enum Variant
        {
            On,
            Off,
            Default,
        }
    }
}
