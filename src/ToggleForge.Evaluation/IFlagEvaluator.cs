namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Avalia flags contra um snapshot. Nunca lança exceção por causa de configuração ausente ou inválida:
    /// devolve o valor padrão com o motivo. Decorators (contagem, logs, métricas) implementam esta interface.
    /// </summary>
    public interface IFlagEvaluator
    {
        EvaluationResult<bool> EvaluateBoolean(
            ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, bool defaultValue);
    }
}
