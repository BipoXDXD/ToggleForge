namespace ToggleForge.Evaluation.Tests;

public sealed class RolloutBucketTests
{
    public static TheoryData<string, string, string, string, string, int> ReferenceVectors => RolloutVectors.All();

    [Theory]
    [MemberData(nameof(ReferenceVectors))]
    public void Compute_matches_the_reference_vector(
        string projectKey, string environmentKey, string flagKey, string seed, string playerId, int expectedBucket)
    {
        var bucket = RolloutBucket.Compute(
            ProjectKey.Parse(projectKey),
            EnvironmentKey.Parse(environmentKey),
            FlagKey.Parse(flagKey),
            seed,
            PlayerId.Parse(playerId));

        Assert.Equal(expectedBucket, bucket);
    }

    [Fact]
    public void Compute_keeps_field_boundaries_so_shifted_concatenations_do_not_collide()
    {
        var abThenC = Compute("ab", "c");
        var aThenBc = Compute("a", "bc");

        Assert.NotEqual(abThenC, aThenBc);
    }

    [Fact]
    public void Compute_returns_buckets_from_0_to_9999()
    {
        var buckets = SamplePlayers(10_000).Select(player => Compute("studio-game", "prod", player)).ToList();

        Assert.All(buckets, bucket => Assert.InRange(bucket, 0, RolloutBucket.BucketCount - 1));
    }

    [Fact]
    public void Compute_places_the_same_player_in_independent_buckets_per_environment()
    {
        var players = SamplePlayers(1_000).ToList();

        var differentBuckets = players.Count(player =>
            Compute("studio-game", "prod", player) != Compute("studio-game", "staging", player));

        // Buckets independentes coincidem com probabilidade 1/10000; 1000 jogadores quase nunca coincidem.
        Assert.True(differentBuckets >= 990, $"Only {differentBuckets} of 1000 players changed bucket.");
    }

    private static int Compute(string projectKey, string environmentKey, string playerId = "player") =>
        RolloutBucket.Compute(
            ProjectKey.Parse(projectKey),
            EnvironmentKey.Parse(environmentKey),
            FlagKey.Parse("flag"),
            "seed",
            PlayerId.Parse(playerId));

    private static IEnumerable<string> SamplePlayers(int count) =>
        Enumerable.Range(1, count).Select(n => $"player-{n}");
}
