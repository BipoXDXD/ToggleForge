using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>
/// O que um formulário de flag mostra depois de tentar salvar, igual na criação e na edição: o erro de cada campo, o
/// resumo acima do botão e o aviso de corpo grande demais ao lado dos valores JSON.
/// </summary>
internal sealed class FlagFormState
{
    // A ordem dos campos nas duas telas: o foco vai para o primeiro que tiver erro.
    private static readonly string[] _pageOrder = [FieldErrors.Key, FieldErrors.Rollout, FieldErrors.OnValue, FieldErrors.OffValue];

    public FieldErrors Fields { get; private set; } = new();

    /// <summary>O resumo do formulário (<c>role="alert"</c>); nulo quando não há o que dizer.</summary>
    public Problem? Problem { get; private set; }

    /// <summary>A API recusou o corpo pelo tamanho (413), e só os valores fazem o corpo crescer.</summary>
    public bool ValuesTooLarge { get; private set; }

    /// <summary>O título do console para o teto JSON do rascunho; os outros problemas não têm.</summary>
    public string? Heading => Problem == FormProblems.OutOfJsonRoom ? FormProblems.OutOfJsonRoom.Title : null;

    /// <summary>O primeiro campo com erro, na ordem da página; nulo quando nenhum tem.</summary>
    public string? FirstMarkedField => _pageOrder.FirstOrDefault(marked => Fields.For(marked) is not null);

    /// <summary>Uma nova tentativa de salvar começa sem nada da anterior.</summary>
    public void Reset() => (Fields, Problem, ValuesTooLarge) = (new FieldErrors(), null, false);

    /// <summary>Cada erro fica sob o seu campo, e o resumo avisa que há campos a corrigir (design spec, 2.2 e 3).</summary>
    /// <param name="general">O que a API disse fora dos campos; quando existe, ele é o resumo.</param>
    public void Mark(FieldErrors fields, Problem? general = null)
    {
        Fields = fields;
        Problem = general ?? (fields.IsEmpty ? null : FormProblems.FixMarkedFields);
    }

    public void Show(Problem problem) => Problem = problem;

    /// <summary>Uma ação que não salva os campos, como remover a flag: os erros deles continuam valendo.</summary>
    public void ClearSummary() => Problem = null;

    public void MarkValuesTooLarge() => ValuesTooLarge = true;

    /// <summary>
    /// O tipo da flag mudou: os erros dos valores JSON e o 413 foram dados ao tipo anterior, e as áreas nem estão mais na
    /// tela quando ele vira booleano. O erro da chave e o do rollout continuam.
    /// </summary>
    public void ClearValues()
    {
        Clear(FieldErrors.OnValue);
        Clear(FieldErrors.OffValue);
        ValuesTooLarge = false;
    }

    /// <summary>
    /// O membro mexeu no campo: a mensagem antiga já não diz respeito a ele. O resumo fala dos campos marcados; quando o
    /// último deixa de estar, ele já não diz respeito ao formulário.
    /// </summary>
    public void Clear(string field)
    {
        Fields.Clear(field);
        if (Fields.IsEmpty && Problem == FormProblems.FixMarkedFields)
        {
            Problem = null;
        }
    }
}
