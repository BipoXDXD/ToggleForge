namespace ToggleForge.Application.Authoring;

/// <summary>Página do histórico: até <see cref="Size"/> versões abaixo de <see cref="BeforeVersion"/>, se houver.</summary>
public sealed record PageRequest
{
    public const int DefaultSize = 20;
    public const int MaxSize = 100;

    public PageRequest(int size, long? beforeVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxSize);
        Size = size;
        BeforeVersion = beforeVersion;
    }

    public int Size { get; }

    public long? BeforeVersion { get; }
}
