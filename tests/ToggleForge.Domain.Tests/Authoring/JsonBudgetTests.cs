using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Tests.Authoring;

/// <summary>O teto de 128 KiB de valores JSON por rascunho (ADR 0011), que protege cada download do snapshot.</summary>
public sealed class JsonBudgetTests
{
    // 16 flags com dois valores de 4 KiB somam exatamente 128 KiB.
    private const int FlagsThatFillTheBudget = 16;

    private static readonly FlagKey _growing = FlagKey.Parse("growing");

    private static readonly string _full = JsonTextOf(bytes: 4_096);

    [Fact]
    public void A_draft_holds_exactly_128_KiB_of_json_values()
    {
        var environment = Flags.NewEnvironment();

        for (var flag = 1; flag <= FlagsThatFillTheBudget; flag++)
        {
            Assert.True(environment.AddFlag(FlagKey.Parse($"config-{flag}"), FullJsonSettings(), RolloutSeed.Parse("s1")).TryGetValue(out _, out _));
        }
    }

    [Fact]
    public void The_smallest_json_flag_past_the_budget_is_refused_and_the_revision_stays()
    {
        var environment = FullEnvironment();
        var revision = environment.DraftRevision;

        // A menor flag JSON tem 2 bytes: "0" de cada lado.
        var added = environment.AddFlag(FlagKey.Parse("one-more"), JsonSettings("0", "0"), RolloutSeed.Parse("s1"));

        Assert.False(added.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.JsonBudgetReached, failure);
        Assert.Equal(revision, environment.DraftRevision);
    }

    [Fact]
    public void Boolean_flags_do_not_count_toward_the_budget() =>
        Assert.True(FullEnvironment().AddFlag(FlagKey.Parse("new-shop"), Flags.Settings(), RolloutSeed.Parse("s1")).TryGetValue(out _, out _));

    [Fact]
    public void A_change_that_grows_a_value_one_byte_past_the_budget_is_refused_and_keeps_the_flag()
    {
        var environment = EnvironmentWithRoomFor4093Bytes();
        var flag = environment.Flags.Single(flag => flag.Key == _growing);
        var (revision, settings) = (environment.DraftRevision, flag.Settings);

        // Trocar "0" (1 byte) por 4.095 bytes soma 4.094: 131.073 bytes, um além do teto.
        var changed = environment.ChangeFlag(_growing, flag.ChangedAtRevision, new FlagChange(OffValue: JsonFlagValue.Parse(JsonTextOf(bytes: 4_095))));

        Assert.False(changed.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.JsonBudgetReached, failure);
        Assert.Equal(revision, environment.DraftRevision);
        Assert.Equal(settings, flag.Settings);
    }

    [Fact]
    public void A_change_that_fills_the_budget_exactly_is_accepted()
    {
        var environment = EnvironmentWithRoomFor4093Bytes();
        var revision = environment.Flags.Single(flag => flag.Key == _growing).ChangedAtRevision;

        // Trocar "0" (1 byte) por 4.094 bytes soma 4.093: exatamente 131.072 bytes.
        var changed = environment.ChangeFlag(_growing, revision, new FlagChange(OffValue: JsonFlagValue.Parse(JsonTextOf(bytes: 4_094))));

        Assert.True(changed.TryGetValue(out var flag, out _));
        Assert.Equal(4_094, Assert.IsType<JsonValues>(flag.Settings.Values).OffValue.ByteCount);
    }

    [Fact]
    public void A_change_that_shrinks_a_value_is_accepted_when_the_draft_is_full()
    {
        var environment = FullEnvironment();
        var key = FlagKey.Parse("config-1");
        var revision = environment.Flags.Single(flag => flag.Key == key).ChangedAtRevision;

        Assert.True(environment.ChangeFlag(key, revision, new FlagChange(OnValue: JsonFlagValue.Parse("0"))).TryGetValue(out _, out _));
    }

    [Fact]
    public void Reaching_the_budget_is_a_conflict_over_a_limit() =>
        Assert.Equal((FailureKind.Conflict, "limit-reached"), (AuthoringFailures.JsonBudgetReached.Kind, AuthoringFailures.JsonBudgetReached.Code));

    private static ProjectEnvironment FullEnvironment()
    {
        var environment = Flags.NewEnvironment();
        for (var flag = 1; flag <= FlagsThatFillTheBudget; flag++)
        {
            environment.AddFlag(FlagKey.Parse($"config-{flag}"), FullJsonSettings(), RolloutSeed.Parse("s1"));
        }

        return environment;
    }

    /// <summary>15 × 8.192 + 4.097 + 2 = 126.979 bytes: sobram 4.093 até o teto.</summary>
    private static ProjectEnvironment EnvironmentWithRoomFor4093Bytes()
    {
        var environment = Flags.NewEnvironment();
        for (var flag = 1; flag < FlagsThatFillTheBudget; flag++)
        {
            environment.AddFlag(FlagKey.Parse($"config-{flag}"), FullJsonSettings(), RolloutSeed.Parse("s1"));
        }

        environment.AddFlag(_growing, JsonSettings(_full, "0"), RolloutSeed.Parse("s1"));
        environment.AddFlag(FlagKey.Parse("tiny"), JsonSettings("0", "0"), RolloutSeed.Parse("s1"));
        return environment;
    }

    /// <summary>Uma string JSON com exatamente <paramref name="bytes"/> bytes, contando as aspas.</summary>
    private static string JsonTextOf(int bytes) => "\"" + new string('x', bytes - 2) + "\"";

    private static FlagSettings FullJsonSettings() => JsonSettings(_full, _full);

    private static FlagSettings JsonSettings(string on, string off) =>
        new(IsEnabled: true, new JsonValues(JsonFlagValue.Parse(on), JsonFlagValue.Parse(off)), RolloutPercentage.All);
}
