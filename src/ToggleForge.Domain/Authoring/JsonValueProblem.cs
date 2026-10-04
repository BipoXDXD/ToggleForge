namespace ToggleForge.Domain.Authoring;

/// <summary>Por que um texto não serve como valor de flag JSON (ADR 0011).</summary>
public enum JsonValueProblem
{
    NotJson,

    /// <summary>Uma chave se repete no mesmo objeto; o Newtonsoft e o JsonUtility a resolveriam diferente.</summary>
    DuplicateKey,

    TooDeep,

    /// <summary>Um número não cabe num <see cref="double"/>, e cada parser o trataria de um jeito.</summary>
    NumberOutOfRange,

    TooLarge,
}
