using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ToggleForge.Evaluation;

namespace ToggleForge.Sdk
{
    /// <summary>
    /// Lê o JSON de <c>GET /sdk/v1/snapshot</c> à mão, sem desserialização por reflection, que o stripping do
    /// IL2CPP quebraria (ADR 0010). Campo desconhecido é ignorado, porque builds antigos do jogo continuam no ar
    /// quando o contrato cresce; qualquer campo usado que esteja errado recusa o snapshot inteiro.
    /// </summary>
    internal static class SnapshotReader
    {
        internal const string Algorithm = "toggleforge.rollout.v1";

        // Folgado de propósito: o snapshot de hoje tem três níveis, mas as configurações JSON vão aninhar mais, e um
        // SDK já publicado num jogo não se corrige. O teto só barra JSON patológico (ADR 0010).
        private const int MaxDepth = 64;

        /// <returns>Nulo quando o corpo não é um snapshot que este SDK sabe avaliar.</returns>
        internal static ConfigurationSnapshot? Read(string json) =>
            Parse(json) is JObject root ? ReadSnapshot(root) : null;

        private static JToken? Parse(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json))
            {
                MaxDepth = MaxDepth,
                DateParseHandling = DateParseHandling.None,
            };
            var settings = new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };
            try
            {
                var token = JToken.ReadFrom(reader, settings);
                return reader.Read() ? null : token;
            }
            catch (JsonReaderException)
            {
                return null;
            }
        }

        private static ConfigurationSnapshot? ReadSnapshot(JObject root)
        {
            if (Text(root, "algorithm") != Algorithm
                || !ProjectKey.TryParse(Text(root, "projectKey"), out var projectKey)
                || !EnvironmentKey.TryParse(Text(root, "environmentKey"), out var environmentKey)
                || Integer(root, "version") is not { } version
                || version < 1
                || root["flags"] is not JArray items)
            {
                return null;
            }

            var flags = ReadFlags(items);
            return flags is null ? null : new ConfigurationSnapshot(projectKey, environmentKey, version, flags);
        }

        /// <returns>Nulo se alguma flag for inválida ou se uma chave se repetir.</returns>
        private static List<BooleanFlag>? ReadFlags(JArray items)
        {
            var flags = new List<BooleanFlag>(items.Count);
            var keys = new HashSet<FlagKey>();
            foreach (var item in items)
            {
                if (item is not JObject entry || ReadFlag(entry) is not { } flag || !keys.Add(flag.Key))
                {
                    return null;
                }

                flags.Add(flag);
            }

            return flags;
        }

        private static BooleanFlag? ReadFlag(JObject flag)
        {
            if (!FlagKey.TryParse(Text(flag, "key"), out var key)
                || Boolean(flag, "enabled") is not { } enabled
                || Boolean(flag, "onValue") is not { } onValue
                || Boolean(flag, "offValue") is not { } offValue
                || Integer(flag, "rolloutBasisPoints") is not { } basisPoints
                || basisPoints is < int.MinValue or > int.MaxValue
                || !RolloutPercentage.TryFromBasisPoints((int)basisPoints, out var percentage)
                || !RolloutSeed.TryParse(Text(flag, "seed"), out var seed))
            {
                return null;
            }

            return new BooleanFlag(key, enabled, onValue, offValue, new PercentageRollout(percentage, seed));
        }

        // Tipos JSON exatos: "true" em texto não é booleano, e 10.0 não é inteiro.
        private static string? Text(JObject source, string name) =>
            source[name] is JValue { Type: JTokenType.String, Value: string text } ? text : null;

        private static bool? Boolean(JObject source, string name) =>
            source[name] is JValue { Type: JTokenType.Boolean, Value: bool value } ? value : null;

        /// <remarks>Inteiro maior que <see cref="long"/> chega como BigInteger e é recusado.</remarks>
        private static long? Integer(JObject source, string name) =>
            source[name] is JValue { Type: JTokenType.Integer, Value: long value } ? value : null;
    }
}
