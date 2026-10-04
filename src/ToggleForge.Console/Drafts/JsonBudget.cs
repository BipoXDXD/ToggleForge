using System.Globalization;
using System.Text;
using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>
/// Quanto do teto de valores JSON de um ambiente o rascunho gasta: os bytes UTF-8 do <c>onValue</c> e do
/// <c>offValue</c> de cada flag JSON, compactados, como a API soma (ADR 0011). Booleanas não contam. O número é o
/// mesmo da API porque ela devolve os valores já compactados, e quem recusa o que passa do teto é ela (409).
/// </summary>
public static class JsonBudget
{
    public const int MaxKibibytes = 128;

    private const int BytesPerKibibyte = 1024;

    public const int MaxBytes = MaxKibibytes * BytesPerKibibyte;

    // Acima de três quartos do teto o rascunho avisa; abaixo disso o número basta (design spec, 2.3).
    private const int NearLimitBytes = MaxBytes / 4 * 3;

    public static int BytesOf(IEnumerable<Flag> flags) =>
        flags.Where(flag => flag.IsJson).Sum(flag => Encoding.UTF8.GetByteCount(flag.OnValue.Text) + Encoding.UTF8.GetByteCount(flag.OffValue.Text));

    /// <summary>Em KiB com uma casa, arredondado para baixo: 131.071 bytes ainda cabem e não podem aparecer como 128.0.</summary>
    public static string FormatKibibytes(int bytes) =>
        (Math.Floor(bytes * 10m / BytesPerKibibyte) / 10).ToString("0.0", CultureInfo.InvariantCulture);

    public static bool IsNearLimit(int bytes) => bytes > NearLimitBytes;
}
