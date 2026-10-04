using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>Os problemas que os formulários de flag explicam com texto do console, iguais na criação e na edição.</summary>
internal static class FormProblems
{
    public const string JsonBudgetReachedType = "/problems/json-budget-reached";

    public const string FlagTypeLockedType = "/problems/flag-type-locked";

    public static readonly Problem FixMarkedFields = new(
        "/problems/invalid-fields", "Invalid fields", 400, "Nothing was saved: fix the fields marked below.");

    // O texto da API diz os números; o console explica o porquê e o que fazer (design spec, 6).
    public static readonly Problem OutOfJsonRoom = new(
        JsonBudgetReachedType, "This draft is out of room for JSON values.", 409,
        "All JSON values of an environment add up to at most 128 KiB, because every player downloads all of them. " +
        "Make a value smaller or remove a JSON flag you no longer use, then save again. Nothing was saved.");

    /// <summary>
    /// A chave já foi publicada neste ambiente com o outro tipo, e uma chave publicada guarda o tipo (ADR 0011). Só
    /// existem dois tipos, então o publicado é o que não foi enviado; o texto é do console porque o erro fica no campo.
    /// </summary>
    /// <param name="sentType">O tipo do pedido recusado.</param>
    public static string KeyPublishedWithAnotherType(string sentType)
    {
        var (published, sent) = FlagTypes.IsJson(sentType) ? ("Boolean", "JSON") : ("JSON", "Boolean");
        return $"This key was published as a {published} flag in this environment, and a published key keeps its type. Use a new key for a {sent} flag.";
    }
}
