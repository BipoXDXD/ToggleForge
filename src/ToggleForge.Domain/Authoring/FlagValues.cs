namespace ToggleForge.Domain.Authoring;

/// <summary>
/// Os valores ligado e desligado de uma flag, sempre do mesmo tipo. Todo valor chega como <see cref="JsonFlagValue"/>,
/// porque <c>true</c> também é um valor válido numa flag JSON: é o tipo da flag que diz como lê-lo.
/// </summary>
public abstract record FlagValues
{
    private protected FlagValues()
    {
    }

    public abstract FlagType Type { get; }

    public abstract JsonFlagValue OnValue { get; }

    public abstract JsonFlagValue OffValue { get; }

    public static Result<FlagValues> Of(FlagType type, JsonFlagValue onValue, JsonFlagValue offValue)
    {
        ArgumentNullException.ThrowIfNull(onValue);
        ArgumentNullException.ThrowIfNull(offValue);
        return type switch
        {
            FlagType.Boolean => BooleanValues.From(onValue, offValue),
            FlagType.Json => new JsonValues(onValue, offValue),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown flag type."),
        };
    }

    /// <exception cref="InvalidDataException">O que foi guardado não forma um par válido do tipo.</exception>
    public static FlagValues Restore(FlagType type, JsonFlagValue onValue, JsonFlagValue offValue) =>
        Of(type, onValue, offValue).TryGetValue(out var values, out var failure)
            ? values
            : throw new InvalidDataException($"Stored {type} flag values are invalid: {failure.Detail}");

    /// <summary>O par com os lados dados trocados; nulo mantém o atual, e o tipo nunca muda.</summary>
    internal Result<FlagValues> With(JsonFlagValue? onValue, JsonFlagValue? offValue) =>
        Of(Type, onValue ?? OnValue, offValue ?? OffValue);
}

public sealed record BooleanValues(bool On, bool Off) : FlagValues
{
    public override FlagType Type => FlagType.Boolean;

    public override JsonFlagValue OnValue => On ? JsonFlagValue.True : JsonFlagValue.False;

    public override JsonFlagValue OffValue => Off ? JsonFlagValue.True : JsonFlagValue.False;

    internal static Result<FlagValues> From(JsonFlagValue onValue, JsonFlagValue offValue)
    {
        if (onValue.AsBoolean is not { } on)
        {
            return AuthoringFailures.OnValueNotOfFlagType;
        }

        if (offValue.AsBoolean is not { } off)
        {
            return AuthoringFailures.OffValueNotOfFlagType;
        }

        return new BooleanValues(on, off);
    }
}

public sealed record JsonValues : FlagValues
{
    public JsonValues(JsonFlagValue onValue, JsonFlagValue offValue)
    {
        OnValue = onValue ?? throw new ArgumentNullException(nameof(onValue));
        OffValue = offValue ?? throw new ArgumentNullException(nameof(offValue));
    }

    public override FlagType Type => FlagType.Json;

    public override JsonFlagValue OnValue { get; }

    public override JsonFlagValue OffValue { get; }

    /// <summary>Os bytes dos dois valores, que contam para o teto do rascunho.</summary>
    public int ByteCount => OnValue.ByteCount + OffValue.ByteCount;
}
