using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ToggleForge.Api.Tests.Support;

public sealed record ApiResponse(HttpStatusCode Status, HttpResponseMessage Message, string Body)
{
    public JsonNode Json => JsonNode.Parse(Body) ?? throw new JsonException("The body is JSON null.");

    public string ProblemType => Json["type"]!.GetValue<string>();

    public string? Header(string name) =>
        Message.Headers.NonValidated.TryGetValues(name, out var values) || Message.Content.Headers.NonValidated.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : null;

    /// <summary>Os ponteiros (corpo) ou nomes (path e header) de cada erro listado no Problem Details.</summary>
    public IReadOnlyList<string> ErrorSources() =>
        Json["errors"]!.AsArray()
            .Select(error => error!["source"]!)
            .Select(source => (source["pointer"] ?? source["name"])!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .ToList();
}
