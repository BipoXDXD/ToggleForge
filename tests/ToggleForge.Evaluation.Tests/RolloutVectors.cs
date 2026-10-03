using System.Text.Json;

namespace ToggleForge.Evaluation.Tests;

internal static class RolloutVectors
{
    public static TheoryData<string, string, string, string, string, int> Buckets()
    {
        using var document = Load();
        var data = new TheoryData<string, string, string, string, string, int>();
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            data.Add(
                ReadString(vector, "projectKey"),
                ReadString(vector, "environmentKey"),
                ReadString(vector, "flagKey"),
                ReadString(vector, "seed"),
                ReadString(vector, "playerId"),
                vector.GetProperty("bucket").GetInt32());
        }

        return data;
    }

    /// <summary>Casos de validação como code units UTF-16, para que surrogates soltos sobrevivam ao JSON.</summary>
    public static TheoryData<string, int[], bool> OpaqueTextCases(string property)
    {
        using var document = Load();
        var data = new TheoryData<string, int[], bool>();
        foreach (var testCase in document.RootElement.GetProperty(property).EnumerateArray())
        {
            data.Add(
                ReadString(testCase, "name"),
                testCase.GetProperty("codeUnits").EnumerateArray().Select(unit => unit.GetInt32()).ToArray(),
                testCase.GetProperty("valid").GetBoolean());
        }

        return data;
    }

    public static string FromCodeUnits(int[] codeUnits) =>
        new(codeUnits.Select(codeUnit => (char)codeUnit).ToArray());

    private static JsonDocument Load() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Vectors", "rollout-v1.json")));

    private static string ReadString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new InvalidDataException($"Field '{name}' is null.");
}
