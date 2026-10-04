using System.Text.Json;
using ToggleForge.Domain.Authoring;

namespace ToggleForge.Api.Http;

/// <summary>O único lugar da API que traduz o tipo de uma flag entre o contrato e o domínio.</summary>
internal static class FlagTypes
{
    public static string Name(FlagType type) => type switch
    {
        FlagType.Boolean => "boolean",
        FlagType.Json => "json",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown flag type."),
    };

    /// <summary>Só as strings exatas do contrato; <c>null</c>, outro tipo JSON ou outra caixa são recusados.</summary>
    public static bool TryParse(JsonElement name, out FlagType type)
    {
        type = default;
        if (name.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        foreach (var candidate in Enum.GetValues<FlagType>())
        {
            if (NameEquals(name, Name(candidate)))
            {
                type = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool NameEquals(JsonElement name, string expected)
    {
        try
        {
            return name.ValueEquals(expected);
        }
        catch (InvalidOperationException)
        {
            // Um escape de surrogate isolado (ou UTF-8 inválido) não forma texto: não é nenhum dos nomes do contrato.
            return false;
        }
    }
}
