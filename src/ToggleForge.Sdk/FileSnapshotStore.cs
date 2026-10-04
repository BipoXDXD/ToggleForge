using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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

        // O corpo vai escapado dentro do JSON do arquivo e pode crescer; acima disso, não é um arquivo nosso.
        private const long MaxFileBytes = 4L * HttpSnapshotTransport.MaxSnapshotBytes;

        // A forma que a API aceita em If-None-Match: uma ETag corrompida no disco travaria toda sincronização em 400.
        private static readonly Regex _eTagShape = new(@"^(W/)?""[A-Za-z0-9._-]{1,60}""$", RegexOptions.CultureInvariant);

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
            var file = new FileInfo(_path);
            if (!file.Exists || file.Length > MaxFileBytes)
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

            // Um temporário por gravação: dois clientes da mesma credencial (duas instâncias do jogo, um cliente que
            // sobrou no Editor da Unity) nunca escrevem no mesmo arquivo pela metade.
            var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, Serialize(snapshot), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                MoveIntoPlace(temporary);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        /// <summary>
        /// A troca é atômica no mesmo diretório: quem lê vê o arquivo antigo ou o novo, nunca a metade. Se outro
        /// cliente criar o arquivo entre a checagem e o <c>Move</c>, a troca vira <c>Replace</c>.
        /// </summary>
        private void MoveIntoPlace(string temporary)
        {
            if (File.Exists(_path))
            {
                File.Replace(temporary, _path, destinationBackupFileName: null);
                return;
            }

            try
            {
                File.Move(temporary, _path);
            }
            catch (IOException) when (File.Exists(_path))
            {
                File.Replace(temporary, _path, destinationBackupFileName: null);
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
            var stored = ReadObject(text);
            if (stored is null
                || stored["format"] is not JValue { Type: JTokenType.Integer, Value: long format } || format != Format
                || stored["snapshot"] is not JValue { Type: JTokenType.String, Value: string body }
                || !TryReadInstant(stored["confirmedAt"], out var confirmedAt)
                || !TryReadOptionalETag(stored["eTag"], out var eTag))
            {
                return null;
            }

            return new StoredSnapshot(body, eTag, confirmedAt);
        }

        /// <returns>Nulo quando o texto não é JSON ou não é um objeto.</returns>
        private static JObject? ReadObject(string text)
        {
            try
            {
                using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
                return JToken.ReadFrom(reader) as JObject;
            }
            catch (JsonReaderException)
            {
                return null;
            }
        }

        private static bool TryReadInstant(JToken? token, out DateTimeOffset instant)
        {
            instant = default;
            return token is JValue { Type: JTokenType.String, Value: string text }
                && DateTimeOffset.TryParseExact(text, InstantFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out instant);
        }

        /// <returns>
        /// Falso só quando há uma ETag em texto fora da forma que a API aceita. Ausente ou de outro tipo, ela fica
        /// nula e o snapshot vale sem ela.
        /// </returns>
        private static bool TryReadOptionalETag(JToken? token, out string? eTag)
        {
            eTag = token is JValue { Type: JTokenType.String, Value: string text } ? text : null;
            return eTag is null || _eTagShape.IsMatch(eTag);
        }
    }
}
