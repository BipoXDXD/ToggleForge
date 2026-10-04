using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private const string JsonType = "json";

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
        private static List<Flag>? ReadFlags(JArray items)
        {
            var flags = new List<Flag>(items.Count);
            var keys = new HashSet<FlagKey>();
            foreach (var item in items)
            {
                if (item is not JObject entry || !FlagKey.TryParse(Text(entry, "key"), out var key) || !keys.Add(key))
                {
                    return null;
                }

                switch (KindOf(entry))
                {
                    case FlagKind.Boolean when ReadBooleanFlag(key, entry) is { } boolean:
                        flags.Add(boolean);
                        break;
                    case FlagKind.Json when ReadJsonFlag(key, entry) is { } json:
                        flags.Add(json);
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
            JValue { Type: JTokenType.String, Value: JsonType } => FlagKind.Json,
            JValue { Type: JTokenType.String } => FlagKind.Newer,
            _ => FlagKind.Malformed,
        };

        private static BooleanFlag? ReadBooleanFlag(FlagKey key, JObject flag)
        {
            if (Boolean(flag, "enabled") is not { } enabled
                || Boolean(flag, "onValue") is not { } onValue
                || Boolean(flag, "offValue") is not { } offValue
                || ReadRollout(flag) is not { } rollout)
            {
                return null;
            }

            return new BooleanFlag(key, enabled, onValue, offValue, rollout);
        }

        private static JsonFlag? ReadJsonFlag(FlagKey key, JObject flag)
        {
            if (Boolean(flag, "enabled") is not { } enabled
                || CompactJson(flag, "onValue") is not { } onValue
                || CompactJson(flag, "offValue") is not { } offValue
                || ReadRollout(flag) is not { } rollout)
            {
                return null;
            }

            return new JsonFlag(key, enabled, onValue, offValue, rollout);
        }

        private static PercentageRollout? ReadRollout(JObject flag)
        {
            if (Integer(flag, "rolloutBasisPoints") is not { } basisPoints
                || basisPoints is < int.MinValue or > int.MaxValue
                || !RolloutPercentage.TryFromBasisPoints((int)basisPoints, out var percentage)
                || !RolloutSeed.TryParse(Text(flag, "seed"), out var seed))
            {
                return null;
            }

            return new PercentageRollout(percentage, seed);
        }

        /// <returns>
        /// O valor regravado como JSON compacto, com as chaves na ordem recebida; nulo se o campo faltar ou trouxer
        /// algo que um parser JSON comum não leria. O JSON <c>null</c> é um valor, não a falta dele.
        /// </returns>
        private static string? CompactJson(JObject source, string name) =>
            source[name] is { } value && IsPlainJson(value) ? value.ToString(Formatting.None) : null;

        private static bool IsPlainJson(JToken value) =>
            value is JContainer container ? container.DescendantsAndSelf().All(IsPlainToken) : IsPlainToken(value);

        // O Newtonsoft aceita NaN, Infinity, undefined e construtores, e lê 1e400 como infinito. Regravados, viram
        // texto que o parser do jogo leria de outro jeito ou nem leria; a API nunca os manda (ADR 0011).
        private static bool IsPlainToken(JToken token) => token.Type switch
        {
            JTokenType.Object or JTokenType.Array or JTokenType.Property => true,
            JTokenType.String or JTokenType.Integer or JTokenType.Boolean or JTokenType.Null => true,
            JTokenType.Float => token is JValue { Value: double number } && double.IsFinite(number),
            _ => false,
        };

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

            Json,

            /// <summary>Um tipo que este SDK não conhece, publicado depois dele.</summary>
            Newer,

            Malformed,
        }
    }
}
