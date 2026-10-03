using System.Text.Json.Serialization;

namespace ToggleForge.Api.Http;

/// <summary>Problem Details (RFC 9457) exatamente como no contrato: nenhum campo além destes.</summary>
internal sealed record ProblemBody(
    string Type,
    string Title,
    int Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Instance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ProblemError>? Errors = null);

internal sealed record ProblemError(string Type, string Detail, ProblemSource Source);

internal sealed record ProblemSource(
    string Location,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Pointer = null);
