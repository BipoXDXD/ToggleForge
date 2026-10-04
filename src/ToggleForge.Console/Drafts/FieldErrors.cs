using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>
/// Os erros de um formulário de flag, cada um no campo a que se refere, pelo JSON Pointer que a API usa no corpo.
/// O console confere o que dá antes de enviar, e a API, o resto; as duas mensagens caem no mesmo lugar.
/// </summary>
public sealed class FieldErrors
{
    /// <summary>Só a criação manda a chave; a edição nunca recebe erro nela.</summary>
    public const string Key = "/key";

    public const string OnValue = "/onValue";

    public const string OffValue = "/offValue";

    public const string Rollout = "/rolloutBasisPoints";

    private static readonly string[] _fields = [Key, OnValue, OffValue, Rollout];

    private readonly Dictionary<string, string> _byField = new(StringComparer.Ordinal);

    public bool IsEmpty => _byField.Count == 0;

    /// <returns>A mensagem do campo, ou nula se ele está certo.</returns>
    public string? For(string field) => _byField.GetValueOrDefault(field);

    /// <remarks>Só a primeira mensagem de cada campo fica: uma de cada vez basta para corrigir.</remarks>
    internal void Add(string field, string message) => _byField.TryAdd(field, message);

    /// <summary>O membro mexeu no campo: a mensagem antiga já não diz respeito ao que está escrito.</summary>
    internal void Clear(string field) => _byField.Remove(field);

    /// <summary>
    /// Separa do problema da API os erros que apontam para um campo do formulário ou para dentro dele (como
    /// <c>/onValue/a</c>). O resto, como um erro em <c>/type</c>, continua no problema, para a mensagem geral.
    /// </summary>
    /// <returns>Os erros por campo e o problema sem eles; nulo quando não sobrou nada para a mensagem geral.</returns>
    public static (FieldErrors Fields, Problem? General) Split(Problem problem)
    {
        var fields = new FieldErrors();
        var rest = new List<ProblemError>();
        foreach (var error in problem.Errors ?? [])
        {
            if (FieldOf(error.Source?.Pointer) is { } field)
            {
                fields.Add(field, error.Detail);
            }
            else
            {
                rest.Add(error);
            }
        }

        if (fields.IsEmpty)
        {
            return (fields, problem);
        }

        return (fields, rest.Count == 0 ? null : problem with { Errors = rest });
    }

    private static string? FieldOf(string? pointer) =>
        pointer is null
            ? null
            : _fields.FirstOrDefault(field => pointer == field || pointer.StartsWith(field + "/", StringComparison.Ordinal));
}
