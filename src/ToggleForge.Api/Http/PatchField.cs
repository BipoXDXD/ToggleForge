using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToggleForge.Api.Http;

/// <summary>
/// Campo de um merge patch (RFC 7396): ausente mantém o valor atual. O conversor só roda quando a propriedade
/// aparece no corpo, então o default da struct significa "ausente". <c>null</c> é recusado, porque nenhum campo
/// do contrato pode ser apagado.
/// </summary>
[JsonConverter(typeof(PatchFieldConverterFactory))]
internal readonly record struct PatchField<T>(bool IsPresent, T Value)
    where T : struct
{
    public T? OrNull => IsPresent ? Value : null;
}

internal sealed class PatchFieldConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(PatchField<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        var converter = typeof(Converter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]);
        return (JsonConverter)Activator.CreateInstance(converter)!;
    }

    private sealed class Converter<T> : JsonConverter<PatchField<T>>
        where T : struct
    {
        /// <summary><typeparamref name="T"/> é uma struct não anulável, então um <c>null</c> no corpo já falha aqui.</summary>
        public override PatchField<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(IsPresent: true, JsonSerializer.Deserialize<T>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, PatchField<T> value, JsonSerializerOptions options) =>
            throw new NotSupportedException("Patch fields are only read.");
    }
}
