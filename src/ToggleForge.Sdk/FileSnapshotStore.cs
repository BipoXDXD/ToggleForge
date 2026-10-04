using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ToggleForge.Sdk
{
    /// <summary>
    /// Guarda o snapshot num arquivo do diretório escolhido pelo jogo. O nome do arquivo vem de um hash da
    /// credencial: um cache de outro ambiente, deixado por um build anterior, nunca é lido por engano, e a
    /// credencial não vai para o disco.
    /// </summary>
    public sealed class FileSnapshotStore : ISnapshotStore
    {
        private const int Format = 1;

        // O formato "O" guarda o deslocamento e lê de volta exatamente o mesmo instante.
        private const string InstantFormat = "O";

        private readonly string _directory;
        private readonly string _path;

        public FileSnapshotStore(string directory, SdkCredential credential)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            if (credential is null)
            {
                throw new ArgumentNullException(nameof(credential));
            }

            _path = Path.Combine(directory, $"toggleforge-{Fingerprint(credential)}.json");
        }

        public async Task<StoredSnapshot?> LoadAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var text = await File.ReadAllTextAsync(_path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            return Parse(text);
        }

        public async Task SaveAsync(StoredSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            Directory.CreateDirectory(_directory);
            var temporary = _path + ".tmp";
            await File.WriteAllTextAsync(temporary, Serialize(snapshot), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

            // A troca é atômica no mesmo diretório: quem lê vê o arquivo antigo ou o novo, nunca a metade.
            if (File.Exists(_path))
            {
                File.Replace(temporary, _path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporary, _path);
            }
        }

        private static string Fingerprint(SdkCredential credential)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(credential.Value));
            return BitConverter.ToString(hash, 0, 16).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        }

        private static string Serialize(StoredSnapshot snapshot) =>
            new JObject
            {
                ["format"] = Format,
                ["eTag"] = snapshot.ETag,
                ["confirmedAt"] = snapshot.ConfirmedAt.ToString(InstantFormat, CultureInfo.InvariantCulture),
                ["snapshot"] = snapshot.Body,
            }.ToString(Formatting.None);

        /// <returns>Nulo quando o arquivo não é um cache que esta versão do SDK escreveu.</returns>
        private static StoredSnapshot? Parse(string text)
        {
            JObject stored;
            try
            {
                using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
                if (JToken.ReadFrom(reader) is not JObject root)
                {
                    return null;
                }

                stored = root;
            }
            catch (JsonReaderException)
            {
                return null;
            }

            if (stored["format"] is not JValue { Type: JTokenType.Integer, Value: long format } || format != Format
                || stored["snapshot"] is not JValue { Type: JTokenType.String, Value: string body }
                || stored["confirmedAt"] is not JValue { Type: JTokenType.String, Value: string confirmedAt }
                || !DateTimeOffset.TryParseExact(confirmedAt, InstantFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instant))
            {
                return null;
            }

            var eTag = stored["eTag"] is JValue { Type: JTokenType.String, Value: string storedETag } ? storedETag : null;
            return new StoredSnapshot(body, eTag, instant);
        }
    }
}
