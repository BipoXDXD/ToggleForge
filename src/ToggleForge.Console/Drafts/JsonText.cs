using System.Text;
using System.Text.Json;
using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>
/// O que o console sabe dizer sobre o texto de uma área JSON enquanto o membro digita: vazio, válido (com o tamanho
/// que a API vai contar), inválido (com onde) ou fundo demais para ler.
/// </summary>
/// <remarks>
/// Só sintaxe e tamanho: chave repetida, profundidade acima de 8 e número fora do <see cref="double"/> são regra da
/// API, que devolve o erro no campo (ADR 0011).
/// </remarks>
public static class JsonText
{
    /// <summary>O teto de um valor JSON, em bytes UTF-8 do texto compactado (ADR 0011).</summary>
    public const int MaxValueBytes = 4_096;

    /// <summary>Três quartos do teto: daqui em diante o medidor avisa.</summary>
    public const int NearLimitBytes = 3_072;

    public static JsonAnalysis Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonAnalysis.Empty();
        }

        if (FlagValue.TryParse(text, out var value))
        {
            return new JsonAnalysis.Valid(value, Encoding.UTF8.GetByteCount(value.Text));
        }

        return Locate(text);
    }

    public static MeterLevel LevelOf(int bytes) => bytes switch
    {
        > MaxValueBytes => MeterLevel.OverLimit,
        >= NearLimitBytes => MeterLevel.NearLimit,
        _ => MeterLevel.Comfortable,
    };

    // Uma segunda leitura, só para achar onde o texto quebra: o FlagValue diz se é JSON, não onde deixou de ser. O
    // leitor aceita um nível a mais que o FlagValue para o texto fundo demais aparecer como tal, e não como erro de
    // sintaxe.
    private static JsonAnalysis Locate(string text)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), new JsonReaderOptions { MaxDepth = FlagValue.MaxReadingDepth + 1 });
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject && reader.CurrentDepth >= FlagValue.MaxReadingDepth)
                {
                    return new JsonAnalysis.TooDeep();
                }
            }
        }
        catch (JsonException error)
        {
            return new JsonAnalysis.Invalid(PositionOf(text, error));
        }

        // O leitor aceitou o texto, mas o FlagValue não: um escape de meio caractere (\uD800) que não vira UTF-8.
        return new JsonAnalysis.Invalid(Position: null);
    }

    // O leitor conta linha e coluna a partir de zero, e a coluna em bytes UTF-8; o membro lê caracteres, a partir de um.
    private static SyntaxPosition? PositionOf(string text, JsonException error)
    {
        if (error.LineNumber is not { } lineIndex || error.BytePositionInLine is not { } bytesBefore)
        {
            return null;
        }

        var lines = text.Split('\n');
        var line = lines[(int)Math.Min(lineIndex, lines.Length - 1)].TrimEnd('\r');
        var lineBytes = Encoding.UTF8.GetBytes(line);
        var charsBefore = Encoding.UTF8.GetCharCount(lineBytes, 0, (int)Math.Min(bytesBefore, lineBytes.Length));
        return new SyntaxPosition((int)lineIndex + 1, charsBefore + 1, line);
    }
}

public enum MeterLevel
{
    Comfortable,
    NearLimit,
    OverLimit,
}

/// <summary>O resultado de <see cref="JsonText.Analyze"/>.</summary>
public abstract record JsonAnalysis
{
    private JsonAnalysis()
    {
    }

    public sealed record Empty : JsonAnalysis;

    /// <param name="Value">O valor compacto, como o console o envia.</param>
    /// <param name="ByteCount">Bytes UTF-8 do texto compacto: o que a API compara com o teto.</param>
    public sealed record Valid(FlagValue Value, int ByteCount) : JsonAnalysis
    {
        public MeterLevel Level => JsonText.LevelOf(ByteCount);

        public int BytesOverLimit => ByteCount - JsonText.MaxValueBytes;
    }

    /// <param name="Position">Onde o texto deixou de ser JSON; nulo quando o problema não tem lugar (um escape partido).</param>
    public sealed record Invalid(SyntaxPosition? Position) : JsonAnalysis;

    /// <summary>Mais níveis do que o console lê (<see cref="FlagValue.MaxReadingDepth"/>); a API aceita só 8.</summary>
    public sealed record TooDeep : JsonAnalysis;
}

/// <param name="Line">A partir de 1.</param>
/// <param name="Column">Em caracteres, a partir de 1.</param>
/// <param name="LineText">A linha inteira, sem a quebra.</param>
public sealed record SyntaxPosition(int Line, int Column, string LineText)
{
    /// <summary>Um <c>^</c> sob a coluna; as tabulações da linha ficam, para o <c>^</c> cair no lugar num texto monoespaçado.</summary>
    public string Caret
    {
        get
        {
            var before = LineText.AsSpan(0, Math.Min(Column - 1, LineText.Length));
            var padding = new StringBuilder(before.Length + 1);
            foreach (var character in before)
            {
                padding.Append(character == '\t' ? '\t' : ' ');
            }

            return padding.Append('^').ToString();
        }
    }
}
