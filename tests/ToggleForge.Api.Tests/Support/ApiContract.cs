using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Json.Pointer;
using Json.Schema;
using Yaml2JsonNode;

namespace ToggleForge.Api.Tests.Support;

/// <summary>
/// Confere cada resposta contra <c>docs/api/openapi.yaml</c>: o status está declarado na operação, o corpo
/// segue o schema e todo header declarado (exceto Set-Cookie) está presente e válido.
/// </summary>
public static class ApiContract
{
    private static readonly Uri _documentUri = new("https://contract.toggleforge.test/openapi.json");
    private static readonly JsonElement _document = Load();
    private static readonly BuildOptions _buildOptions = Register();
    private static readonly EvaluationOptions _evaluation = new() { RequireFormatValidation = true, OutputFormat = OutputFormat.List };

    public static void Verify(string operationId, HttpResponseMessage response, string body)
    {
        ArgumentNullException.ThrowIfNull(response);

        var operation = FindOperation(operationId);
        var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        var responsePointer = Resolve(operation.Combine("responses", status))
            ?? throw new Xunit.Sdk.XunitException($"{operationId} answered {status}, which the contract does not declare.\n{body}");

        VerifyHeaders(operationId, responsePointer, response);
        VerifyBody(operationId, responsePointer, response.Content.Headers.ContentType, body);
    }

    /// <summary>operationId → (método HTTP, path) de cada operação do contrato.</summary>
    public static IReadOnlyDictionary<string, (string Method, string Path)> Operations() =>
        _document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(method => method.Value.ValueKind == JsonValueKind.Object && method.Value.TryGetProperty("operationId", out _))
                .Select(method => (Id: method.Value.GetProperty("operationId").GetString()!, Method: method.Name.ToUpperInvariant(), Path: path.Name)))
            .ToDictionary(operation => operation.Id, operation => (operation.Method, operation.Path));

    /// <summary>operationId de cada operação que declara corpo de requisição.</summary>
    public static IReadOnlyList<string> OperationsTakingABody() =>
        Operations().Keys.Where(operationId => Resolve(FindOperation(operationId).Combine("requestBody")) is not null).Order(StringComparer.Ordinal).ToList();

    public static bool Declares(string operationId, int status) =>
        Resolve(FindOperation(operationId).Combine("responses", status.ToString(CultureInfo.InvariantCulture))) is not null;

    private static void VerifyHeaders(string operationId, JsonPointer responsePointer, HttpResponseMessage response)
    {
        if (!Node(responsePointer).TryGetProperty("headers", out var headers))
        {
            return;
        }

        foreach (var header in headers.EnumerateObject().Where(header => !header.NameEquals("Set-Cookie")))
        {
            // Valor cru: o parser tipado do HttpClient mistura o Pragma no Cache-Control.
            Assert.True(
                response.Headers.NonValidated.TryGetValues(header.Name, out var values)
                    || response.Content.Headers.NonValidated.TryGetValues(header.Name, out values),
                $"{operationId} {(int)response.StatusCode} lacks the declared header {header.Name}.");
            var schemaPointer = Resolve(responsePointer.Combine("headers", header.Name))!.Value.Combine("schema");
            var value = string.Join(", ", values);
            var instance = Node(schemaPointer).TryGetProperty("type", out var type) && type.GetString() == "integer"
                ? JsonSerializer.SerializeToElement(int.Parse(value, CultureInfo.InvariantCulture))
                : JsonSerializer.SerializeToElement(value);
            AssertValid(schemaPointer, instance, $"{operationId} header {header.Name}");
        }
    }

    private static void VerifyBody(string operationId, JsonPointer responsePointer, MediaTypeHeaderValue? contentType, string body)
    {
        if (!Node(responsePointer).TryGetProperty("content", out var content))
        {
            Assert.True(body.Length == 0, $"{operationId} declares no body but returned: {body}");
            return;
        }

        var mediaType = contentType?.MediaType ?? throw new Xunit.Sdk.XunitException($"{operationId} returned no Content-Type.");
        Assert.True(content.TryGetProperty(mediaType, out _), $"{operationId} returned {mediaType}, not declared in the contract.");
        AssertValid(responsePointer.Combine("content", mediaType, "schema"), JsonDocument.Parse(body).RootElement, $"{operationId} body");
    }

    private static void AssertValid(JsonPointer schemaPointer, JsonElement instance, string subject)
    {
        var schema = JsonSchema.FromText($$"""{"$ref": "{{_documentUri}}#{{UriFragment(schemaPointer)}}"}""", _buildOptions);
        var results = schema.Evaluate(instance, _evaluation);
        if (results.IsValid)
        {
            return;
        }

        var problems = (results.Details ?? [])
            .Where(detail => detail.Errors is not null)
            .SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}"));
        throw new Xunit.Sdk.XunitException($"{subject} breaks the contract:\n{string.Join('\n', problems)}\n{instance}");
    }

    /// <summary>O ponteiro vai num fragmento de URI, onde o "+" de "application/problem+json" precisa de escape.</summary>
    private static string UriFragment(JsonPointer pointer) =>
        string.Join('/', pointer.ToString().Split('/').Select(Uri.EscapeDataString));

    private static JsonPointer FindOperation(string operationId)
    {
        foreach (var path in _document.GetProperty("paths").EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                if (method.Value.ValueKind == JsonValueKind.Object
                    && method.Value.TryGetProperty("operationId", out var id)
                    && id.GetString() == operationId)
                {
                    return JsonPointer.Create(["paths", path.Name, method.Name]);
                }
            }
        }

        throw new ArgumentException($"The contract has no operation {operationId}.", nameof(operationId));
    }

    /// <summary>Segue um <c>$ref</c> local (como <c>#/components/responses/NotFound</c>); nulo se não há nada ali.</summary>
    private static JsonPointer? Resolve(JsonPointer pointer)
    {
        if (pointer.Evaluate(_document) is not { } node)
        {
            return null;
        }

        return node.TryGetProperty("$ref", out var reference) ? JsonPointer.Parse(reference.GetString()![1..]) : pointer;
    }

    private static JsonElement Node(JsonPointer pointer) =>
        pointer.Evaluate(_document) ?? throw new InvalidOperationException($"Nothing at {pointer} in the contract.");

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Contract", "openapi.yaml");
        return YamlSerializer.Parse(File.ReadAllText(path)).ToJsonElement().Single();
    }

    private static BuildOptions Register()
    {
        var options = new BuildOptions();
        options.SchemaRegistry.Register(new JsonElementBaseDocument(_document, _documentUri));
        return options;
    }
}
