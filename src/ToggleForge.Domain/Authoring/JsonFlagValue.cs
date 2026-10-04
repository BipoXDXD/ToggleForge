using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ToggleForge.Domain.Authoring;

/// <summary>
/// O valor de uma flag JSON (ADR 0011): um valor JSON qualquer, sem chave repetida, com até <see cref="MaxDepth"/>
/// níveis, números que cabem num <see cref="double"/> e até <see cref="MaxBytes"/> bytes em UTF-8 depois de
/// compactado. Guarda o texto compacto com a ordem das chaves, então espaço em branco não conta como mudança.
/// </summary>
public sealed record JsonFlagValue
{
    public const int MaxBytes = 4 * 1024;

    public const int MaxDepth = 8;

    // As repetições são procuradas depois, para a recusa dizer qual regra quebrou.
    private static readonly JsonDocumentOptions _reading = new() { AllowDuplicateProperties = true };

    // O texto vai para o snapshot e para o console como JSON, nunca embutido em HTML sem codificação; escapar
    // acentos só aumentaria os bytes que contam para o limite.
    private static readonly JsonWriterOptions _compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private JsonFlagValue(string text) => Text = text;

    /// <summary>Os valores de uma flag booleana, na mesma forma dos valores das flags JSON.</summary>
    public static JsonFlagValue True { get; } = new("true");

    public static JsonFlagValue False { get; } = new("false");

    /// <summary>O JSON compacto.</summary>
    public string Text { get; }

    /// <summary>O tamanho de <see cref="Text"/> em UTF-8, que é o que os limites contam.</summary>
    public int ByteCount => Encoding.UTF8.GetByteCount(Text);

    internal bool? AsBoolean => Text switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    /// <param name="problem">Por que o texto foi recusado; só tem sentido quando o retorno é falso.</param>
    public static bool TryParse(string? json, [NotNullWhen(true)] out JsonFlagValue? value, out JsonValueProblem problem)
    {
        value = null;
        problem = JsonValueProblem.NotJson;
        if (json is null || !TryRead(json, out var document))
        {
            return false;
        }

        using (document)
        {
            if (ProblemIn(document.RootElement, depth: 0) is { } found)
            {
                problem = found;
                return false;
            }

            var compact = Compact(document.RootElement);
            if (Encoding.UTF8.GetByteCount(compact) > MaxBytes)
            {
                problem = JsonValueProblem.TooLarge;
                return false;
            }

            value = new JsonFlagValue(compact);
            return true;
        }
    }

    /// <exception cref="ArgumentException">O texto não é um valor de flag JSON válido.</exception>
    public static JsonFlagValue Parse(string json) =>
        TryParse(json, out var value, out var problem)
            ? value
            : throw new ArgumentException($"Not a valid json flag value ({problem}).", nameof(json));

    private static bool TryRead(string json, [NotNullWhen(true)] out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(json, _reading);
            return true;
        }
        catch (JsonException)
        {
            // Texto que não é JSON é exatamente o que este método recusa; o motivo volta como NotJson.
            document = null;
            return false;
        }
    }

    /// <param name="depth">Quantos objetos e listas envolvem <paramref name="element"/>.</param>
    private static JsonValueProblem? ProblemIn(JsonElement element, int depth) => element.ValueKind switch
    {
        JsonValueKind.Object => ProblemInObject(element, depth + 1),
        JsonValueKind.Array => ProblemInArray(element, depth + 1),
        JsonValueKind.Number when !(element.TryGetDouble(out var number) && double.IsFinite(number)) => JsonValueProblem.NumberOutOfRange,
        _ => null,
    };

    private static JsonValueProblem? ProblemInObject(JsonElement element, int level)
    {
        if (level > MaxDepth)
        {
            return JsonValueProblem.TooDeep;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!keys.Add(property.Name))
            {
                return JsonValueProblem.DuplicateKey;
            }

            if (ProblemIn(property.Value, level) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }

    private static JsonValueProblem? ProblemInArray(JsonElement element, int level)
    {
        if (level > MaxDepth)
        {
            return JsonValueProblem.TooDeep;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (ProblemIn(item, level) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }

    private static string Compact(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, _compact))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
