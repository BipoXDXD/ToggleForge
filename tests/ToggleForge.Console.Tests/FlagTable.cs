using AngleSharp.Dom;

namespace ToggleForge.Console.Tests;

/// <summary>Lê as tabelas de flags do rascunho e da versão publicada, que têm as mesmas colunas.</summary>
internal static class FlagTable
{
    /// <summary>Chave, tipo, estado e rollout de uma linha, sem a coluna dos valores.</summary>
    public static string[] Row(IElement row)
    {
        var cells = row.QuerySelectorAll("td");
        return
        [
            cells[0].QuerySelector("[data-testid=flag-key]")?.TextContent ?? string.Empty,
            cells[0].QuerySelector("[data-testid=flag-type]")?.TextContent ?? string.Empty,
            cells[1].TextContent.Trim(),
            cells[2].TextContent.Trim(),
        ];
    }
}
