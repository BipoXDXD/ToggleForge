using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Tests.Authoring;

/// <summary>O teto de 128 KiB de valores JSON por rascunho (ADR 0011), que protege cada download do snapshot.</summary>
public sealed class JsonBudgetTests
{
    // 16 flags com dois valores de 4 KiB somam exatamente 128 KiB.
    private const int FlagsThatFillTheBudget = 16;

    // Um texto JSON de 4.096 bytes: aspas mais 4.094 caracteres.
    private static readonly string _full = "\"" + new string('x', 4094) + "\"";

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
    public void One_more_byte_of_json_is_refused_and_the_revision_stays()
    {
        var environment = FullEnvironment();
        var revision = environment.DraftRevision;

        var added = environment.AddFlag(FlagKey.Parse("one-more"), JsonSettings("0", "0"), RolloutSeed.Parse("s1"));

        Assert.False(added.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.JsonBudgetReached, failure);
        Assert.Equal(revision, environment.DraftRevision);
    }

    [Fact]
    public void Boolean_flags_do_not_count_toward_the_budget() =>
        Assert.True(FullEnvironment().AddFlag(FlagKey.Parse("new-shop"), Flags.Settings(), RolloutSeed.Parse("s1")).TryGetValue(out _, out _));

    [Fact]
    public void A_change_that_grows_a_value_past_the_budget_is_refused()
    {
        var environment = Flags.NewEnvironment();
        for (var flag = 1; flag < FlagsThatFillTheBudget; flag++)
        {
            environment.AddFlag(FlagKey.Parse($"config-{flag}"), FullJsonSettings(), RolloutSeed.Parse("s1"));
        }

        environment.AddFlag(FlagKey.Parse("growing"), JsonSettings(_full, "0"), RolloutSeed.Parse("s1"));
        environment.AddFlag(FlagKey.Parse("tiny"), JsonSettings("0", "0"), RolloutSeed.Parse("s1"));
        // 15 × 8.192 + 4.097 + 2 = 126.979 bytes: sobram 4.093, e trocar "0" por 4.096 bytes soma 4.095.
        var growing = FlagKey.Parse("growing");
        var revision = environment.Flags.Single(flag => flag.Key == growing).ChangedAtRevision;

        var changed = environment.ChangeFlag(growing, revision, new FlagChange(OffValue: JsonFlagValue.Parse(_full)));

        Assert.False(changed.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.JsonBudgetReached, failure);
    }

    [Fact]
    public void A_change_that_shrinks_a_value_is_accepted_when_the_draft_is_full()
    {
        var environment = FullEnvironment();
        var key = FlagKey.Parse("config-1");
        var revision = environment.Flags.Single(flag => flag.Key == key).ChangedAtRevision;

        Assert.True(environment.ChangeFlag(key, revision, new FlagChange(OnValue: JsonFlagValue.Parse("0"))).TryGetValue(out _, out _));
    }

    private static ProjectEnvironment FullEnvironment()
    {
        var environment = Flags.NewEnvironment();
        for (var flag = 1; flag <= FlagsThatFillTheBudget; flag++)
        {
            environment.AddFlag(FlagKey.Parse($"config-{flag}"), FullJsonSettings(), RolloutSeed.Parse("s1"));
        }

        return environment;
    }

    private static FlagSettings FullJsonSettings() => JsonSettings(_full, _full);

    private static FlagSettings JsonSettings(string on, string off) =>
        new(IsEnabled: true, new JsonValues(JsonFlagValue.Parse(on), JsonFlagValue.Parse(off)), RolloutPercentage.All);
}
