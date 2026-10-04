using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ToggleForge.Evaluation;

namespace ToggleForge.Sdk
{
    /// <summary>
    /// Lê o JSON de <c>GET /sdk/v1/snapshot</c> à mão, sem desserialização por reflection, que o stripping do
    /// IL2CPP quebraria (ADR 0010). Campo desconhecido é ignorado, e flag de um tipo que este SDK não conhece
    /// também (ADR 0011), porque builds antigos do jogo continuam no ar quando o contrato cresce; qualquer campo
    /// usado que esteja errado recusa o snapshot inteiro.
    /// </summary>
    internal static class SnapshotReader
    {
        internal const string Algorithm = "toggleforge.rollout.v1";

        private const string BooleanType = "boolean";

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

        /// <returns>
        /// Nulo se alguma flag for inválida ou se uma chave se repetir, mesmo numa flag de tipo que este SDK não
        /// conhece: a chave é comum a todos os tipos.
        /// </returns>
        private static List<BooleanFlag>? ReadFlags(JArray items)
        {
            var flags = new List<BooleanFlag>(items.Count);
            var keys = new HashSet<FlagKey>();
            foreach (var item in items)
            {
                if (item is not JObject entry || !FlagKey.TryParse(Text(entry, "key"), out var key) || !keys.Add(key))
                {
                    return null;
                }

                switch (KindOf(entry))
                {
                    case FlagKind.Boolean when ReadBooleanFlag(key, entry) is { } flag:
                        flags.Add(flag);
                        break;
                    case FlagKind.Newer:
                        // Publicada depois deste SDK: só ela fica de fora, e o jogo recebe o próprio valor padrão
                        // para ela.
                        break;
                    default:
                        return null;
                }
            }

            return flags;
        }

        /// <remarks>Sem <c>type</c> a flag é booleana: os snapshots anteriores à ADR 0011 não o tinham.</remarks>
        private static FlagKind KindOf(JObject flag) => flag["type"] switch
        {
            null => FlagKind.Boolean,
            JValue { Type: JTokenType.String, Value: BooleanType } => FlagKind.Boolean,
            JValue { Type: JTokenType.String } => FlagKind.Newer,
            _ => FlagKind.Malformed,
        };

        private static BooleanFlag? ReadBooleanFlag(FlagKey key, JObject flag)
        {
            if (Boolean(flag, "enabled") is not { } enabled
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

        private enum FlagKind
        {
            Boolean,

            /// <summary>Um tipo que este SDK não conhece, publicado depois dele.</summary>
            Newer,

            Malformed,
        }
    }
}
