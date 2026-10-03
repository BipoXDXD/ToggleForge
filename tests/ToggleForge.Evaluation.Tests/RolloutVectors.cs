using System.Text.Json;

namespace ToggleForge.Evaluation.Tests;

/// <summary>
/// Vetores gerados pelo oráculo Python (tools/rollout-vectors/generate.py), independente da implementação C#.
/// </summary>
internal static class RolloutVectors
{
    public static TheoryData<string, string, string, string, string, int> All()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "Vectors", "rollout-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(filePath));
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

    private static string ReadString(JsonElement vector, string name) =>
        vector.GetProperty(name).GetString() ?? throw new InvalidDataException($"Vector field '{name}' is null.");
}
