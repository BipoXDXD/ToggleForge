using System.Globalization;
using ToggleForge.Console.Api;

namespace ToggleForge.Console.History;

/// <summary>Como o histórico descreve uma publicação para o membro.</summary>
public static class PublicationText
{
    /// <summary>Sempre em UTC, escrito por extenso: o estúdio pode ter membros em fusos diferentes.</summary>
    public static string When(DateTimeOffset publishedAt) =>
        publishedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <param name="origin">Origem desconhecida (a lista pode crescer) aparece como publicação comum.</param>
    public static string Origin(string origin, long? restoredVersion) =>
        origin == "rollback" && restoredVersion is { } restored
            ? $"Rollback to version {restored.ToString(CultureInfo.InvariantCulture)}"
            : "Publication";
}
