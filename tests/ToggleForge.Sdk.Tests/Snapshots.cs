namespace ToggleForge.Sdk.Tests;

/// <summary>Corpos de <c>GET /sdk/v1/snapshot</c> como a API os manda, com o que cada teste precisa mudar.</summary>
internal static class Snapshots
{
    public const string Credential = "tfsdk_CANARYxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";

    public static string Json(long version = 1, string environmentKey = "dev", string flags = """[{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":10000,"seed":"s1"}]""") =>
        $$"""
        {"algorithm":"toggleforge.rollout.v1","projectKey":"space-race","environmentKey":"{{environmentKey}}","version":{{version}},
         "publishedAt":"2026-10-04T12:00:00Z","flags":{{flags}}}
        """;

    /// <summary>A flag <c>new-shop</c> desligada, para distinguir uma versão da outra pelo valor.</summary>
    public static string Disabled(long version) =>
        Json(version, flags: """[{"key":"new-shop","enabled":false,"onValue":true,"offValue":false,"rolloutBasisPoints":10000,"seed":"s1"}]""");

    /// <summary>A flag JSON <c>shop-config</c>, ligada para todos, com o valor ligado dado e o desligado <c>null</c>.</summary>
    public static string JsonFlag(string onValue = """{"discount":0.2,"featuredItems":["sword"]}""", string offValue = "null") =>
        $$"""{"key":"shop-config","type":"json","enabled":true,"onValue":{{onValue}},"offValue":{{offValue}},"rolloutBasisPoints":10000,"seed":"s2"}""";

    /// <summary>A flag booleana <c>new-shop</c> de <see cref="Json"/> e a flag JSON <c>shop-config</c>.</summary>
    public static string WithShopConfig(long version = 1) =>
        Json(version, flags: $$"""[{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":10000,"seed":"s1"},{{JsonFlag()}}]""");
}
