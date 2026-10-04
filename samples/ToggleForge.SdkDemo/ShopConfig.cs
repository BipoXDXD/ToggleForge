using System.Globalization;
using System.Text.Json;

namespace ToggleForge.SdkDemo;

/// <summary>
/// A configuração da loja como o jogo a usa, lida do valor da flag JSON <c>shop-config</c>. Na Unity o parser
/// seria o JsonUtility ou o Newtonsoft; aqui é o System.Text.Json, e o SDK não depende de nenhum deles.
/// </summary>
internal sealed record ShopConfig(decimal Discount, IReadOnlyList<string> FeaturedItems)
{
    // Campo que falta ou chega nulo é recusado, em vez de virar 0 ou uma lista nula no jogo.
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>O valor padrão do jogo: sem desconto e sem destaques.</summary>
    public static ShopConfig Closed { get; } = new(0m, []);

    /// <exception cref="JsonException">O texto não é uma configuração da loja.</exception>
    public static ShopConfig Parse(string json) =>
        JsonSerializer.Deserialize<ShopConfig>(json, _options) ?? throw new JsonException("The shop config is null.");

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"desconto {Discount}, destaques [{string.Join(", ", FeaturedItems)}]");
}
