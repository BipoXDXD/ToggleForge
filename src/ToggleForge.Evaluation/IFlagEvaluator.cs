using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Nunca lança exceção por causa de configuração ausente ou inválida: devolve o valor padrão com o motivo.
    /// </summary>
    public interface IFlagEvaluator
    {
        EvaluationResult<bool> EvaluateBoolean(
            ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, bool defaultValue);

        /// <param name="parse">
        /// Converte o texto JSON da flag. Não é chamado quando o resultado é o valor padrão; se lançar exceção,
        /// volta <paramref name="defaultValue"/> com <see cref="EvaluationReason.InvalidValue"/>.
        /// </param>
        EvaluationResult<T> EvaluateJson<T>(
            ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, T defaultValue, Func<string, T> parse);
    }
}
