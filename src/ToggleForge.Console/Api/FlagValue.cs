using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToggleForge.Console.Api;

/// <summary>
/// O valor de uma flag como a API o troca (schema <c>FlagValue</c>): <c>true</c> ou <c>false</c> numa flag booleana,
/// qualquer valor JSON numa flag JSON. Guarda o texto compacto, com as chaves e os números como vieram, então dois
/// valores são iguais quando o texto compacto é igual. O JSON <c>null</c> é um valor (<see cref="Null"/>); a
/// ausência do campo é a referência nula.
/// </summary>
[JsonConverter(typeof(FlagValueConverter))]
public sealed record FlagValue
{
    // A profundidade é regra da API (8 níveis, ADR 0011), e é ela que devolve o erro de profundidade. Com o padrão de
    // 64 do leitor, o cliente recusaria esse valor por conta própria, como se fosse erro de sintaxe.
    private static readonly JsonDocumentOptions _reading = new() { MaxDepth = 512 };

    // Acentos e <, >, & ficam como foram escritos, como a API os guarda. A página nunca põe este texto em HTML sem
    // codificação: o Blazor codifica toda expressão, e nenhum valor passa por MarkupString.
    private static readonly JsonWriterOptions _compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // NewLine fixo: o texto vai para áreas de texto e testes, e o padrão do sistema mudaria entre Windows e Linux.
    private static readonly JsonWriterOptions _indented = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
        NewLine = "\n",
    };

    private FlagValue(string text) => Text = text;

    public static FlagValue True { get; } = new("true");

    public static FlagValue False { get; } = new("false");

    /// <summary>O JSON <c>null</c>: vale como valor de uma flag JSON, mas só na criação (contrato, <c>updateFlag</c>).</summary>
    public static FlagValue Null { get; } = new("null");

    /// <summary>O JSON compacto.</summary>
    public string Text { get; }

    public bool IsNull => Text == "null";

    public static FlagValue Of(bool value) => value ? True : False;

    /// <summary>
    /// Confere só a sintaxe: um valor JSON, sem comentário nem vírgula sobrando. Tamanho, profundidade, chave repetida
    /// e número fora do <see cref="double"/> são regra da API.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out FlagValue? value)
    {
        value = null;
        if (text is null)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text, _reading);
            return TryCompact(document.RootElement, out value);
        }
        catch (JsonException)
        {
            // Texto que não é JSON é exatamente o que este método recusa.
            return false;
        }
        catch (ArgumentException)
        {
            // Um surrogate isolado não se converte para UTF-8, e o leitor avisa com esta exceção; também não é JSON.
            return false;
        }
    }

    private static bool TryCompact(JsonElement element, [NotNullWhen(true)] out FlagValue? value)
    {
        try
        {
            value = new FlagValue(Write(element, _compact));
            return true;
        }
        catch (InvalidOperationException)
        {
            // Um escape \uD800 sem par passa pelo leitor, mas compactar exige desescapá-lo; não é texto que a API aceite.
            value = null;
            return false;
        }
    }

    /// <summary>Para editar e ler: dois espaços por nível, um item por linha.</summary>
    public string Indented()
    {
        using var document = JsonDocument.Parse(Text, _reading);
        return Write(document.RootElement, _indented);
    }

    /// <summary>O começo do texto compacto, para caber numa célula de tabela.</summary>
    /// <param name="maxLength">Em caracteres, contando a reticência; pelo menos 2.</param>
    /// <remarks>
    /// Não corta um par de surrogates ao meio porque o texto não tem nenhum: o encoder escapa o que fica fora do BMP
    /// (um emoji vira <c>\uXXXX\uXXXX</c>).
    /// Também não corta um escape ao meio: o corte recua para antes dele, então a prévia pode ficar mais curta.
    /// </remarks>
    public string Preview(int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 2);
        return Text.Length <= maxLength ? Text : string.Concat(Text.AsSpan(0, CutBeforeEscape(maxLength - 1)), "…");
    }

    // No texto compacto, toda barra começa um escape: \uXXXX tem 6 caracteres, os outros (\", \\, \n...) têm 2.
    private int CutBeforeEscape(int cut)
    {
        const int UnicodeEscapeLength = 6;
        const int ShortEscapeLength = 2;

        var index = 0;
        while (index < cut)
        {
            if (Text[index] != '\\')
            {
                index++;
                continue;
            }

            var length = Text[index + 1] == 'u' ? UnicodeEscapeLength : ShortEscapeLength;
            if (index + length > cut)
            {
                return index;
            }

            index += length;
        }

        return cut;
    }

    internal static FlagValue Read(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return new FlagValue(Write(document.RootElement, _compact));
    }

    private static string Write(JsonElement element, JsonWriterOptions options)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}

/// <summary>Lê qualquer valor JSON e o escreve de volta como JSON, nunca como string com JSON dentro.</summary>
internal sealed class FlagValueConverter : JsonConverter<FlagValue>
{
    // Sem isto, o JSON null viraria a referência nula, que no console quer dizer "campo ausente".
    public override bool HandleNull => true;

    public override FlagValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        FlagValue.Read(ref reader);

    // O texto já é JSON compacto conferido por FlagValue; validar de novo só gastaria tempo.
    public override void Write(Utf8JsonWriter writer, FlagValue value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.Text, skipInputValidation: true);
}
