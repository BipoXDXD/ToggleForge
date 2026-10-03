namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Nunca lança exceção por causa de configuração ausente ou inválida: devolve o valor padrão com o motivo.
    /// </summary>
    public interface IFlagEvaluator
    {
        EvaluationResult<bool> EvaluateBoolean(
            ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, bool defaultValue);
    }
}
