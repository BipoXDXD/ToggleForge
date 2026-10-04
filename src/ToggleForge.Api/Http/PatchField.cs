using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToggleForge.Api.Http;

/// <summary>
/// Campo de um merge patch (RFC 7396): ausente mantém o valor atual. O conversor só roda quando a propriedade
/// aparece no corpo, então o default da struct significa "ausente". Nenhum campo do contrato pode ser apagado, então
/// <c>null</c> é sempre recusado: o binding recusa nos tipos que não o leem (<c>bool</c>, <c>int</c>), mas um
/// <see cref="System.Text.Json.JsonElement"/> o lê como <see cref="System.Text.Json.JsonValueKind.Null"/>, e quem
/// recusa é o endpoint.
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
        /// <summary>
        /// Um <c>null</c> no corpo falha aqui quando <typeparamref name="T"/> não o lê, e passa como valor quando lê
        /// (<see cref="JsonElement"/>); nesse caso a recusa fica com quem consome o campo.
        /// </summary>
        public override PatchField<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try
            {
                return new(IsPresent: true, JsonSerializer.Deserialize<T>(ref reader, options));
            }
            catch (JsonException exception)
            {
                // A leitura aninhada começa um caminho novo ("$.b"), que perderia o campo do patch. Sem Path, o
                // serializador de fora preenche o caminho a partir do campo ("$.onValue"), como na criação.
                throw new JsonException(exception.Message, exception);
            }
        }

        public override void Write(Utf8JsonWriter writer, PatchField<T> value, JsonSerializerOptions options) =>
            throw new NotSupportedException("Patch fields are only read.");
    }
}
