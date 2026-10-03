using System.Globalization;

namespace ToggleForge.Api.Http;

internal static class RetryAfter
{
    /// <summary>O contrato aceita de 1 a 3600 segundos.</summary>
    public const int MaxSeconds = 3600;

    /// <summary>Segundos inteiros, arredondados para cima, para o header Retry-After.</summary>
    public static string Seconds(TimeSpan wait) =>
        Math.Clamp((int)Math.Ceiling(wait.TotalSeconds), 1, MaxSeconds).ToString(CultureInfo.InvariantCulture);
}
